using System;
using System.Runtime.InteropServices;
using AudioHQ.Core;
using NAudio.CoreAudioApi;

namespace AudioHQ.App.ViewModels;

/// <summary>
/// One curated output strip: maps a physical device (by id) to a user-named,
/// reorderable channel with its own gain/mute. The device may be offline
/// (saved but currently unplugged) or equal to the source (cannot mirror to itself).
/// </summary>
public sealed class ChannelViewModel : ViewModelBase
{
    private readonly MirrorEngine _engine;
    private readonly Func<int> _latencyMs;
    private readonly Action _onChanged;
    private readonly EqViewModel _eq;
    private readonly EqPresetStore _presets;
    private OutputChannel? _channel;

    private bool _isActive;
    private bool _isMuted;
    private double _gain;
    private string _name;
    private bool _isSource;
    private bool _isEditing;
    private string _status = "";

    /// <summary>Stable persisted identity of the target device.</summary>
    public string DeviceId { get; }

    /// <summary>Resolved device, or null when the saved device is not currently present.</summary>
    public MMDevice? Device { get; private set; }

    public ChannelViewModel(MirrorEngine engine, string deviceId, MMDevice? device,
        string name, double gain, Func<int> latencyMs, Action onChanged,
        EqPresetStore presets, EqSettings? eq = null)
    {
        _engine = engine;
        DeviceId = deviceId;
        Device = device;
        _latencyMs = latencyMs;
        _onChanged = onChanged;
        _presets = presets;
        _gain = gain;
        _name = string.IsNullOrWhiteSpace(name) ? (device?.FriendlyName ?? "Channel") : name;
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

    /// <summary>True only when the channel can actually mirror (device present and not the source).</summary>
    public bool IsAvailable => Device is not null && !_isSource;

    /// <summary>Persisted "was ON" intent; the mixer activates these once the engine is up.</summary>
    public bool PendingActive { get; set; }

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

    /// <summary>Set by the mixer when this channel targets the current source device.</summary>
    public bool IsSource
    {
        get => _isSource;
        set
        {
            if (_isSource == value) return;
            _isSource = value;
            if (value && _isActive) IsActive = false;
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

            if (value)
            {
                if (!IsAvailable)
                {
                    RefreshUnavailableStatus();
                    OnPropertyChanged();
                    return;
                }

                try
                {
                    _channel = _engine.AddOutput(Device!, _latencyMs());
                    _channel.Gain = (float)_gain;
                    _channel.Muted = _isMuted;
                    _channel.Equalizer.Configure(_eq.ToSettings());
                    Status = "";
                    _isActive = true;
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
                    _isActive = false;
                }
                catch (InvalidOperationException ex)
                {
                    Log.Write($"Activate '{Name}' FAILED: {ex}");
                    Status = "Source not capturing";
                    _isActive = false;
                }
                catch (Exception ex)
                {
                    Log.Write($"Activate '{Name}' FAILED: {ex}");
                    Status = ex.Message.Length > 80 ? ex.Message[..80] : ex.Message;
                    _isActive = false;
                }
            }
            else
            {
                if (_channel is not null)
                {
                    _engine.RemoveOutput(_channel);
                    _channel = null;
                }
                _isActive = false;
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

    /// <summary>Re-point this channel at a (re)discovered device, or null when it goes offline.</summary>
    public void SetDevice(MMDevice? device)
    {
        Device = device;
        OnPropertyChanged(nameof(IsAvailable));
        if (device is null && _isActive) IsActive = false;
        RefreshUnavailableStatus();
    }

    private void RefreshUnavailableStatus()
    {
        if (_isSource) Status = "= source";
        else if (Device is null) Status = "Offline";
        else if (!_isActive) Status = "";
    }

    public ChannelDefinition ToDefinition() => new()
    {
        DeviceId = DeviceId,
        Name = _name,
        Gain = _gain,
        Active = _isActive,
        Eq = _eq.ToSettings(),
    };
}
