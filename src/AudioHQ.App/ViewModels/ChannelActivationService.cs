using System;
using System.Runtime.InteropServices;
using AudioHQ.Core;

namespace AudioHQ.App.ViewModels;

public sealed record ChannelActivationResult(OutputChannel? Channel, bool DeviceMissing, string Status)
{
    public bool IsActive => Channel is not null;
}

/// <summary>
/// Creates live output channels and maps activation failures to UI status text.
/// The real, WASAPI-backed implementation of <see cref="IChannelActivationService"/>.
/// </summary>
public sealed class ChannelActivationService : IChannelActivationService
{
    private readonly MirrorEngine _engine;
    private string _deviceId;
    private readonly Func<int> _latencyMs;

    public ChannelActivationService(MirrorEngine engine, string deviceId, Func<int> latencyMs)
    {
        _engine = engine;
        _deviceId = deviceId;
        _latencyMs = latencyMs;
    }

    public void RebindDevice(string deviceId) => _deviceId = deviceId;

    public ChannelActivationResult Activate(
        string channelName,
        double gain,
        bool muted,
        EqSettings eq,
        Action<OutputChannel, Exception?> playbackStopped)
    {
        OutputChannel? channel = null;
        NAudio.CoreAudioApi.MMDevice? device = null;
        try
        {
            device = AudioDevices.FindRenderById(_deviceId);
            if (device is null)
                return new ChannelActivationResult(null, DeviceMissing: true, "");

            channel = _engine.AddOutput(device, _latencyMs());
            channel.Gain = (float)gain;
            channel.Muted = muted;
            channel.Equalizer.Configure(eq);
            channel.PlaybackStopped += playbackStopped;
            return new ChannelActivationResult(channel, DeviceMissing: false, "");
        }
        catch (Exception ex)
        {
            Log.Write($"Activate '{channelName}' FAILED: {ex}");
            CleanUpFailedActivation(channel, device);
            return new ChannelActivationResult(null, DeviceMissing: false, StatusFor(ex));
        }
    }

    public static string StatusFor(Exception ex) => ex switch
    {
        COMException com => (uint)com.HResult switch
        {
            0x8889000A => "In use (exclusive)",
            0x88890008 => "Format not supported",
            0x88890004 => "Device unavailable",
            _ => $"Error 0x{com.HResult:X8}",
        },
        InvalidOperationException => "Source not capturing",
        _ => ex.Message.Length > 80 ? ex.Message[..80] : ex.Message,
    };

    private void CleanUpFailedActivation(OutputChannel? channel, NAudio.CoreAudioApi.MMDevice? device)
    {
        if (channel is not null) _engine.RemoveOutput(channel);
        else device?.Dispose();
    }
}
