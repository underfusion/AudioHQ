using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioHQ.Core;

/// <summary>
/// Milestone 1 core: captures everything playing on a source device (WASAPI loopback)
/// and renders a copy of it on a target device.
///
/// Kept as the single-target reference path; the GUI uses <see cref="MirrorEngine"/>.
/// Follows the same device-ownership rule as the rest of the engine: it OWNS both devices
/// and disposes them (see the lifetime rules in docs/ARCHITECTURE.md), so callers pass a
/// fresh <see cref="MMDevice"/> and do not dispose it themselves.
/// </summary>
public sealed class LoopbackMirror : IDisposable
{
    private readonly WasapiLoopbackCapture _capture;
    private readonly WasapiOut _output;
    private readonly BufferedWaveProvider _buffer;
    private readonly MMDevice _source;
    private readonly MMDevice _target;

    public LoopbackMirror(MMDevice source, MMDevice target, int outputLatencyMs = 100)
    {
        _source = source;
        _target = target;
        _capture = new WasapiLoopbackCapture(source);

        _buffer = new BufferedWaveProvider(_capture.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(EngineTunables.BufferSeconds),
        };

        ISampleProvider pipeline = _buffer.ToSampleProvider();

        int targetRate = target.AudioClient.MixFormat.SampleRate;
        if (_capture.WaveFormat.SampleRate != targetRate)
            pipeline = new WdlResamplingSampleProvider(pipeline, targetRate);

        _capture.DataAvailable += (_, e) => _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

        _output = new WasapiOut(target, AudioClientShareMode.Shared, useEventSync: true, outputLatencyMs);
        _output.Init(pipeline);
    }

    public void Start()
    {
        _capture.StartRecording();
        _output.Play();
    }

    public void Stop()
    {
        _capture.StopRecording();
        _output.Stop();
    }

    /// <summary>
    /// Every step is guarded: a device that already went away makes the driver throw, and an
    /// escaping exception would skip the rest of the teardown and leak the endpoints. Same
    /// rule as <see cref="MirrorEngine.Stop"/> - teardown never throws.
    /// </summary>
    public void Dispose()
    {
        Guard(Stop, "stop");
        Guard(_capture.Dispose, "capture dispose");
        Guard(_output.Dispose, "output dispose");
        Guard(_source.Dispose, "source dispose");
        Guard(_target.Dispose, "target dispose");
    }

    private static void Guard(Action step, string what)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            Log.Write($"LoopbackMirror: {what} failed: {ex.Message}");
        }
    }
}
