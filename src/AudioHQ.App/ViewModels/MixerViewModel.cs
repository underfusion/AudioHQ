using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AudioHQ.App;
using AudioHQ.Core;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace AudioHQ.App.ViewModels;

public sealed record LatencyPreset(string Name, int Ms)
{
    public override string ToString() => Name;
}

/// <summary>
/// Root view model: source selection (= master, the source device's Windows volume),
/// latency, and a user-curated, persisted list of named output channels.
/// </summary>
public sealed class MixerViewModel : ViewModelBase, IDisposable
{
    private readonly MirrorEngine _engine = new();
    private readonly MixerSettings _settings;
    private readonly EqPresetStore _eqPresets;
    private readonly DispatcherTimer _healthTimer;
    private readonly MixerStatusViewModel _status = new();
    private readonly MixerTrayOptionsViewModel _trayOptions;
    private readonly MixerChannelCollectionViewModel _channelCollection;
    private readonly MixerMasterViewModel _master;
    private readonly MixerSourceRecoveryViewModel _sourceRecovery;
    private LatencyPreset _selectedLatency;
    private bool _loaded;
    private bool _dirty;

    public ObservableCollection<MMDevice> Sources { get; } = new();
    public ObservableCollection<ChannelViewModel> Channels => _channelCollection.Channels;

    public MixerSettings Settings => _settings;
    public MixerStatusViewModel Status => _status;
    public MixerTrayOptionsViewModel TrayOptions => _trayOptions;
    public MixerMasterViewModel Master => _master;

    public ICommand RemoveChannelCommand { get; }

    /// <summary>Closes the status notification bubble (the "X" on it).</summary>
    public ICommand DismissStatusCommand { get; }

    /// <summary>Selects (or deselects) a channel as the tray-focus target.</summary>
    public ICommand FocusChannelCommand { get; }

    /// <summary>The channel currently selected to drive the tray icon and middle-click toggle, or null.</summary>
    public ChannelViewModel? FocusedChannel => _channelCollection.FocusedChannel;

    public LatencyPreset[] LatencyPresets { get; } =
    {
        new("Ultra (15 ms)", 15),
        new("Low (30 ms)", 30),
        new("Balanced (60 ms)", 60),
        new("Safe (100 ms)", 100),
    };

    public MixerViewModel()
    {
        _settings = MixerSettings.Load();
        _eqPresets = new EqPresetStore(_settings.EqPresets, Save);
        _selectedLatency = LatencyPresets.FirstOrDefault(p => p.Ms == _settings.LatencyMs)
                           ?? LatencyPresets[1];
        _trayOptions = new MixerTrayOptionsViewModel(_settings, Save);
        _channelCollection = new MixerChannelCollectionViewModel(
            _engine,
            Sources,
            () => SelectedSource?.ID,
            () => _selectedLatency.Ms,
            MarkDirty,
            _eqPresets,
            Save);
        _sourceRecovery = new MixerSourceRecoveryViewModel(
            _engine,
            Sources,
            Channels,
            SetStatus,
            ClearStatus,
            Save,
            NotifySourceChanged);
        _master = new MixerMasterViewModel(_settings, () => _sourceRecovery.SelectedSource, () => SourceName, Save);
        _channelCollection.PropertyChanged += ChannelCollection_PropertyChanged;
        RemoveChannelCommand = _channelCollection.RemoveChannelCommand;
        DismissStatusCommand = new RelayCommand(_ => ClearStatus());
        FocusChannelCommand = _channelCollection.FocusChannelCommand;
        _engine.SourceLost += OnEngineSourceLost;

        foreach (var device in AudioDevices.GetActiveRenderDevices())
            Sources.Add(device);

        _sourceRecovery.Initialize(_settings.SourceDeviceId);

        BuildChannels(_sourceRecovery.SelectedSource?.ID);
        if (_sourceRecovery.SelectedSource is not null)
            _sourceRecovery.RestartEngine(_sourceRecovery.SelectedSource); // also restores the channels saved as ON

        _loaded = true;

        // Resume hint. Known to be missed on some Modern Standby machines, hence the
        // clock-jump fallback in HealthCheck.
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // Background watchdog: keeps the device list current, recovers the engine if the
        // source drops out and reactivates wanted channels (see HealthCheck).
        _healthTimer = new DispatcherTimer { Interval = MixerSourceRecoveryViewModel.HealthInterval };
        _healthTimer.Tick += (_, _) => _sourceRecovery.HealthCheck();
        _healthTimer.Start();
    }

    // --- Source-loss handling & background recovery -----------------------------

    /// <summary>
    /// Engine callback (may be off the UI thread) for an unsolicited capture stop. Marshals to
    /// the UI thread and kicks off recovery.
    /// </summary>
    private void OnEngineSourceLost(Exception? error)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(new Action(() => HandleSourceLost(error)));
        else
            HandleSourceLost(error);
    }

    private void HandleSourceLost(Exception? error)
    {
        _sourceRecovery.HandleSourceLost(error);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(new Action(_sourceRecovery.BeginResumeRecovery));
        else
            _sourceRecovery.BeginResumeRecovery();
    }

    private void NotifySourceChanged()
    {
        OnPropertyChanged(nameof(SelectedSource));
        OnPropertyChanged(nameof(SourceName));
        _master.NotifySourceChanged();
    }

    private void BuildChannels(string? sourceId) => _channelCollection.Build(_settings.Channels, sourceId);

    public LatencyPreset SelectedLatency
    {
        get => _selectedLatency;
        set
        {
            if (value is null || value == _selectedLatency) return;
            _selectedLatency = value;
            Log.Write($"Latency preset -> {value.Name}");
            OnPropertyChanged();

            // Re-open active channels so the new buffer size takes effect.
            foreach (var channel in Channels.Where(c => c.IsActive))
            {
                channel.IsActive = false;
                channel.IsActive = true;
            }
            Save();
        }
    }

    public MMDevice? SelectedSource
    {
        get => _sourceRecovery.SelectedSource;
        set
        {
            if (value is null) return;
            _sourceRecovery.SelectSource(value);
        }
    }

    public string SourceName => _sourceRecovery.SourceName;

    /// <summary>Devices not yet used as a channel and not the current source - candidates to add.</summary>
    public IReadOnlyList<MMDevice> GetAvailableDevices() => _channelCollection.GetAvailableDevices();

    public void AddChannel(MMDevice device) => _channelCollection.AddChannel(device);

    public void MoveChannel(int oldIndex, int newIndex) => _channelCollection.MoveChannel(oldIndex, newIndex);

    private void ChannelCollection_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MixerChannelCollectionViewModel.FocusedChannel))
            OnPropertyChanged(nameof(FocusedChannel));
    }

    /// <summary>Show a notification. <paramref name="isError"/> picks the red vs. blue styling.</summary>
    private void SetStatus(string message, bool isError) => _status.Set(message, isError);

    /// <summary>Hide the notification bubble.</summary>
    private void ClearStatus() => _status.Clear();

    private void MarkDirty() => _dirty = true;

    private void Save()
    {
        if (!_loaded) return;
        MixerSettingsProjection.Apply(
            _settings,
            _sourceRecovery.PreferredSourceId,
            _selectedLatency.Ms,
            Channels.Select(c => c.ToDefinition()),
            _eqPresets.Persistable);
        _settings.Save();
        _dirty = false;
    }

    public void SaveSettings() => Save();

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _healthTimer?.Stop();
        _channelCollection.PropertyChanged -= ChannelCollection_PropertyChanged;
        _engine.SourceLost -= OnEngineSourceLost;
        if (_dirty) Save();
        _engine.Dispose();
    }
}
