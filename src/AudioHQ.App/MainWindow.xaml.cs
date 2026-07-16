using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AudioHQ.App.ViewModels;
using AudioHQ.Core;

namespace AudioHQ.App;

public partial class MainWindow : Window
{
    private MixerViewModel? _viewModel;
    private AppMixerViewModel? _appMixer;
    private MainWindowTraySync? _traySync;
    private AppPanelAnimator? _appPanelAnimator;
    private AppRowDragController? _appRowDrag;
    private AppMixerWindow? _appMixerWindow;

    public bool LaunchMinimized => _viewModel?.TrayOptions.LaunchMinimized == true;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"AudioHQ v{AppVersion.Display}";
        VersionText.Text = $"v{AppVersion.Display}";

        try
        {
            _viewModel = new MixerViewModel();
            DataContext = _viewModel;
            RestoreWindowPosition();

            // Per-app mixer lives in its own view model, bound to the left panel only.
            _appMixer = new AppMixerViewModel(_viewModel.Settings, _viewModel.SaveSettings);
            AppMixerRegion.DataContext = _appMixer;
            AppPanel.DataContext = _appMixer;
            _appPanelAnimator = new AppPanelAnimator(this, AppPanel);
            _appRowDrag = new AppRowDragController(AppPanel, () => _appMixer);

            Loaded += (_, _) =>
            {
                if (_viewModel.Settings.AppMixerDetached) DetachAppMixer();
                else RestoreAttachedAppMixer(_viewModel.Settings.AppMixerExpanded);
            };

            _traySync = new MainWindowTraySync(this, _viewModel);

        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start audio engine:\n{ex.Message}", "AudioHQ",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        var window = OwnedWindows.OfType<OptionsWindow>().FirstOrDefault();
        if (window is null)
        {
            window = new OptionsWindow { Owner = this, DataContext = _viewModel };
            WindowPlacement.FollowOwner(window);
            window.Show();
        }
        else
        {
            if (!window.IsVisible) window.Show();
            window.Activate();
        }
    }

    // --- Per-app mixer panel (left slide-out) ----------------------------------

    // Toggle the panel open/closed; the setter refreshes the list when it opens.
    private void ToggleAppPanel_Click(object sender, RoutedEventArgs e)
    {
        if (_appMixer is null) return;
        if (_appMixerWindow is not null)
        {
            if (_appMixerWindow.IsVisible)
            {
                _appMixerWindow.Hide();
                _appMixer.IsExpanded = false;
            }
            else
            {
                _appMixer.IsExpanded = true;
                _appMixerWindow.Show();
                _appMixerWindow.Activate();
            }
            return;
        }
        bool expand = !_appMixer.IsExpanded;
        _appMixer.IsExpanded = expand;
        _appPanelAnimator?.Animate(expand);
    }

    private void RestoreWindowPosition()
    {
        if (_viewModel?.Settings is not { MainWindowLeft: double left, MainWindowTop: double top }) return;
        if (!double.IsFinite(left) || !double.IsFinite(top)) return;

        const double visibleEdge = 50;
        bool intersectsVirtualDesktop =
            left + visibleEdge >= SystemParameters.VirtualScreenLeft &&
            left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - visibleEdge &&
            top + visibleEdge >= SystemParameters.VirtualScreenTop &&
            top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - visibleEdge;
        if (!intersectsVirtualDesktop) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
    }

    private void SaveWindowPosition()
    {
        if (_viewModel is null) return;
        Rect placement = WindowState == WindowState.Normal ?
            new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (!double.IsFinite(placement.Left) || !double.IsFinite(placement.Top)) return;
        _viewModel.Settings.MainWindowLeft = placement.Left;
        _viewModel.Settings.MainWindowTop = placement.Top;
        _viewModel.SaveSettings();
    }

    private void AppMixerDock_Click(object sender, RoutedEventArgs e)
    {
        if (_appMixerWindow is null) DetachAppMixer();
        else AttachAppMixer();
    }

    private void RestoreAttachedAppMixer(bool expanded)
    {
        if (_appMixer is null) return;
        AppPanel.BeginAnimation(WidthProperty, null);
        AppPanel.BeginAnimation(MarginProperty, null);
        AppPanel.Width = expanded ? (double)FindResource("AppMixerPanelWidth") : 0;
        AppPanel.Margin = expanded
            ? (Thickness)FindResource("AppMixerPanelOpenMargin")
            : (Thickness)FindResource("AppMixerPanelClosedMargin");
        _appMixer.IsExpanded = expanded;
    }

