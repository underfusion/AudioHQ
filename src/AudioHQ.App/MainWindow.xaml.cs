using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AudioHQ.App.ViewModels;
using AudioHQ.Core;

namespace AudioHQ.App;

public partial class MainWindow : Window
{
    private MixerViewModel? _viewModel;
    private AppMixerViewModel? _appMixer;
    private TrayController? _tray;
    private BitmapSource? _windowIconBase;
    private BitmapSource? _windowIconDot;

    private DragAdorner? _dragAdorner;
    private AdornerLayer? _adornerLayer;
    private Border? _dragSourceRow;
    private Border? _dropTarget;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public MainWindow()
    {
        InitializeComponent();
        Title = $"AudioHQ v{AppVersion.Display}";
        VersionText.Text = $"v{AppVersion.Display}";

        try
        {
            _viewModel = new MixerViewModel();
            DataContext = _viewModel;

            // Per-app mixer lives in its own view model, bound to the left panel only.
            _appMixer = new AppMixerViewModel(_viewModel.Settings, _viewModel.SaveSettings);
            AppMixerRegion.DataContext = _appMixer;
            // Re-read the app sessions whenever the window comes to the front (restore from tray,
            // alt-tab back) while the panel is open, so the list stays current.
            Activated += MainWindow_Activated;

            _tray = new TrayController(this,
                () => _viewModel.MinimizeToTray,
                () => _viewModel.CloseToTray,
                () => ToggleFocusedChannel());

            // Pre-build the two taskbar icon variants (base + green-dot overlay).
            _windowIconBase = BuildWindowIcon(false);
            _windowIconDot = BuildWindowIcon(true);
            if (_windowIconBase is not null) Icon = _windowIconBase;

            // Keep the tray hover text and focus-state icon in sync.
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            _viewModel.Channels.CollectionChanged += Channels_CollectionChanged;
            foreach (var channel in _viewModel.Channels)
                channel.PropertyChanged += Channel_PropertyChanged;
            RefreshTrayTooltip();
            RefreshTrayFocusState();

            // Pin the master's green line and "100" label to the thumb centre at full scale, once
            // the slider template has been realised (and on any resize).
            Loaded += (_, _) => Dispatcher.BeginInvoke(
                new Action(PositionMasterUnityLine), System.Windows.Threading.DispatcherPriority.Loaded);
            MasterFader.SizeChanged += (_, _) => PositionMasterUnityLine();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start audio engine:\n{ex.Message}", "AudioHQ",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    // --- Tray tooltip, focus-state icon, and middle-click toggle -----------------

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MixerViewModel.FocusedChannel))
            RefreshTrayFocusState();
    }

    private void Channels_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ChannelViewModel c in e.OldItems) c.PropertyChanged -= Channel_PropertyChanged;
        if (e.NewItems is not null)
            foreach (ChannelViewModel c in e.NewItems) c.PropertyChanged += Channel_PropertyChanged;
        RefreshTrayTooltip();
    }

    private void Channel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChannelViewModel.IsActive) or nameof(ChannelViewModel.Name))
            RefreshTrayTooltip();
        if (e.PropertyName == nameof(ChannelViewModel.IsActive)
            && sender is ChannelViewModel c && c == _viewModel?.FocusedChannel)
            RefreshTrayFocusState();
    }

    private void RefreshTrayTooltip()
    {
        if (_viewModel is null || _tray is null) return;
        var on = _viewModel.Channels.Where(c => c.IsActive).Select(c => c.Name).ToList();
        var off = _viewModel.Channels.Where(c => !c.IsActive).Select(c => c.Name).ToList();
        static string Join(System.Collections.Generic.List<string> names) =>
            names.Count == 0 ? "-" : string.Join(", ", names);
        _tray.SetTooltip($"AudioHQ\nON: {Join(on)}\nOFF: {Join(off)}");
    }

    private void RefreshTrayFocusState()
    {
        if (_viewModel is null || _tray is null) return;
        var isActive = _viewModel.FocusedChannel?.IsActive;
        _tray.SetFocusedChannelState(isActive);
        if (_windowIconBase is not null && _windowIconDot is not null)
            Icon = isActive == true ? _windowIconDot : _windowIconBase;
    }

    private void ToggleFocusedChannel()
    {
        var channel = _viewModel?.FocusedChannel;
        if (channel is null) return;
        channel.IsActive = !channel.IsActive;
    }

    private BitmapSource? BuildWindowIcon(bool dot)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = Array.Find(asm.GetManifestResourceNames(),
                n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
            System.Drawing.Icon? baseIcon = null;
            if (name is not null)
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream is not null) baseIcon = new System.Drawing.Icon(stream, 32, 32);
            }
            using var bmp = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.Transparent);
                if (baseIcon is not null)
                {
                    using var baseBmp = baseIcon.ToBitmap();
                    g.DrawImage(baseBmp, 0, 0, 32, 32);
                }
                if (dot)
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.FillEllipse(System.Drawing.Brushes.LimeGreen, 20, 20, 11, 11);
                }
            }
            baseIcon?.Dispose();
            var hBmp = bmp.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    hBmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            finally { DeleteObject(hBmp); }
        }
        catch (Exception ex)
        {
            Log.Write($"BuildWindowIcon(dot={dot}) failed: {ex.Message}");
            return null;
        }
    }

    // The master tops out at 100%, so 100% is the top of the track. Pin the green unity line (and
    // the "100" label beside it) to the thumb centre at full scale, derived from the thumb's
    // rendered position, so the thumb rests on the line at 100% and overhangs slightly above it.
    private void PositionMasterUnityLine()
    {
        MasterFader.ApplyTemplate();
        MasterFader.UpdateLayout();
        if (MasterFader.Template?.FindName("PART_Track", MasterFader) is not Track track) return;
        if (track.Thumb is not { ActualHeight: > 0 } thumb || MasterUnityLine.Parent is not UIElement parent) return;

        double range = MasterFader.Maximum - MasterFader.Minimum;
        if (range <= 0) return;

        Point thumbCentre = thumb.TranslatePoint(new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2), parent);
        double travel = Math.Max(0, track.ActualHeight - thumb.ActualHeight);
        double unityY = thumbCentre.Y - (MasterFader.Maximum - MasterFader.Value) / range * travel;

        MasterUnityLine.Margin = new Thickness(0, unityY - MasterUnityLine.Height / 2, 0, 0);
        // The label canvas sits 10px below the grid top; centre the "100" text on the line.
        Canvas.SetTop(MasterTopLabel, unityY - 10 - MasterTopLabel.ActualHeight / 2);
    }

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        new OptionsWindow { Owner = this, DataContext = _viewModel }.ShowDialog();
    }

    // --- Per-app mixer panel (left slide-out) ----------------------------------

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        if (_appMixer?.IsExpanded == true) _appMixer.Refresh();
    }

    // Toggle the panel open/closed; the setter refreshes the list when it opens.
    private void ToggleAppPanel_Click(object sender, RoutedEventArgs e)
    {
        if (_appMixer is null) return;
        bool expand = !_appMixer.IsExpanded;
        _appMixer.IsExpanded = expand;
        AnimateAppPanel(expand);
    }

    private void AnimateAppPanel(bool expand)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(180));
        var ease = new System.Windows.Media.Animation.CubicEase
            { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };

        var panelWidth = (double)FindResource("AppMixerPanelWidth");
        AppPanel.BeginAnimation(WidthProperty,
            new System.Windows.Media.Animation.DoubleAnimation
            {
                To = expand ? panelWidth : 0,
                Duration = duration,
                EasingFunction = ease,
            });

        // Keep the animated panel margins aligned with the app-mixer spacing tokens.
        var openMargin = (Thickness)FindResource("AppMixerPanelOpenMargin");
        var closedMargin = (Thickness)FindResource("AppMixerPanelClosedMargin");
        AppPanel.BeginAnimation(MarginProperty,
            new System.Windows.Media.Animation.ThicknessAnimation
            {
                To = expand ? openMargin : closedMargin,
                Duration = duration,
                EasingFunction = ease,
            });
    }

    // Drag-reorder an app row. The drag handle (bottom-left of each row) starts the drag;
    // a ghost adorner follows the cursor and the source row is dimmed while dragging.
    private void AppRow_DragStart(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not AppSessionViewModel app) return;

        // Walk up to the tagged row Border.
        Border? row = null;
        DependencyObject? curr = fe;
        while (curr != null)
        {
            curr = VisualTreeHelper.GetParent(curr);
            if (curr is Border b && b.Tag is "AppRow") { row = b; break; }
        }

        if (row != null)
        {
            _adornerLayer = AdornerLayer.GetAdornerLayer(AppPanel);
            if (_adornerLayer != null)
            {
                // Snapshot the row at full opacity before dimming it.
                var dpi = VisualTreeHelper.GetDpi(row);
                int w = Math.Max(1, (int)Math.Round(row.ActualWidth * dpi.DpiScaleX));
                int h = Math.Max(1, (int)Math.Round(row.ActualHeight * dpi.DpiScaleY));
                var bmp = new RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                bmp.Render(row);
                bmp.Freeze();

                var ghost = new Rectangle
                {
                    Width = row.ActualWidth,
                    Height = row.ActualHeight,
                    RadiusX = 8,
                    RadiusY = 8,
                    Fill = new ImageBrush(bmp) { Stretch = Stretch.Fill },
                    IsHitTestVisible = false,
                    Effect = new DropShadowEffect
                    {
                        BlurRadius = 12,
                        ShadowDepth = 4,
                        Opacity = 0.55,
                        Direction = 270,
                        Color = Colors.Black,
                    },
                };
                var clickPos = e.GetPosition(row);
                _dragAdorner = new DragAdorner(AppPanel, ghost, clickPos.X, clickPos.Y);
                _adornerLayer.Add(_dragAdorner);
                var initPos = e.GetPosition(AppPanel);
                _dragAdorner.UpdatePosition(initPos.X, initPos.Y);
            }

            _dragSourceRow = row;
            row.Opacity = 0.35;
        }

        GiveFeedbackEventHandler giveFeedback = (_, gev) =>
        {
            gev.UseDefaultCursors = false;
            Mouse.SetCursor(Cursors.SizeAll);
            if (_dragAdorner != null && GetCursorPos(out POINT pt))
            {
                var rel = AppPanel.PointFromScreen(new Point(pt.X, pt.Y));
                _dragAdorner.UpdatePosition(rel.X, rel.Y);
            }
            gev.Handled = true;
        };
        DragDrop.AddGiveFeedbackHandler(fe, giveFeedback);

        e.Handled = true;
        try
        {
            DragDrop.DoDragDrop(fe, app, DragDropEffects.Move);
        }
        finally
        {
            DragDrop.RemoveGiveFeedbackHandler(fe, giveFeedback);
            ClearDropHighlight();
            if (_dragAdorner != null && _adornerLayer != null)
            {
                _adornerLayer.Remove(_dragAdorner);
                _dragAdorner = null;
                _adornerLayer = null;
            }
            if (_dragSourceRow != null)
            {
                _dragSourceRow.Opacity = 1.0;
                _dragSourceRow = null;
            }
        }
    }

    private void AppRow_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(AppSessionViewModel)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Move;

        if (sender is Border target && !ReferenceEquals(target, _dropTarget)
            && !ReferenceEquals(target, _dragSourceRow))
        {
            ClearDropHighlight();
            _dropTarget = target;
            target.BorderThickness = new Thickness(0, 2, 0, 0);
            target.BorderBrush = (Brush)Application.Current.Resources["AccentBlue"];
        }

        e.Handled = true;
    }

    private void AppRow_Drop(object sender, DragEventArgs e)
    {
        ClearDropHighlight();
        if (_appMixer is null) return;
        if (e.Data.GetData(typeof(AppSessionViewModel)) is not AppSessionViewModel source) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not AppSessionViewModel target) return;
        _appMixer.MoveApp(source, target);
        e.Handled = true;
    }

    private void ClearDropHighlight()
    {
        if (_dropTarget == null) return;
        _dropTarget.BorderThickness = new Thickness(0);
        _dropTarget.BorderBrush = Brushes.Transparent;
        _dropTarget = null;
    }

    // Open the graphic-EQ editor for the channel whose EQ pill was clicked.
    private void ChannelEq_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ChannelViewModel channel)
            new EqWindow { Owner = this, DataContext = channel }.ShowDialog();
    }

    // Double-click the master fader to snap it back to 100% (unity).
    private void Fader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is Slider slider)
        {
            slider.Value = 1.0;
            e.Handled = true;
        }
    }

    // Double-click anywhere in a channel fader zone to snap the gain to 100%.
    private void FaderZone_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        var slider = FindVisualChild<Slider>(sender as DependencyObject);
        if (slider is not null)
        {
            slider.Value = 1.0;
            e.Handled = true;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null) return null;
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var found = FindVisualChild<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    // --- Drag-and-drop reorder -------------------------------------------------

    private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ChannelViewModel channel)
        {
            DragDrop.DoDragDrop(fe, channel, DragDropEffects.Move);
            e.Handled = true;
        }
    }

    private void Channel_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ChannelViewModel))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Channel_Drop(object sender, DragEventArgs e)
    {
        if (_viewModel is null) return;
        if (e.Data.GetData(typeof(ChannelViewModel)) is not ChannelViewModel source) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not ChannelViewModel target) return;
        if (ReferenceEquals(source, target)) return;

        int from = _viewModel.Channels.IndexOf(source);
        int to = _viewModel.Channels.IndexOf(target);
        _viewModel.MoveChannel(from, to);
        e.Handled = true;
    }

    // --- Inline rename ---------------------------------------------------------

    private void Name_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement fe && fe.DataContext is ChannelViewModel channel)
        {
            channel.IsEditing = true;
            e.Handled = true;
        }
    }

    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // The box is created collapsed; focus/select only once it is shown for editing.
        if (sender is TextBox box && box.IsVisible)
        {
            box.Dispatcher.BeginInvoke(new Action(() => { box.Focus(); box.SelectAll(); }),
                System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (e.Key == Key.Enter)
        {
            CommitRename(box);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Discard the edit: revert the box to the bound value, then close.
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            if (box.DataContext is ChannelViewModel channel) channel.IsEditing = false;
            e.Handled = true;
        }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) CommitRename(box);
    }

    private static void CommitRename(TextBox box)
    {
        // LostFocus binding pushes the new name; just leave edit mode.
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (box.DataContext is ChannelViewModel channel) channel.IsEditing = false;
    }

    // --- Master rename (same pattern, but on the MixerViewModel) ----------------

    private void MasterName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && _viewModel is not null)
        {
            _viewModel.IsEditingMaster = true;
            e.Handled = true;
        }
    }

    private void MasterRenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (e.Key == Key.Enter)
        {
            CommitMasterRename(box);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            if (_viewModel is not null) _viewModel.IsEditingMaster = false;
            e.Handled = true;
        }
    }

    private void MasterRenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) CommitMasterRename(box);
    }

    private void CommitMasterRename(TextBox box)
    {
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (_viewModel is not null) _viewModel.IsEditingMaster = false;
    }

    // --- Add channel -----------------------------------------------------------

    private void AddChannel_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || sender is not Button button) return;

        var devices = _viewModel.GetAvailableDevices();
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };

        if (devices.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No more devices available", IsEnabled = false });
        }
        else
        {
            foreach (var device in devices)
            {
                var captured = device;
                var item = new MenuItem { Header = device.FriendlyName };
                item.Click += (_, _) => _viewModel.AddChannel(captured);
                menu.Items.Add(item);
            }
        }

        menu.IsOpen = true;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Close-to-tray (when enabled) hides instead of exiting; the tray Exit item
        // bypasses this and lets the window really close.
        if (_tray?.HandleClosing(e) == true) return;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray?.Dispose();
        _viewModel?.Dispose();
        base.OnClosed(e);
    }
}
