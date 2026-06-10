using System;
using System.Runtime.InteropServices;
using AudioHQ.Core;
using NAudio.CoreAudioApi;

namespace AudioHQ.App.ViewModels;

/// <summary>One output strip: toggles mirroring to a physical device on/off.</summary>
public sealed class ChannelViewModel : ViewModelBase
{
    private readonly MirrorEngine _engine;
    private readonly Func<int> _latencyMs;
    private OutputChannel? _channel;

    private bool _isActive;
    private bool _isMuted;
    private double _gain = 1.0;
    private string _status = "";

    public MMDevice Device { get; }
    public string Name => Device.FriendlyName;

    public ChannelViewModel(MirrorEngine engine, MMDevice device, Func<int> latencyMs)
    {
        _engine = engine;
        Device = device;
        _latencyMs = latencyMs;
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;

            if (value)
            {
                try
                {
                    _channel = _engine.AddOutput(Device, _latencyMs());
                    _channel.Gain = (float)_gain;
                    _channel.Muted = _isMuted;
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
        }
    }

    public string GainPercent => $"{Math.Round(_gain * 100)}%";

    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }
}
