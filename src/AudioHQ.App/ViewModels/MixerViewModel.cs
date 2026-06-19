using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AudioHQ.App;
using AudioHQ.Core;
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
    private MMDevice? _selectedSource;
    private string _engineStatus = "";
    private bool _engineStatusIsError;
    private LatencyPreset _selectedLatency;
    private bool _loaded;
    private bool _dirty;
    private bool _isEditingMaster;
    private bool _recovering;

    // The source the USER chose (persisted as SourceDeviceId). Distinct from _selectedSource,
    // which is whatever is actually live and may be a fallback when the chosen device is not
    // ready yet (e.g. Bluetooth earbuds still connecting right after a PC restart). Only an
    // explicit pick changes this; a fallback never overwrites it, so the preference survives.
    private string? _preferredSourceId;

    // Device ids that would not start as a source (e.g. locked in exclusive mode). Skipped by the
    // switch-back watchdog until they disappear and reappear, so we never spin retrying a bad one.
    private readonly HashSet<string> _unstartableSources = new();

    /// <summary>How often the background watchdog re-checks devices and engine health.</summary>
    private static readonly TimeSpan HealthInterval = TimeSpan.FromSeconds(3);

    public ObservableCollection<MMDevice> Sources { get; } = new();
    public ObservableCollection<ChannelViewModel> Channels { get; } = new();

    public ICommand RemoveChannelCommand { get; }

    /// <summary>Closes the status notification bubble (the "X" on it).</summary>
    public ICommand DismissStatusCommand { get; }

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
        RemoveChannelCommand = new RelayCommand(p => RemoveChannel(p as ChannelViewModel));
        DismissStatusCommand = new RelayCommand(_ => ClearStatus());
        _engine.SourceLost += OnEngineSourceLost;

        foreach (var device in AudioDevices.GetActiveRenderDevices())
            Sources.Add(device);

        _selectedLatency = LatencyPresets.FirstOrDefault(p => p.Ms == _settings.LatencyMs)
                           ?? LatencyPresets[1];

        _preferredSourceId = _settings.SourceDeviceId;

        var source = ResolveSource();
        _selectedSource = source;

        BuildChannels(source?.ID);
        if (source is not null)
        {
            RestartEngine(source);
            // Restore the ON channels from last session now that capture is running.
            foreach (var channel in Channels.Where(c => c.PendingActive && c.IsAvailable))
                channel.IsActive = true;
        }

        _loaded = true;

        // Keep the HKCU Run entry in step with the saved preference (refreshes the
        // exe path if the app was moved since it was first enabled).
        StartupRegistration.Set(_settings.RunWithWindows);

        // Background watchdog: keeps the device list current and recovers the engine if the
        // source ever drops out (see OnEngineSourceLost / HealthCheck).
        _healthTimer = new DispatcherTimer { Interval = HealthInterval };
        _healthTimer.Tick += (_, _) => HealthCheck();
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
        Log.Write($"MixerViewModel: source lost ({error?.Message ?? "device removed"}), recovering");
        SetStatus($"Source '{SourceName}' disconnected - recovering...", isError: false);
        TryRecover();
    }

    /// <summary>
    /// Periodic watchdog: refresh the device list and, if capture has died or the source device
    /// has vanished without a clean stop, try to recover. A healthy engine is left untouched.
    /// </summary>
    private void HealthCheck()
    {
        RefreshDevices();

        bool sourceGone = _engine.Source is not null
                          && Sources.All(d => d.ID != _engine.Source.ID);

        if (_engine.IsCapturing && !sourceGone)
        {
            // Healthy. If we are only on a fallback because the chosen source was not ready
            // earlier, switch back to it now that it has reappeared.
            TrySwitchToPreferred();
            return;
        }

        if (Sources.Count == 0)
        {
            SetStatus("No audio output device available. Connect a device.", isError: true);
            return;
        }

        Log.Write($"HealthCheck: recovery needed (capturing={_engine.IsCapturing}, sourceGone={sourceGone})");
        TryRecover();
    }

    /// <summary>
    /// Re-resolve a live source (the saved one if it is back, otherwise the current default) and
    /// restart the engine on it, restoring the channels that were ON. Reports what happened.
    /// </summary>
    private void TryRecover()
    {
        if (_recovering) return;
        _recovering = true;
        try
        {
            RefreshDevices();

            var source = ResolveSource();
            if (source is null)
            {
                SetStatus("No audio output device available. Connect a device.", isError: true);
                return;
            }

            bool switched = source.ID != _selectedSource?.ID;
            _selectedSource = source;
            RestartEngine(source);

            if (_engine.IsCapturing)
            {
                if (switched)
                    SetStatus($"Source switched to '{source.FriendlyName}'.", isError: false);
                else
                    ClearStatus();
                NotifySourceChanged();
                Save();
            }
            // On a failed restart RestartEngine has already set an explanatory EngineStatus;
            // leave it in place and let the next watchdog tick retry.
        }
        finally
        {
            _recovering = false;
        }
    }

    /// <summary>
    /// Capture is healthy but running on a fallback device. If the user's chosen source has come
    /// back (e.g. Bluetooth earbuds finished connecting after boot), switch onto it. If it will
    /// not start, restore the working fallback and remember not to keep retrying it.
    /// </summary>
    private void TrySwitchToPreferred()
    {
        if (_preferredSourceId is null || _preferredSourceId == _engine.Source?.ID) return;
        if (_unstartableSources.Contains(_preferredSourceId)) return;

        var preferred = Sources.FirstOrDefault(d => d.ID == _preferredSourceId);
        if (preferred is null) return;

        var fallback = _selectedSource;
        Log.Write($"HealthCheck: preferred source '{preferred.FriendlyName}' is back, switching from fallback '{fallback?.FriendlyName ?? "(none)"}'");
        _selectedSource = preferred;
        RestartEngine(preferred);

        if (_engine.IsCapturing)
        {
            SetStatus($"Source restored to '{preferred.FriendlyName}'.", isError: false);
            NotifySourceChanged();
            return;
        }

        // Preferred device refused to start - keep audio alive on the fallback and stop retrying
        // it until it disconnects and reconnects.
        _unstartableSources.Add(preferred.ID);
        Log.Write($"Preferred source '{preferred.FriendlyName}' would not start; staying on fallback");
        if (fallback is not null)
        {
            _selectedSource = fallback;
            RestartEngine(fallback);
        }
        NotifySourceChanged();
    }

    private void NotifySourceChanged()
    {
        OnPropertyChanged(nameof(SelectedSource));
        OnPropertyChanged(nameof(SourceName));
        OnPropertyChanged(nameof(MasterName));
        OnPropertyChanged(nameof(MasterVolume));
        OnPropertyChanged(nameof(MasterMuted));
        OnPropertyChanged(nameof(MasterPercent));
    }

    /// <summary>
    /// Sync the live device list into <see cref="Sources"/> (add/remove by id, keeping existing
    /// instances) and re-point each channel onto its device as it appears or disappears.
    /// </summary>
    private void RefreshDevices()
    {
        List<MMDevice> current;
        try { current = AudioDevices.GetActiveRenderDevices(); }
        catch (Exception ex) { Log.Write($"RefreshDevices failed: {ex.Message}"); return; }

        var currentIds = current.Select(d => d.ID).ToHashSet();

        for (int i = Sources.Count - 1; i >= 0; i--)
            if (!currentIds.Contains(Sources[i].ID))
            {
                Log.Write($"Device removed: '{Sources[i].FriendlyName}'");
                _unstartableSources.Remove(Sources[i].ID);
                Sources.RemoveAt(i);
            }

        var knownIds = Sources.Select(d => d.ID).ToHashSet();
        foreach (var device in current)
            if (knownIds.Add(device.ID))
            {
                Log.Write($"Device appeared: '{device.FriendlyName}'");
                Sources.Add(device);
            }

        // Re-point channels only across a presence transition, to avoid needless churn.
        foreach (var channel in Channels)
        {
            var device = current.FirstOrDefault(d => d.ID == channel.DeviceId);
            if ((channel.Device is not null) != (device is not null))
                channel.SetDevice(device);
        }
    }

    private MMDevice? ResolveSource()
    {
        if (_preferredSourceId is not null)
        {
            var saved = Sources.FirstOrDefault(d => d.ID == _preferredSourceId);
            if (saved is not null) return saved;
        }

        var defaultDevice = AudioDevices.GetDefaultRender();
        return Sources.FirstOrDefault(d => d.ID == defaultDevice.ID) ?? Sources.FirstOrDefault();
    }

    private void BuildChannels(string? sourceId)
    {
        Channels.Clear();

        var defs = _settings.Channels;
        if (defs.Count == 0)
        {
            // First run: seed from every device that is not the source, so nothing is lost.
            defs = Sources.Where(d => d.ID != sourceId)
                          .Select(d => new ChannelDefinition { DeviceId = d.ID, Name = d.FriendlyName, Gain = 1.0 })
                          .ToList();
        }

        foreach (var def in defs)
        {
            var device = Sources.FirstOrDefault(d => d.ID == def.DeviceId);
            var vm = new ChannelViewModel(_engine, def.DeviceId, device, def.Name, def.Gain,
                () => _selectedLatency.Ms, MarkDirty, _eqPresets, def.Eq)
            {
                IsSource = def.DeviceId == sourceId,
                PendingActive = def.Active,
            };
            Channels.Add(vm);
        }
    }

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
        get => _selectedSource;
        set
        {
            if (value is null || value == _selectedSource) return;
            _selectedSource = value;
            // An explicit pick becomes the remembered preference.
            _preferredSourceId = value.ID;
            _unstartableSources.Remove(value.ID);
            RestartEngine(value);
            Save();
            NotifySourceChanged();
        }
    }

    public string SourceName => _selectedSource?.FriendlyName ?? "(no source)";

    /// <summary>Editable master label; empty override falls back to the source device name.</summary>
    public string MasterName
    {
        get => string.IsNullOrWhiteSpace(_settings.MasterName) ? SourceName : _settings.MasterName!;
        set
        {
            var trimmed = (value ?? "").Trim();
            _settings.MasterName = trimmed.Length == 0 ? null : trimmed;
            OnPropertyChanged();
            Save();
        }
    }

    /// <summary>Inline rename mode for the master strip.</summary>
    public bool IsEditingMaster
    {
        get => _isEditingMaster;
        set { _isEditingMaster = value; OnPropertyChanged(); }
    }

    private void RestartEngine(MMDevice source)
    {
        var wasActive = Channels.Where(c => c.IsActive).ToList();
        foreach (var channel in Channels) channel.IsActive = false;

        bool started = false;
        try
        {
            _engine.Start(source);
            ClearStatus();
            started = true;
        }
        catch (COMException ex)
        {
            Log.Write($"Engine.Start FAILED for '{source.FriendlyName}': {ex}");
            _engine.Stop();
            SetStatus((uint)ex.HResult == 0x8889000A
                ? $"Cannot capture '{source.FriendlyName}': locked in exclusive mode by another app. Pick a different source."
                : $"Cannot capture '{source.FriendlyName}': error 0x{ex.HResult:X8}. Pick a different source.",
                isError: true);
        }

        foreach (var channel in Channels)
            channel.IsSource = channel.DeviceId == source.ID;

        if (started)
            foreach (var channel in wasActive.Where(c => c.IsAvailable))
                channel.IsActive = true;
    }

    /// <summary>Devices not yet used as a channel and not the current source - candidates to add.</summary>
    public IReadOnlyList<MMDevice> GetAvailableDevices()
    {
        var used = Channels.Select(c => c.DeviceId).ToHashSet();
        return Sources.Where(d => d.ID != _selectedSource?.ID && !used.Contains(d.ID)).ToList();
    }

    public void AddChannel(MMDevice device)
    {
        if (device is null || Channels.Any(c => c.DeviceId == device.ID)) return;
        var vm = new ChannelViewModel(_engine, device.ID, device, device.FriendlyName, 1.0,
            () => _selectedLatency.Ms, MarkDirty, _eqPresets)
        {
            IsSource = device.ID == _selectedSource?.ID,
        };
        Channels.Add(vm);
        Log.Write($"Channel added: '{device.FriendlyName}'");
        Save();
    }

    private void RemoveChannel(ChannelViewModel? channel)
    {
        if (channel is null) return;
        channel.IsActive = false;
        Channels.Remove(channel);
        Log.Write($"Channel removed: '{channel.Name}'");
        Save();
    }

    public void MoveChannel(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || newIndex < 0 || oldIndex >= Channels.Count || newIndex >= Channels.Count
            || oldIndex == newIndex) return;
        Channels.Move(oldIndex, newIndex);
        Log.Write($"Channel reordered: {oldIndex} -> {newIndex}");
        Save();
    }

    /// <summary>
    /// Text shown in the notification bubble (empty = hidden). Set it through <see cref="SetStatus"/>
    /// so the error/info severity travels with the message and the bubble colours itself correctly.
    /// </summary>
    public string EngineStatus
    {
        get => _engineStatus;
        private set { _engineStatus = value; OnPropertyChanged(); }
    }

    /// <summary>True = the bubble is a failure (red); false = an informational notice (blue).</summary>
    public bool EngineStatusIsError
    {
        get => _engineStatusIsError;
        private set { _engineStatusIsError = value; OnPropertyChanged(); }
    }

    /// <summary>Show a notification. <paramref name="isError"/> picks the red vs. blue styling.</summary>
    private void SetStatus(string message, bool isError)
    {
        EngineStatusIsError = isError;
        EngineStatus = message;
    }

    /// <summary>Hide the notification bubble.</summary>
    private void ClearStatus()
    {
        EngineStatusIsError = false;
        EngineStatus = "";
    }

    public double MasterVolume
    {
        get => _selectedSource?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0;
        set
        {
            if (_selectedSource is null) return;
            _selectedSource.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(value, 0, 1);
            OnPropertyChanged();
            OnPropertyChanged(nameof(MasterPercent));
        }
    }

    public bool MasterMuted
    {
        get => _selectedSource?.AudioEndpointVolume.Mute ?? false;
        set
        {
            if (_selectedSource is null) return;
            _selectedSource.AudioEndpointVolume.Mute = value;
            OnPropertyChanged();
        }
    }

    public string MasterPercent => $"{Math.Round(MasterVolume * 100)}%";

    // --- Tray / startup options (persisted to settings.json) -------------------

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set { if (_settings.CloseToTray == value) return; _settings.CloseToTray = value; OnPropertyChanged(); Save(); }
    }

    public bool MinimizeToTray
    {
        get => _settings.MinimizeToTray;
        set { if (_settings.MinimizeToTray == value) return; _settings.MinimizeToTray = value; OnPropertyChanged(); Save(); }
    }

    public bool RunWithWindows
    {
        get => _settings.RunWithWindows;
        set
        {
            if (_settings.RunWithWindows == value) return;
            _settings.RunWithWindows = value;
            StartupRegistration.Set(value);
            OnPropertyChanged();
            Save();
        }
    }

    private void MarkDirty() => _dirty = true;

    private void Save()
    {
        if (!_loaded) return;
        // Persist the user's chosen source, never a temporary fallback.
        _settings.SourceDeviceId = _preferredSourceId;
        _settings.LatencyMs = _selectedLatency.Ms;
        _settings.Channels = Channels.Select(c => c.ToDefinition()).ToList();
        _settings.EqPresets = _eqPresets.Persistable.ToList();
        _settings.Save();
        _dirty = false;
    }

    public void Dispose()
    {
        _healthTimer?.Stop();
        _engine.SourceLost -= OnEngineSourceLost;
        if (_dirty) Save();
        _engine.Dispose();
    }
}
