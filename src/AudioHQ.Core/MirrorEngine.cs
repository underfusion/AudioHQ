using NAudio.CoreAudioApi;
using NAudio.Wave;

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

    // Immutable snapshot of _outputs read by the capture callback. Republished under _lock
    // on every add/remove so the audio thread never takes a lock (a UI-thread add/remove or
    // a slow log write can then never stall the capture callback).
    private volatile OutputChannel[] _outputsSnapshot = Array.Empty<OutputChannel>();

    /// <summary>The engine OWNS this instance (disposed on Stop); callers pass a fresh MMDevice.</summary>
    public MMDevice? Source { get; private set; }

    /// <summary>Endpoint id of <see cref="Source"/>, cached at Start so health checks never
    /// have to read a property off a possibly-dead COM object.</summary>
    public string? SourceId { get; private set; }

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
        SourceId = source.ID;
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
        // Lock-free: reads the published snapshot. A channel removed concurrently may still
        // receive one last Write; OutputChannel guards that with its disposed flag.
        foreach (var output in _outputsSnapshot)
            output.Write(e.Buffer, e.BytesRecorded);
    }

    public OutputChannel AddOutput(MMDevice device, int latencyMs = 100)
    {
        if (_capture is null)
            throw new InvalidOperationException("Engine is not started.");

        var channel = new OutputChannel(device, _capture.WaveFormat, latencyMs);
        lock (_lock)
        {
            _outputs.Add(channel);
            _outputsSnapshot = _outputs.ToArray();
        }
        return channel;
    }

    public void RemoveOutput(OutputChannel channel)
    {
        lock (_lock)
        {
            _outputs.Remove(channel);
            _outputsSnapshot = _outputs.ToArray();
        }
        channel.Dispose();
    }

    public void Stop()
    {
        // Every teardown step is guarded: a source device that already went away makes the
        // driver throw from Stop/Dispose, and an escaping exception would leave the engine
        // holding a dead capture and undisposed outputs, so it could never start again.
        if (_capture is not null)
        {
            // Unsubscribe RecordingStopped first so the resulting stop is not mistaken for a
            // lost source (it is an intentional teardown).
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.DataAvailable -= OnDataAvailable;
            try
            {
                _capture.StopRecording();
            }
            catch (Exception ex)
            {
                Log.Write($"Engine.Stop: StopRecording failed: {ex.Message}");
            }
            try
            {
                _capture.Dispose();
            }
            catch (Exception ex)
            {
                Log.Write($"Engine.Stop: capture dispose failed: {ex.Message}");
            }
            _capture = null;
        }
        IsCapturing = false;

        OutputChannel[] outputs;
        lock (_lock)
        {
            outputs = _outputs.ToArray();
            _outputs.Clear();
            _outputsSnapshot = Array.Empty<OutputChannel>();
        }
        foreach (var output in outputs)
        {
            try
            {
                output.Dispose();
            }
            catch (Exception ex)
            {
                Log.Write($"Engine.Stop: output dispose failed: {ex.Message}");
            }
        }

        try
        {
            Source?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Write($"Engine.Stop: source dispose failed: {ex.Message}");
        }
        Source = null;
        SourceId = null;
    }

    public void Dispose() => Stop();
}