    private void DetachAppMixer()
    {
        if (_appMixer is null || _viewModel is null || _appMixerWindow is not null) return;
        AppPanel.BeginAnimation(WidthProperty, null);
        AppPanel.BeginAnimation(MarginProperty, null);
        AppMixerRegion.Children.Remove(AppPanel);
        AppPanel.Width = (double)FindResource("AppMixerPanelWidth");
        AppPanel.Margin = new Thickness(0);
        _appMixer.IsExpanded = true;
        _appMixerWindow = new AppMixerWindow(this, AppPanel, AppMixerItems, _appMixer);
        _appMixerWindow.IsVisibleChanged += (_, _) =>
            _appMixer.IsExpanded = _appMixerWindow?.IsVisible == true;
        _viewModel.Settings.AppMixerDetached = true;
        _viewModel.SaveSettings();
        AppMixerDockButton.ToolTip = "Attach mixer";
        AppMixerDetachIcon.Visibility = Visibility.Collapsed;
        AppMixerAttachIcon.Visibility = Visibility.Visible;
        _appMixerWindow.Show();
    }

    private void AttachAppMixer()
    {
        if (_appMixerWindow is null || _viewModel is null) return;
        _appMixerWindow.ReleaseMixer();
        _appMixerWindow.ClosePermanently();
        _appMixerWindow = null;
        AppMixerRegion.Children.Add(AppPanel);
        DockPanel.SetDock(AppPanel, Dock.Left);
        AppPanel.Width = (double)FindResource("AppMixerPanelWidth");
        AppPanel.Margin = (Thickness)FindResource("AppMixerPanelOpenMargin");
        _appMixer!.IsExpanded = true;
        _viewModel.Settings.AppMixerDetached = false;
        _viewModel.SaveSettings();
        AppMixerDockButton.ToolTip = "Detach mixer";
        AppMixerAttachIcon.Visibility = Visibility.Collapsed;
        AppMixerDetachIcon.Visibility = Visibility.Visible;
    }

    // Drag-reorder an app row. The drag handle (bottom-left of each row) starts the drag;
    // a ghost adorner follows the cursor and the source row is dimmed while dragging.
    private void AppRow_DragStart(object sender, MouseButtonEventArgs e)
    {
        _appRowDrag?.Start(sender, e);
    }

    private void AppRow_DragOver(object sender, DragEventArgs e)
    {
        _appRowDrag?.DragOver(sender, e);
    }

    private void AppRow_Drop(object sender, DragEventArgs e)
    {
        _appRowDrag?.Drop(sender, e);
    }

    // Open the graphic-EQ editor for the channel whose EQ pill was clicked.
    private void ChannelEq_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ChannelViewModel channel)
        {
            var window = OwnedWindows.OfType<EqWindow>()
                .FirstOrDefault(w => ReferenceEquals(w.DataContext, channel));
            if (window is null)
            {
                window = new EqWindow { Owner = this, DataContext = channel };
                WindowPlacement.FollowOwner(window);
                window.Show();
            }
            else
            {
                if (!window.IsVisible) window.Show();
                window.Activate();
            }
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
        ChannelDragDropController.StartDrag(sender, e);
    }

    private void Channel_DragOver(object sender, DragEventArgs e)
    {
        ChannelDragDropController.DragOver(e);
    }

    private void Channel_Drop(object sender, DragEventArgs e)
    {
        if (_viewModel is null) return;
        ChannelDragDropController.Drop(_viewModel, sender, e);
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
        if (sender is TextBox box) RenameTextBoxController.FocusWhenVisible(box);
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        e.Handled = RenameTextBoxController.HandleKeyDown(box, e, () =>
        {
            if (box.DataContext is ChannelViewModel channel) channel.IsEditing = false;
        });
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) CommitRename(box);
    }

    private static void CommitRename(TextBox box)
    {
        RenameTextBoxController.Commit(box, () =>
        {
            if (box.DataContext is ChannelViewModel channel) channel.IsEditing = false;
        });
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

    protected override void OnDeactivated(EventArgs e)
    {
        // Losing focus is a natural checkpoint: the user may not touch the app again before
        // it is killed or minimized to tray for hours, so do not wait out the debounce.
        _viewModel?.FlushPendingSave();
        base.OnDeactivated(e);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        SaveWindowPosition();
        // Close-to-tray (when enabled) hides instead of exiting; the tray Exit item
        // bypasses this and lets the window really close.
        if (_traySync?.HandleClosing(e) == true) return;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_appMixerWindow is not null)
        {
            _appMixerWindow.ReleaseMixer();
            _appMixerWindow.ClosePermanently();
        }
        _traySync?.Dispose();
        _appMixer?.Dispose();
        _viewModel?.Dispose();
        base.OnClosed(e);
    }
}
