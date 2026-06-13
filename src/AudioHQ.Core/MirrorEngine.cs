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

    /// <summary>True while a capture is live (as far as the driver has told us).</summary>
    public bool IsCapturing { get; private set; }

    /// <summary>
    /// Raised when capture stops on its own - the source device was removed, disabled or
    /// invalidated - as opposed to an intentional <see cref="Stop"/>. The argument is the
    /// driver exception, if any. May fire on a background thread; marshal before touching UI.
    /// </summary>
    public event Action<Exception?>? SourceLost;

    public void Start(MMDevice source)
    {
        Stop();
        Source = source;
        Log.Write($"Engine.Start: source='{source.FriendlyName}'");
        _capture = new WasapiLoopbackCapture(source);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
        IsCapturing = true;
        Log.Write($"Engine.Start: capturing OK, format={_capture.WaveFormat}");
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // Stop() unsubscribes this handler before stopping, so reaching here always means an
        // unsolicited stop: the source endpoint went away (unplugged/disabled/invalidated).
        IsCapturing = false;
        Log.Write($"Engine: capture stopped unexpectedly (source lost). error={e.Exception?.Message ?? "(none)"}");
        SourceLost?.Invoke(e.Exception);
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
            // Unsubscribe RecordingStopped first so the resulting stop is not mistaken for a
            // lost source (it is an intentional teardown).
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.DataAvailable -= OnDataAvailable;
            _capture.StopRecording();
            _capture.Dispose();
            _capture = null;
        }
        IsCapturing = false;

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
    private readonly EqualizerProvider _equalizer;
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

        int targetRate = mixFormat.SampleRate;
        // Trough (minimum backlog) the controller steers toward. It must stay ABOVE the
        // WASAPI pull granularity (~latencyMs per render callback) plus delivery jitter, or
        // the buffer underruns at the low point and we feed silence (crackle). latency + 5ms
        // is the trimmed-down margin; still under the hard resync at latency + 25ms so normal
        // drift never trips it. Raise back toward +10 if a jittery source starts to crackle.
        double targetBacklogMs = latencyMs + 5.0;
        ISampleProvider pipeline = new AdaptiveResampler(
            _buffer.ToSampleProvider(),
            targetRate,
            () => _buffer.BufferedDuration.TotalSeconds,
            targetBacklogMs / 1000.0,
            device.FriendlyName);
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
            _out = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: false, latencyMs);
            _out.Init(_volume);
            Log.Write("OutputChannel: push-mode init OK");
        }
        _out.Play();
        Log.Write($"OutputChannel: playing on '{device.FriendlyName}'");
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
        // Safety net only: AdaptiveResampler normally holds the backlog near its target,
        // but a stall or a big jump (device hiccup, format glitch) can still overrun it.
        // In that case drop the whole queue rather than let the delay creep up permanently.
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
