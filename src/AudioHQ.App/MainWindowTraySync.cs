using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using AudioHQ.App.ViewModels;

namespace AudioHQ.App;

/// <summary>Keeps tray tooltip, tray focus overlay, and taskbar focus overlay in sync.</summary>
public sealed class MainWindowTraySync : IDisposable
{
    private readonly Window _window;
    private readonly MixerViewModel _viewModel;
    private readonly TrayController _tray;
    private readonly BitmapSource? _windowIconBase;
    private readonly BitmapSource? _windowIconDot;

    public MainWindowTraySync(Window window, MixerViewModel viewModel)
    {
        _window = window;
        _viewModel = viewModel;
        _tray = new TrayController(window,
            () => _viewModel.TrayOptions.MinimizeToTray,
            () => _viewModel.TrayOptions.CloseToTray,
            ToggleFocusedChannel);

        _windowIconBase = WindowIconFactory.Build(dot: false);
        _windowIconDot = WindowIconFactory.Build(dot: true);
        if (_windowIconBase is not null) _window.Icon = _windowIconBase;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.Channels.CollectionChanged += Channels_CollectionChanged;
        foreach (var channel in _viewModel.Channels)
            channel.PropertyChanged += Channel_PropertyChanged;

        RefreshTooltip();
        RefreshFocusState();
    }

    public bool HandleClosing(CancelEventArgs e) => _tray.HandleClosing(e);

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MixerViewModel.FocusedChannel))
            RefreshFocusState();
    }

    private void Channels_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ChannelViewModel c in e.OldItems) c.PropertyChanged -= Channel_PropertyChanged;
        if (e.NewItems is not null)
            foreach (ChannelViewModel c in e.NewItems) c.PropertyChanged += Channel_PropertyChanged;
        RefreshTooltip();
    }

    private void Channel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChannelViewModel.IsActive) or nameof(ChannelViewModel.Name))
            RefreshTooltip();
        if (e.PropertyName == nameof(ChannelViewModel.IsActive)
            && sender is ChannelViewModel c && c == _viewModel.FocusedChannel)
            RefreshFocusState();
    }

    private void RefreshTooltip()
    {
        var on = _viewModel.Channels.Where(c => c.IsActive).Select(c => c.Name).ToList();
        var off = _viewModel.Channels.Where(c => !c.IsActive).Select(c => c.Name).ToList();
        static string Join(System.Collections.Generic.List<string> names) =>
            names.Count == 0 ? "-" : string.Join(", ", names);
        _tray.SetTooltip($"AudioHQ\nON: {Join(on)}\nOFF: {Join(off)}");
    }

    private void RefreshFocusState()
    {
        var isActive = _viewModel.FocusedChannel?.IsActive;
        _tray.SetFocusedChannelState(isActive);
        if (_windowIconBase is not null && _windowIconDot is not null)
            _window.Icon = isActive == true ? _windowIconDot : _windowIconBase;
    }

    private void ToggleFocusedChannel()
    {
        var channel = _viewModel.FocusedChannel;
        if (channel is null) return;
        channel.IsActive = !channel.IsActive;
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.Channels.CollectionChanged -= Channels_CollectionChanged;
        foreach (var channel in _viewModel.Channels)
            channel.PropertyChanged -= Channel_PropertyChanged;
        _tray.Dispose();
    }
}
