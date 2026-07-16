using NAudio.Dsp;
using NAudio.Wave;

namespace AudioHQ.Core;

/// <summary>
/// Sample-rate converter that continuously nudges its conversion ratio to keep each
/// output's backlog safe. It compensates for the unavoidable drift between the
/// capture-device clock and the output-device clock by gently speeding up or slowing
/// down playback (by a fraction of a percent - inaudible) so the latency holds steady.
///
/// It steers the per-window MINIMUM backlog (the "trough"), not the average. A bursty
/// source - e.g. a wireless endpoint that delivers ~60 ms of audio at a time instead of
/// ~10 ms - makes the backlog saw-tooth by the burst size; controlling the trough keeps
/// that low point above one render pull (so the buffer never starves and feeds silence)
/// while letting the peaks ride as high as the bursts push them. The conversion ratio is
/// recomputed only a few times per second, so it never jitters per callback.
/// It also folds in the fixed capture-rate -> device-rate conversion, so it always runs
/// even when the rates already match (the drift still needs correcting).
/// </summary>
public sealed class AdaptiveResampler : ISampleProvider
{
    // Tuning lives in EngineTunables (const, so these still inline into the hot loop).
    private const double MaxCorrection = EngineTunables.ResamplerMaxCorrection;
    private const double Gain = EngineTunables.ResamplerGain;
    private const double TroughSmoothing = EngineTunables.ResamplerTroughSmoothing;
    private const int MaxIterations = EngineTunables.ResamplerMaxIterations;

    private readonly ISampleProvider _source;
    private readonly WdlResampler _resampler;
    private readonly Func<double> _bufferedSeconds;
    private readonly double _targetSeconds;
    private readonly double _nominalInRate;
    private readonly int _channels;
    private readonly long _controlFrames; // frames per control tick (~0.2 s)

    private double _windowMinFill = double.MaxValue;
    private long _windowFrames;
    private double _smoothedTrough;

    public WaveFormat WaveFormat { get; }

    /// <param name="source">Upstream provider (the buffered capture stream).</param>
    /// <param name="targetRate">Output sample rate (the device mix rate).</param>
    /// <param name="bufferedSeconds">Live read of the upstream backlog, in seconds.</param>
    /// <param name="targetSeconds">Trough backlog the controller steers toward.</param>
    public AdaptiveResampler(ISampleProvider source, int targetRate,
                             Func<double> bufferedSeconds, double targetSeconds)
    {
        _source = source;
        _bufferedSeconds = bufferedSeconds;
        _targetSeconds = targetSeconds;
        _smoothedTrough = targetSeconds;
        _channels = source.WaveFormat.Channels;
        _nominalInRate = source.WaveFormat.SampleRate;
        _controlFrames = targetRate / 5; // recompute the ratio ~5x per second

        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(targetRate, _channels);

        // Same setup WdlResamplingSampleProvider uses; output-driven so each Read pulls
        // exactly as many input frames as the requested output count needs.
        _resampler = new WdlResampler();
        _resampler.SetMode(true, 2, false);
        _resampler.SetFilterParms();
        _resampler.SetFeedMode(false);
        _resampler.SetRates(_nominalInRate, targetRate);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        double fill = _bufferedSeconds();
        if (fill < _windowMinFill) _windowMinFill = fill;

        // Fill the WHOLE output buffer. A single ResampleOut can return fewer frames than
        // asked (fractional position / filter priming); the buffered source zero-pads so it
        // never starves, hence the loop always completes.
        int framesRequested = count / _channels;
        int framesProduced = 0;
        int iterations = 0;
        while (framesProduced < framesRequested && iterations < MaxIterations)
        {
            iterations++;
            int need = framesRequested - framesProduced;
            int inNeeded = _resampler.ResamplePrepare(need, _channels, out float[] inBuffer, out int inBufferOffset);
            int inGot = _source.Read(inBuffer, inBufferOffset, inNeeded * _channels) / _channels;
            int outGot = _resampler.ResampleOut(buffer, offset + framesProduced * _channels, inGot, need, _channels);
            framesProduced += outGot;
            if (outGot == 0) break;
        }

        _windowFrames += framesProduced;
        if (_windowFrames >= _controlFrames)
        {
            // Drive the smoothed trough toward target. error < 0 means the low point is
            // dipping toward starvation, so slow down (consume less input) to let it refill;
            // error > 0 means we are carrying needless latency, so speed up to drain it.
            _smoothedTrough += TroughSmoothing * (_windowMinFill - _smoothedTrough);
            double error = _smoothedTrough - _targetSeconds;
            double correction = Math.Clamp(error * Gain, -MaxCorrection, MaxCorrection);
            _resampler.SetRates(_nominalInRate * (1.0 + correction), WaveFormat.SampleRate);
            _windowMinFill = double.MaxValue;
            _windowFrames = 0;
        }

        // Never return 0 from a live pipeline: WasapiOut treats a zero read as end-of-stream
        // and stops playback for good. If the resampler momentarily produced nothing
        // (filter priming right after a resync), hand back silence instead.
        if (framesProduced == 0 && framesRequested > 0)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }

        return framesProduced * _channels;
    }
}
