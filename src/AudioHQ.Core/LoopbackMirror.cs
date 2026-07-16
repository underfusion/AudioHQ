using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioHQ.Core;

/// <summary>
/// Milestone 1 core: captures everything playing on a source device (WASAPI loopback)
/// and renders a copy of it on a target device.
/// </summary>
public sealed class LoopbackMirror : IDisposable
{
    private readonly WasapiLoopbackCapture _capture;
    private readonly WasapiOut _output;
    private readonly BufferedWaveProvider _buffer;

    public LoopbackMirror(MMDevice source, MMDevice target, int outputLatencyMs = 100)
    {
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

    public void Dispose()
    {
        Stop();
        _capture.Dispose();
        _output.Dispose();
    }
}
