using System;
using System.Runtime.InteropServices;
using System.Windows;
using AudioHQ.Core;
using NAudio.CoreAudioApi;

namespace AudioHQ.App.ViewModels;

/// <summary>
/// One curated output strip: maps a physical device (by id) to a user-named,
/// reorderable channel with its own gain/mute. The device may be offline
/// (saved but currently unplugged) or equal to the source (cannot mirror to itself).
/// The channel never caches an MMDevice: it resolves a FRESH instance by id at every
/// activation, because cached instances go stale across sleep/resume and unplug/replug.
/// </summary>
public sealed class ChannelViewModel : ViewModelBase
{
    /// <summary>Consecutive failed auto-reactivations before the watchdog gives up on the
    /// device (until it disappears and reappears, or a resume resets the budget).</summary>
    private const int MaxAutoRetries = 3;

    private readonly MirrorEngine _engine;
    private readonly Func<int> _latencyMs;
    private readonly Action _onChanged;
    private readonly EqViewModel _eq;
    private readonly EqPresetStore _presets;
    private OutputChannel? _channel;

    private bool _isActive;
    private bool _wantsActive;
    private int _autoRetriesLeft = MaxAutoRetries;
    private bool _isPresent;
    private bool _isMuted;
    private double _gain;
    private string _name;
    private bool _isSource;
    private bool _isEditing;
    private bool _isFocused;
    private string _status = "";

    /// <summary>Stable persisted identity of the target device.</summary>
    public string DeviceId { get; }

    public ChannelViewModel(MirrorEngine engine, string deviceId, bool present,
        string name, double gain, Func<int> latencyMs, Action onChanged,
        EqPresetStore presets, EqSettings? eq = null)
    {
        _engine = engine;
        DeviceId = deviceId;
        _isPresent = present;
        _latencyMs = latencyMs;
        _onChanged = onChanged;
        _presets = presets;
        _gain = gain;
        _name = string.IsNullOrWhiteSpace(name) ? "Channel" : name;
        _eq = new EqViewModel(eq, ApplyEq);
    }

    /// <summary>The editable graphic EQ for this channel (bound by the EQ editor window).</summary>
    public EqViewModel Eq => _eq;

    /// <summary>App-wide saved EQ presets (bound by the EQ editor's preset picker).</summary>
    public EqPresetStore EqPresets => _presets;

    /// <summary>Push the current EQ curve onto the live output (if active) and persist it.</summary>
    private void ApplyEq()
    {
        _channel?.Equalizer.Configure(_eq.ToSettings());
        OnPropertyChanged(nameof(EqEnabled));
        _onChanged();
    }

    /// <summary>
    /// EQ on/off. Drives the channel's EQ pill (click toggles it); setting it routes through
    /// the EQ model, which applies the curve live and raises this back via <see cref="ApplyEq"/>.
    /// </summary>
    public bool EqEnabled
    {
        get => _eq.Enabled;
        set => _eq.Enabled = value;
    }

    /// <summary>True while the saved device is currently enumerated as active.</summary>
    public bool IsPresent => _isPresent;

    /// <summary>True only when the channel can actually mirror (device present and not the source).</summary>
    public bool IsAvailable => _isPresent && !_isSource;

    /// <summary>
    /// Persisted "should be ON" intent. Survives device loss, sleep/resume and engine
    /// restarts: whenever the channel is off but wanted and available, the mixer watchdog
    /// reactivates it. Only an explicit user toggle changes it.
    /// </summary>
    public bool WantsActive
    {
        get => _wantsActive;
        set => _wantsActive = value;
    }

    public string Name
    {
        get => _name;
        set
        {
            var trimmed = (value ?? "").Trim();
            if (trimmed.Length == 0 || trimmed == _name) return;
            _name = trimmed;
            OnPropertyChanged();
            _onChanged();
        }
    }

