using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AudioHQ.App.ViewModels;
using AudioHQ.Core;

namespace AudioHQ.App;

public partial class MainWindow : Window
{
    private MixerViewModel? _viewModel;
    private TrayController? _tray;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"AudioHQ v{AppVersion.Display}";
        VersionText.Text = $"v{AppVersion.Display}";

        try
        {
            _viewModel = new MixerViewModel();
            DataContext = _viewModel;
            _tray = new TrayController(this,
                () => _viewModel.MinimizeToTray,
                () => _viewModel.CloseToTray);

            // Keep the tray hover text in sync with which outputs are ON/OFF.
            _viewModel.Channels.CollectionChanged += Channels_CollectionChanged;
            foreach (var channel in _viewModel.Channels)
                channel.PropertyChanged += Channel_PropertyChanged;
            RefreshTrayTooltip();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start audio engine:\n{ex.Message}", "AudioHQ",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    // --- Tray tooltip: live ON/OFF summary of the output channels ---------------

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

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        new OptionsWindow { Owner = this, DataContext = _viewModel }.ShowDialog();
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
