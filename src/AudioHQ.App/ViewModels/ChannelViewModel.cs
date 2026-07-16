using System;
using AudioHQ.Core;

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
    private readonly Action _onChanged;
    private readonly EqViewModel _eq;
    private readonly EqPresetStore _presets;
    private readonly ChannelLifecycleController _lifecycle;

    private bool _wantsActive;
    private bool _isMuted;
    private double _gain;
    private string _name;
    private bool _isSource;
    private bool _isEditing;
    private bool _isFocused;
    private string _status = "";

    /// <summary>Current Windows identity of the target device.</summary>
    public string DeviceId { get; private set; }

    /// <summary>Last known Windows friendly name, separate from the editable channel label.</summary>
    public string DeviceName { get; private set; }

    public ChannelViewModel(MirrorEngine engine, string deviceId, bool present,
        string name, double gain, Func<int> latencyMs, Action onChanged,
        EqPresetStore presets, EqSettings? eq = null, string? deviceName = null,
        bool muted = false)
    {
        DeviceId = deviceId;
        DeviceName = string.IsNullOrWhiteSpace(deviceName) ? name : deviceName;
        _onChanged = onChanged;
        _presets = presets;
        _gain = gain;
        // Restored through the field, not the property: the setter would flag settings dirty
        // just for loading them back.
        _isMuted = muted;
        _name = string.IsNullOrWhiteSpace(name) ? "Channel" : name;
        _eq = new EqViewModel(eq, ApplyEq);

        // The device half of the strip. It owns the live output and the retry budget and
        // calls back here when the DEVICE changes something; this class owns what the USER
        // changes.
        _lifecycle = new ChannelLifecycleController(
            engine, deviceId, latencyMs, present,
            request: () => new ChannelActivationRequest(Name, _gain, _isMuted, _eq.ToSettings()),
            channelName: () => Name,
            activeChanged: () => OnPropertyChanged(nameof(IsActive)),
            availabilityChanged: () => OnPropertyChanged(nameof(IsAvailable)),
            refreshStatus: RefreshUnavailableStatus,
            setStatus: status => Status = status);
    }

    /// <summary>The editable graphic EQ for this channel (bound by the EQ editor window).</summary>
    public EqViewModel Eq => _eq;

    /// <summary>App-wide saved EQ presets (bound by the EQ editor's preset picker).</summary>
    public EqPresetStore EqPresets => _presets;

    /// <summary>Push the current EQ curve onto the live output (if active) and persist it.</summary>
    private void ApplyEq()
    {
        _lifecycle.Channel?.Equalizer.Configure(_eq.ToSettings());
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
    public bool IsPresent => _lifecycle.IsPresent;

    /// <summary>True only when the channel can actually mirror (device present and not the source).</summary>
    public bool IsAvailable => _lifecycle.IsPresent && !_isSource;

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
            if (value && _lifecycle.IsActive) Suspend();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAvailable));
            RefreshUnavailableStatus();
        }
    }

    public bool IsActive
    {
        get => _lifecycle.IsActive;
        set
        {
            if (_lifecycle.IsActive == value) return;

            // This setter is the USER path (toggle in the UI, or the mixer acting for the
            // user): it updates the intent. Mechanical stops go through Suspend().
            _wantsActive = value;
            _lifecycle.ResetAutoRetry();

            if (value)
            {
                if (!IsAvailable)
                {
                    RefreshUnavailableStatus();
                    OnPropertyChanged();
                    return;
                }
                _lifecycle.Activate();
            }
            else
            {
                _lifecycle.Deactivate();
                RefreshUnavailableStatus();
            }

            OnPropertyChanged();
            _onChanged();
        }
    }

    /// <summary>
    /// Mute is persisted, like gain: a channel muted at exit comes back muted. Activation
    /// re-applies it via <see cref="ChannelActivationService.Activate"/>.
    /// </summary>
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            if (_lifecycle.Channel is not null) _lifecycle.Channel.Muted = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public double Gain
    {
        get => _gain;
        set
        {
            _gain = value;
            if (_lifecycle.Channel is not null) _lifecycle.Channel.Gain = (float)value;
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

    /// <summary>
    /// Deactivate WITHOUT clearing the ON intent - used for mechanical stops (engine restart,
    /// device loss, sleep). The watchdog reactivates the channel when it becomes possible.
    /// </summary>
    public void Suspend() => _lifecycle.Suspend();

    /// <summary>
    /// Watchdog hook: bring the channel back if the user wants it ON and it can run.
    /// <paramref name="force"/> (engine restart, resume) bypasses the retry budget.
    /// </summary>
    public void TryAutoReactivate(bool force = false) =>
        _lifecycle.TryAutoReactivate(_wantsActive && IsAvailable, force);

    /// <summary>Give a failing device a fresh retry budget (called on resume).</summary>
    public void ResetAutoRetry() => _lifecycle.ResetAutoRetry();

    /// <summary>Mark the saved device as (re)appeared or gone, from the mixer's device sync.</summary>
    public void SetPresent(bool present) => _lifecycle.SetPresent(present);

    /// <summary>Adopt a replacement endpoint id for the same uniquely named physical output.</summary>
    public void RebindDevice(string deviceId, string deviceName)
    {
        if (deviceId == DeviceId) return;
        Log.Write($"Channel '{Name}': rebound endpoint '{DeviceId}' -> '{deviceId}' ({deviceName})");
        _lifecycle.RebindDevice(deviceId);
        DeviceId = deviceId;
        DeviceName = deviceName;
        OnPropertyChanged(nameof(DeviceId));
        OnPropertyChanged(nameof(DeviceName));
        OnPropertyChanged(nameof(IsAvailable));
        RefreshUnavailableStatus();
    }

    private void RefreshUnavailableStatus()
    {
        if (_isSource) Status = "= source";
        else if (!_lifecycle.IsPresent) Status = "Offline";
        else if (!_lifecycle.IsActive) Status = "";
    }

    public ChannelDefinition ToDefinition() => new()
    {
        DeviceId = DeviceId,
        DeviceName = DeviceName,
        Name = _name,
        Gain = _gain,
        Muted = _isMuted,
        Active = _wantsActive,
        Focused = _isFocused,
        Eq = _eq.ToSettings(),
    };
}