    /// <summary>Inline rename mode (toggled from the UI).</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set { _isEditing = value; OnPropertyChanged(); }
    }

    /// <summary>True when this channel is selected to drive the tray icon and middle-click toggle.</summary>
    public bool IsFocused
    {
        get => _isFocused;
        set { _isFocused = value; OnPropertyChanged(); }
    }

    /// <summary>Set by the mixer when this channel targets the current source device.</summary>
    public bool IsSource
    {
        get => _isSource;
        set
        {
            if (_isSource == value) return;
            _isSource = value;
            // Becoming the source suspends mirroring but keeps the ON intent: when the
            // source moves elsewhere again, the watchdog brings this channel back.
            if (value && _isActive) Suspend();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAvailable));
            RefreshUnavailableStatus();
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;

            // This setter is the USER path (toggle in the UI, or the mixer acting for the
            // user): it updates the intent. Mechanical stops go through Suspend().
            _wantsActive = value;
            _autoRetriesLeft = MaxAutoRetries;

            if (value)
            {
                if (!IsAvailable)
                {
                    RefreshUnavailableStatus();
                    OnPropertyChanged();
                    return;
                }
                Activate();
            }
            else
            {
                DetachChannel();
                _isActive = false;
                RefreshUnavailableStatus();
            }

            OnPropertyChanged();
            _onChanged();
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            if (_channel is not null) _channel.Muted = value;
            OnPropertyChanged();
        }
    }

    public double Gain
    {
        get => _gain;
        set
        {
            _gain = value;
            if (_channel is not null) _channel.Gain = (float)value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GainPercent));
            _onChanged();
        }
    }

    public string GainPercent => $"{Math.Round(_gain * 100)}%";

    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    /// <summary>Open a live output on a FRESH device instance. Sets _isActive on success.</summary>
    private void Activate()
    {
        OutputChannel? channel = null;
        MMDevice? device = null;
        try
        {
            device = AudioDevices.FindRenderById(DeviceId);
            if (device is null)
            {
                SetPresent(false);
                return;
            }

            channel = _engine.AddOutput(device, _latencyMs());
            channel.Gain = (float)_gain;
            channel.Muted = _isMuted;
            channel.Equalizer.Configure(_eq.ToSettings());
            channel.PlaybackStopped += OnPlaybackStopped;
            _channel = channel;
            _isActive = true;
            _autoRetriesLeft = MaxAutoRetries;
            Status = "";
        }
        catch (COMException ex)
        {
            Log.Write($"Activate '{Name}' FAILED: {ex}");
            Status = (uint)ex.HResult switch
            {
                0x8889000A => "In use (exclusive)",
                0x88890008 => "Format not supported",
                0x88890004 => "Device unavailable",
                _ => $"Error 0x{ex.HResult:X8}",
            };
            CleanUpFailedActivation(channel, device);
        }
        catch (InvalidOperationException ex)
        {
            Log.Write($"Activate '{Name}' FAILED: {ex}");
            Status = "Source not capturing";
            CleanUpFailedActivation(channel, device);
        }
        catch (Exception ex)
        {
            Log.Write($"Activate '{Name}' FAILED: {ex}");
            Status = ex.Message.Length > 80 ? ex.Message[..80] : ex.Message;
            CleanUpFailedActivation(channel, device);
        }
    }

    private void CleanUpFailedActivation(OutputChannel? channel, MMDevice? device)
    {
        _isActive = false;
        if (channel is not null) _engine.RemoveOutput(channel);   // disposes the device too
        else device?.Dispose();
        _channel = null;
    }

    /// <summary>Close the live output (if any) without touching intent or the toggle state.</summary>
    private void DetachChannel()
    {
        if (_channel is null) return;
        _channel.PlaybackStopped -= OnPlaybackStopped;
        _engine.RemoveOutput(_channel);
        _channel = null;
    }

    /// <summary>
    /// Deactivate WITHOUT clearing the ON intent - used for mechanical stops (engine restart,
    /// device loss, sleep). The watchdog reactivates the channel when it becomes possible.
    /// </summary>
    public void Suspend()
    {
        DetachChannel();
        if (!_isActive) return;
        _isActive = false;
        OnPropertyChanged(nameof(IsActive));
    }

    /// <summary>
    /// Watchdog hook: bring the channel back if the user wants it ON and it can run. Retries
    /// are budgeted so a persistently failing device is not hammered every tick; the budget
    /// resets when the device reappears, on resume, or on an explicit user action.
    /// <paramref name="force"/> (engine restart, resume) bypasses the budget.
    /// </summary>
    public void TryAutoReactivate(bool force = false)
    {
        if (_isActive || !_wantsActive || !IsAvailable) return;
        if (!force)
        {
            if (_autoRetriesLeft <= 0) return;
            _autoRetriesLeft--;
        }

        Activate();
        if (_isActive)
        {
            Log.Write($"Channel '{Name}': auto-reactivated");
            OnPropertyChanged(nameof(IsActive));
        }
    }

    /// <summary>Give a failing device a fresh retry budget (called on resume).</summary>
    public void ResetAutoRetry() => _autoRetriesLeft = MaxAutoRetries;

    /// <summary>Engine callback (render thread) for an unsolicited output stop.</summary>
    private void OnPlaybackStopped(OutputChannel channel, Exception? error)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(new Action(() => HandlePlaybackStopped(channel, error)));
        else
            HandlePlaybackStopped(channel, error);
    }

    private void HandlePlaybackStopped(OutputChannel channel, Exception? error)
    {
        if (!ReferenceEquals(channel, _channel)) return; // already detached or replaced
        Log.Write($"Channel '{Name}': output died ({error?.Message ?? "no error"}), will reconnect");
        DetachChannel();
        _isActive = false;
        OnPropertyChanged(nameof(IsActive));
        Status = "Reconnecting...";
        // Intent is preserved; the mixer watchdog (or resume recovery) reactivates it.
    }

    /// <summary>Mark the saved device as (re)appeared or gone, from the mixer's device sync.</summary>
    public void SetPresent(bool present)
    {
        if (_isPresent == present) return;
        _isPresent = present;
        OnPropertyChanged(nameof(IsAvailable));
        if (!present) Suspend();               // keep the ON intent - it comes back with the device
        else _autoRetriesLeft = MaxAutoRetries; // fresh device, fresh retry budget
        RefreshUnavailableStatus();
    }

    private void RefreshUnavailableStatus()
    {
        if (_isSource) Status = "= source";
        else if (!_isPresent) Status = "Offline";
        else if (!_isActive) Status = "";
    }

    public ChannelDefinition ToDefinition() => new()
    {
        DeviceId = DeviceId,
        Name = _name,
        Gain = _gain,
        Active = _wantsActive,
        Focused = _isFocused,
        Eq = _eq.ToSettings(),
    };
}
