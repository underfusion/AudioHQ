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
    private MMDevice? _selectedSource;
    private string _engineStatus = "";
    private bool _engineStatusIsError;
    private LatencyPreset _selectedLatency;
    private bool _loaded;
    private bool _dirty;
    private bool _isEditingMaster;
    private bool _recovering;
    private ChannelViewModel? _focusedChannel;

    // The source the USER chose (persisted as SourceDeviceId). Distinct from _selectedSource,
    // which is whatever is actually live and may be a fallback when the chosen device is not
    // ready yet (e.g. Bluetooth earbuds still connecting right after a PC restart). Only an
    // explicit pick changes this; a fallback never overwrites it, so the preference survives.
    private string? _preferredSourceId;

    // Device ids that would not start as a source (e.g. locked in exclusive mode). Skipped by the
    // switch-back watchdog until they disappear and reappear, so we never spin retrying a bad one.
    private readonly HashSet<string> _unstartableSources = new();

    // Resume handling: after a wake-up, devices come back staggered over several seconds, so
    // the watchdog keeps retrying aggressively (fresh retry budgets) for this many ticks.
    private int _resumeTicksLeft;
    private DateTime _lastTickUtc = DateTime.UtcNow;

    /// <summary>How often the background watchdog re-checks devices and engine health.</summary>
    private static readonly TimeSpan HealthInterval = TimeSpan.FromSeconds(3);

    /// <summary>Watchdog ticks with forced channel-retry budgets after a resume (~30 s).</summary>
    private const int ResumeGraceTicks = 10;

    /// <summary>A tick gap this large means the machine slept through timer time - treat as resume.
    /// SystemEvents.PowerModeChanged is unreliable on Modern Standby, so this is the fallback.</summary>
    private static readonly TimeSpan ClockJumpThreshold = TimeSpan.FromSeconds(20);

    public ObservableCollection<MMDevice> Sources { get; } = new();
    public ObservableCollection<ChannelViewModel> Channels { get; } = new();

    public MixerSettings Settings => _settings;

    public ICommand RemoveChannelCommand { get; }

    /// <summary>Closes the status notification bubble (the "X" on it).</summary>
    public ICommand DismissStatusCommand { get; }

    /// <summary>Selects (or deselects) a channel as the tray-focus target.</summary>
    public ICommand FocusChannelCommand { get; }

    /// <summary>The channel currently selected to drive the tray icon and middle-click toggle, or null.</summary>
    public ChannelViewModel? FocusedChannel => _focusedChannel;

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
        FocusChannelCommand = new RelayCommand(p => ToggleFocusChannel(p as ChannelViewModel));
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
            RestartEngine(source); // also restores the channels saved as ON

        _loaded = true;

        // Keep the HKCU Run entry in step with the saved preference (refreshes the
        // exe path if the app was moved since it was first enabled).
        StartupRegistration.Set(_settings.RunWithWindows);

        // Resume hint. Known to be missed on some Modern Standby machines, hence the
        // clock-jump fallback in HealthCheck.
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // Background watchdog: keeps the device list current, recovers the engine if the
        // source drops out and reactivates wanted channels (see HealthCheck).
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
    /// has vanished without a clean stop, try to recover. A healthy engine is left untouched,
    /// but wanted-yet-inactive channels are still given a reactivation chance every tick.
    /// </summary>
    private void HealthCheck()
    {
        try
        {
            var now = DateTime.UtcNow;
            bool sleptThrough = now - _lastTickUtc > HealthInterval + ClockJumpThreshold;
            _lastTickUtc = now;
            if (sleptThrough)
            {
                Log.Write("HealthCheck: timer gap detected (sleep/resume), forcing full recovery");
                BeginResumeRecovery();
                return;
            }

            RefreshDevices();

            bool sourceGone = _engine.SourceId is not null
                              && Sources.All(d => d.ID != _engine.SourceId);

            if (_engine.IsCapturing && !sourceGone)
            {
                // Healthy. If we are only on a fallback because the chosen source was not ready
                // earlier, switch back to it now that it has reappeared.
                TrySwitchToPreferred();
                ReactivateWantedChannels();
                return;
            }

            if (Sources.Count == 0)
            {
                SetStatus("No audio output device available. Connect a device.", isError: true);
                return;
            }

            Log.Write($"HealthCheck: recovery needed (capturing={_engine.IsCapturing}, sourceGone={sourceGone})");
            TryRecover();
            ReactivateWantedChannels();
        }
        catch (Exception ex)
        {
            // The watchdog must never take the app down; log and let the next tick retry.
            Log.Write($"HealthCheck failed: {ex}");
        }
    }

    /// <summary>Bring back channels that should be ON but are not (device returned, output died).</summary>
    private void ReactivateWantedChannels()
    {
        bool resumeGrace = _resumeTicksLeft > 0;
        if (resumeGrace) _resumeTicksLeft--;

        foreach (var channel in Channels)
        {
            if (resumeGrace) channel.ResetAutoRetry();
            channel.TryAutoReactivate();
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(new Action(BeginResumeRecovery));
        else
            BeginResumeRecovery();
    }

    /// <summary>
    /// Wake-up recovery: every cached MMDevice may have been invalidated while the machine
    /// slept even though the endpoints still enumerate fine, so replace them all with fresh
    /// instances, restart capture and re-open the channels. Devices that are still powering
    /// up (TV over HDMI, Bluetooth) are picked up by the grace-period watchdog ticks.
    /// </summary>
    private void BeginResumeRecovery()
    {
        try
        {
            Log.Write("Resume detected: refreshing devices and restarting the engine");
            _resumeTicksLeft = ResumeGraceTicks;
            _unstartableSources.Clear();
            foreach (var channel in Channels) channel.ResetAutoRetry();
            HardRefreshSources();
            TryRecover();
        }
        catch (Exception ex)
        {
            Log.Write($"Resume recovery failed: {ex}");
        }
    }

    /// <summary>
    /// Replace every cached device instance in <see cref="Sources"/> with a freshly enumerated
    /// one (same ids, new COM objects), fixing up <see cref="_selectedSource"/> so the master
    /// strip talks to a live AudioEndpointVolume again.
    /// </summary>
    private void HardRefreshSources()
    {
        List<MMDevice> current;
        try { current = AudioDevices.GetActiveRenderDevices(); }
        catch (Exception ex) { Log.Write($"HardRefreshSources failed: {ex.Message}"); return; }

        var fresh = current.ToDictionary(d => d.ID);
        var adopted = new HashSet<string>();

        for (int i = Sources.Count - 1; i >= 0; i--)
        {
            var old = Sources[i];
            if (fresh.TryGetValue(old.ID, out var replacement))
            {
                adopted.Add(old.ID);
                Sources[i] = replacement;
                if (ReferenceEquals(_selectedSource, old)) _selectedSource = replacement;
                old.Dispose();
            }
            else
            {
                Log.Write($"Device gone after resume: '{old.FriendlyName}'");
                _unstartableSources.Remove(old.ID);
                Sources.RemoveAt(i);
                // Not disposed: it may still be referenced as _selectedSource until recovery
                // picks a live device.
            }
        }

        foreach (var device in current)
            if (!adopted.Contains(device.ID))
            {
                Log.Write($"Device appeared after resume: '{device.FriendlyName}'");
                Sources.Add(device);
            }

        foreach (var channel in Channels)
            channel.SetPresent(fresh.ContainsKey(channel.DeviceId));

        NotifySourceChanged();
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
        if (_preferredSourceId is null || _preferredSourceId == _engine.SourceId) return;
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
    /// instances) and update each channel's presence flag. Runs every watchdog tick, so
    /// duplicate enumerations are disposed instead of leaking a COM wrapper per device per tick.
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
        {
            if (knownIds.Add(device.ID))
            {
                Log.Write($"Device appeared: '{device.FriendlyName}'");
                Sources.Add(device);
            }
            else
            {
                device.Dispose(); // duplicate of an instance we already hold
            }
        }

        foreach (var channel in Channels)
            channel.SetPresent(currentIds.Contains(channel.DeviceId));
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
            bool present = Sources.Any(d => d.ID == def.DeviceId);
            var vm = new ChannelViewModel(_engine, def.DeviceId, present, def.Name, def.Gain,
                () => _selectedLatency.Ms, MarkDirty, _eqPresets, def.Eq)
            {
                IsSource = def.DeviceId == sourceId,
                WantsActive = def.Active,
            };
            Channels.Add(vm);
        }

        var focusedDef = defs.FirstOrDefault(d => d.Focused);
        if (focusedDef is not null)
        {
            var ch = Channels.FirstOrDefault(c => c.DeviceId == focusedDef.DeviceId);
            if (ch is not null)
            {
                ch.IsFocused = true;
                _focusedChannel = ch;
            }
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
        // Suspend (not deactivate): the channels keep their ON intent and are restored below.
        foreach (var channel in Channels) channel.Suspend();

        string sourceId = source.ID;
        bool started = false;
        try
        {
            // Never hand a cached MMDevice to the engine: after sleep/resume the old COM
            // object can be invalid even though the endpoint still enumerates. Resolve a
            // fresh instance by id; the engine owns and disposes it.
            var live = AudioDevices.FindRenderById(sourceId)
                ?? throw new InvalidOperationException($"Device '{source.FriendlyName}' is not active.");
            _engine.Start(live);
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
        catch (Exception ex)
        {
            // A non-COM failure must not escape into the watchdog timer tick (it would pop
            // the crash dialog every 3 seconds while the device misbehaves).
            Log.Write($"Engine.Start FAILED for '{source.FriendlyName}': {ex}");
            _engine.Stop();
            SetStatus($"Cannot capture '{source.FriendlyName}': {ex.Message}", isError: true);
        }

        foreach (var channel in Channels)
            channel.IsSource = channel.DeviceId == sourceId;

        if (started)
            foreach (var channel in Channels)
                channel.TryAutoReactivate(force: true);
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
        var vm = new ChannelViewModel(_engine, device.ID, present: true, device.FriendlyName, 1.0,
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
        if (_focusedChannel == channel) ToggleFocusChannel(channel);
        channel.IsActive = false;
        Channels.Remove(channel);
        Log.Write($"Channel removed: '{channel.Name}'");
        Save();
    }

    private void ToggleFocusChannel(ChannelViewModel? channel)
    {
        if (channel is null) return;
        var prev = _focusedChannel;
        if (prev == channel)
        {
            prev.IsFocused = false;
            _focusedChannel = null;
        }
        else
        {
            if (prev is not null) prev.IsFocused = false;
            channel.IsFocused = true;
            _focusedChannel = channel;
        }
        OnPropertyChanged(nameof(FocusedChannel));
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

    // Master volume/mute talk straight to the source device's AudioEndpointVolume, which can
    // die between watchdog ticks (unplug, sleep); a fader drag must never crash a binding.

    public double MasterVolume
    {
        get
        {
            try { return _selectedSource?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0; }
            catch (Exception ex) { Log.Write($"MasterVolume read failed: {ex.Message}"); return 0; }
        }
        set
        {
            if (_selectedSource is null) return;
            try { _selectedSource.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(value, 0, 1); }
            catch (Exception ex) { Log.Write($"MasterVolume set failed: {ex.Message}"); }
            OnPropertyChanged();
            OnPropertyChanged(nameof(MasterPercent));
        }
    }

    public bool MasterMuted
    {
        get
        {
            try { return _selectedSource?.AudioEndpointVolume.Mute ?? false; }
            catch (Exception ex) { Log.Write($"MasterMuted read failed: {ex.Message}"); return false; }
        }
        set
        {
            if (_selectedSource is null) return;
            try { _selectedSource.AudioEndpointVolume.Mute = value; }
            catch (Exception ex) { Log.Write($"MasterMuted set failed: {ex.Message}"); }
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

    public void SaveSettings() => Save();

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _healthTimer?.Stop();
        _engine.SourceLost -= OnEngineSourceLost;
        if (_dirty) Save();
        _engine.Dispose();
    }
}
