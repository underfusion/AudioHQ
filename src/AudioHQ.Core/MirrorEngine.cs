using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioHQ.Core;

/// <summary>
/// Captures the source device via WASAPI loopback and fans the stream out
/// to any number of independent output channels, each with its own gain/mute.
/// </summary>
public sealed class MirrorEngine : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private readonly List<OutputChannel> _outputs = new();
    private readonly object _lock = new();

    public MMDevice? Source { get; private set; }

    public void Start(MMDevice source)
    {
        Stop();
        Source = source;
        Log.Write($"Engine.Start: source='{source.FriendlyName}'");
        _capture = new WasapiLoopbackCapture(source);
        _capture.DataAvailable += OnDataAvailable;
        _capture.StartRecording();
        Log.Write($"Engine.Start: capturing OK, format={_capture.WaveFormat}");
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            foreach (var output in _outputs)
                output.Write(e.Buffer, e.BytesRecorded);
        }
    }

    public OutputChannel AddOutput(MMDevice device, int latencyMs = 100)
    {
        if (_capture is null)
            throw new InvalidOperationException("Engine is not started.");

        var channel = new OutputChannel(device, _capture.WaveFormat, latencyMs);
        lock (_lock) _outputs.Add(channel);
        return channel;
    }

    public void RemoveOutput(OutputChannel channel)
    {
        lock (_lock) _outputs.Remove(channel);
        channel.Dispose();
    }

    public void Stop()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.StopRecording();
            _capture.Dispose();
            _capture = null;
        }

        lock (_lock)
        {
            foreach (var output in _outputs)
                output.Dispose();
            _outputs.Clear();
        }
    }

    public void Dispose() => Stop();
}

/// <summary>One mirrored output: buffer -> resample -> gain/mute -> WASAPI render.</summary>
public sealed class OutputChannel : IDisposable
{
    private readonly BufferedWaveProvider _buffer;
    private readonly VolumeSampleProvider _volume;
    private readonly WasapiOut _out;
    private readonly TimeSpan _maxBacklog;

    private float _gain = 1f;
    private bool _muted;

    public MMDevice Device { get; }

    internal OutputChannel(MMDevice device, WaveFormat captureFormat, int latencyMs)
    {
        Device = device;

        var mixFormat = device.AudioClient.MixFormat;
        // Allow some jitter headroom above the render buffer before resyncing.
        // Capture delivers ~10ms chunks, so anything above latency + ~25ms is pure added delay.
        _maxBacklog = TimeSpan.FromMilliseconds(latencyMs + 25);
        Log.Write($"OutputChannel: device='{device.FriendlyName}', capture={captureFormat}, deviceMix={mixFormat}, latency={latencyMs}ms, maxBacklog={_maxBacklog.TotalMilliseconds}ms");

        _buffer = new BufferedWaveProvider(captureFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(2),
        };

        ISampleProvider pipeline = _buffer.ToSampleProvider();

        int targetRate = mixFormat.SampleRate;
        if (captureFormat.SampleRate != targetRate)
        {
            pipeline = new WdlResamplingSampleProvider(pipeline, targetRate);
            Log.Write($"OutputChannel: resampling {captureFormat.SampleRate} -> {targetRate}");
        }

        _volume = new VolumeSampleProvider(pipeline) { Volume = _gain };

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
            _out = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: false, latencyMs);
            _out.Init(_volume);
            Log.Write("OutputChannel: push-mode init OK");
        }
        _out.Play();
        Log.Write($"OutputChannel: playing on '{device.FriendlyName}'");
    }

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
        // If the queue outgrows the target (slow device, clock drift), resync instead of
        // letting the delay creep up permanently.
        if (_buffer.BufferedDuration > _maxBacklog)
        {
            _buffer.ClearBuffer();
            Log.Write($"OutputChannel '{Device.FriendlyName}': backlog exceeded {_maxBacklog.TotalMilliseconds}ms, resynced");
        }
        _buffer.AddSamples(buffer, 0, count);
    }

    public void Dispose()
    {
        _out.Stop();
        _out.Dispose();
    }
}
