using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioHQ.Core;

/// <summary>One mirrored output: buffer -> resample -> gain/mute -> WASAPI render.</summary>
public sealed class OutputChannel : IDisposable
{
    private readonly BufferedWaveProvider _buffer;
    private readonly EqualizerProvider _equalizer;
    private readonly VolumeSampleProvider _volume;
    private readonly WasapiOut _out;
    private readonly TimeSpan _maxBacklog;
    private readonly string _deviceName;

    private float _gain = 1f;
    private bool _muted;
    private volatile bool _disposed;
    private volatile bool _writeFailureLogged;

    /// <summary>
    /// The channel OWNS this instance (disposed with the channel) - but only once the
    /// constructor has SUCCEEDED. If construction throws, the device is still the caller's
    /// to dispose.
    /// </summary>
    public MMDevice Device { get; }

    /// <summary>
    /// Raised when playback stops on its own - the output device was removed, disabled or
    /// invalidated (sleep/resume, unplug) - as opposed to an intentional Dispose. Fires on
    /// the render thread; marshal before touching UI.
    /// </summary>
    public event Action<OutputChannel, Exception?>? PlaybackStopped;

    internal OutputChannel(MMDevice device, WaveFormat captureFormat, int latencyMs)
    {
        Device = device;
        // Cache the name: reading it later off a dead device would hit COM again.
        _deviceName = device.FriendlyName;

        var mixFormat = device.AudioClient.MixFormat;
        _maxBacklog = TimeSpan.FromMilliseconds(latencyMs + EngineTunables.ResyncMarginMs);
        Log.Write($"OutputChannel: device='{device.FriendlyName}', capture={captureFormat}, deviceMix={mixFormat}, latency={latencyMs}ms, maxBacklog={_maxBacklog.TotalMilliseconds}ms");

        _buffer = new BufferedWaveProvider(captureFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(EngineTunables.BufferSeconds),
        };

        int targetRate = mixFormat.SampleRate;
        double targetBacklogMs = latencyMs + EngineTunables.TargetBacklogMarginMs;
        ISampleProvider pipeline = new AdaptiveResampler(
            _buffer.ToSampleProvider(),
            targetRate,
            () => _buffer.BufferedDuration.TotalSeconds,
            targetBacklogMs / 1000.0);
        Log.Write($"OutputChannel: adaptive resampling {captureFormat.SampleRate} -> {targetRate}, target backlog={targetBacklogMs:0}ms");

        // Graphic EQ sits between resampling and gain: it shapes the signal, then the
        // channel gain rides on top. Starts as pure pass-through until the UI configures it.
        _equalizer = new EqualizerProvider(pipeline);
        _volume = new VolumeSampleProvider(_equalizer) { Volume = _gain };

        // Some drivers (notably NVIDIA HDMI) reject event-driven shared mode; fall back to push mode.
        try
        {
            _out = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latencyMs);
            _out.Init(_volume);
            Log.Write("OutputChannel: event-sync init OK");
        }
        catch (Exception ex)
        {
            Log.Write($"OutputChannel: event-sync init FAILED: {ex}");
            _out?.Dispose();
            _out = null!;
            try
            {
                _out = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: false, latencyMs);
                _out.Init(_volume);
                Log.Write("OutputChannel: push-mode init OK");
            }
            catch
            {
                // Both modes failed (e.g. a mix format shared mode rejects outright). Nothing owns
                // this half-built channel, so release the client here or every auto-retry leaks
                // one IAudioClient.
                DisposeFailedInit();
                throw;
            }
        }
        _out.PlaybackStopped += OnOutPlaybackStopped;
        try
        {
            _out.Play();
        }
        catch
        {
            DisposeFailedInit();
            throw;
        }
        Log.Write($"OutputChannel: playing on '{device.FriendlyName}'");
    }

    /// <summary>
    /// Releases the audio client the failed constructor had already created. The caller never
    /// receives the instance, so nobody else can ever call <see cref="Dispose"/> on it.
    /// Deliberately leaves <see cref="Device"/> alone: the channel takes ownership of the device
    /// only once construction SUCCEEDS, so on failure the device is still the caller's to dispose
    /// (<c>ChannelActivationService.CleanUpFailedActivation</c>). Disposing it here would
    /// over-release the COM object.
    /// </summary>
    private void DisposeFailedInit()
    {
        _disposed = true;
        try
        {
            _out?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Write($"OutputChannel '{_deviceName}': failed-init client dispose failed: {ex.Message}");
        }
    }

    private void OnOutPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // Dispose unsubscribes first, so reaching here means an unsolicited stop: the
        // output endpoint died (invalidated after sleep/resume, unplugged, disabled).
        if (_disposed) return;
        Log.Write($"OutputChannel '{_deviceName}': playback stopped unexpectedly. error={e.Exception?.Message ?? "(none)"}");
        PlaybackStopped?.Invoke(this, e.Exception);
    }

    /// <summary>The per-channel graphic EQ; reconfigure it live via <see cref="EqualizerProvider.Configure"/>.</summary>
    public EqualizerProvider Equalizer => _equalizer;

    public float Gain
    {
        get => _gain;
        set { _gain = Math.Clamp(value, 0f, 2f); ApplyVolume(); }
    }

    public bool Muted
    {
        get => _muted;
        set { _muted = value; ApplyVolume(); }
    }

    private void ApplyVolume() => _volume.Volume = _muted ? 0f : _gain;

    internal void Write(byte[] buffer, int count)
    {
        // The capture thread reads a lock-free snapshot, so one last Write can race a
        // concurrent RemoveOutput; drop it instead of feeding a dead pipeline.
        if (_disposed) return;

        // Safety net only: AdaptiveResampler normally holds the backlog near its target,
        // but a stall or a big jump (device hiccup, format glitch) can still overrun it.
        // In that case drop the whole queue rather than let the delay creep up permanently.
        if (_buffer.BufferedDuration > _maxBacklog)
        {
            _buffer.ClearBuffer();
            Log.Write($"OutputChannel '{_deviceName}': backlog exceeded {_maxBacklog.TotalMilliseconds}ms, resynced");
        }
        _buffer.AddSamples(buffer, 0, count);
    }

    /// <summary>
    /// Records a failure thrown out of <see cref="Write"/>. Logs the first one only: the
    /// capture callback runs ~100 times a second, so a permanently broken output would
    /// otherwise flood the log with the same line.
    /// </summary>
    internal void NoteWriteFailure(Exception ex)
    {
        if (_writeFailureLogged) return;
        _writeFailureLogged = true;
        Log.Write($"OutputChannel '{_deviceName}': write failed, this output is dropping audio: {ex.Message}");
    }

    public void Dispose()
    {
        _disposed = true;
        _out.PlaybackStopped -= OnOutPlaybackStopped;
        // Stop/Dispose of a client whose device died can throw; never let teardown escape.
        try
        {
            _out.Stop();
            _out.Dispose();
        }
        catch (Exception ex)
        {
            Log.Write($"OutputChannel '{_deviceName}': dispose failed: {ex.Message}");
        }
        // Guarded too: a device that already went away can throw here, and an escaping
        // exception would skip the rest of the caller's teardown loop.
        try
        {
            Device.Dispose();
        }
        catch (Exception ex)
        {
            Log.Write($"OutputChannel '{_deviceName}': device dispose failed: {ex.Message}");
        }
    }
}
