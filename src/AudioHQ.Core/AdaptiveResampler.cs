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
    // Largest ratio deviation we ever apply (0.5% ~= 8 cents of pitch - inaudible).
    private const double MaxCorrection = 0.005;
    // Proportional gain: correction per second of trough error (0.01 s error -> max).
    private const double Gain = 0.5;
    // EMA factor applied to the trough each control tick (~5/s) -> ~1 s settling.
    private const double TroughSmoothing = 0.3;
    // Hard cap on the fill loop so a pathological ratio can never spin forever.
    private const int MaxIterations = 8;

    private readonly ISampleProvider _source;
    private readonly WdlResampler _resampler;
    private readonly Func<double> _bufferedSeconds;
    private readonly double _targetSeconds;
    private readonly double _nominalInRate;
    private readonly int _channels;
    private readonly string _label;
    private readonly long _controlFrames; // frames per control tick (~0.2 s)

    private double _windowMinFill = double.MaxValue;
    private long _windowFrames;
    private double _smoothedTrough;

    // --- throttled diagnostics (one summary line per ~second of output) ---
    private long _logFrames;
    private double _logMinFillMs = double.MaxValue;
    private double _logMaxFillMs;
    private double _lastCorrection;
    private int _maxIterations;

    public WaveFormat WaveFormat { get; }

    /// <param name="source">Upstream provider (the buffered capture stream).</param>
    /// <param name="targetRate">Output sample rate (the device mix rate).</param>
    /// <param name="bufferedSeconds">Live read of the upstream backlog, in seconds.</param>
    /// <param name="targetSeconds">Trough backlog the controller steers toward.</param>
    /// <param name="label">Device name, for the diagnostic log lines.</param>
    public AdaptiveResampler(ISampleProvider source, int targetRate,
                             Func<double> bufferedSeconds, double targetSeconds, string label)
    {
        _source = source;
        _bufferedSeconds = bufferedSeconds;
        _targetSeconds = targetSeconds;
        _smoothedTrough = targetSeconds;
        _channels = source.WaveFormat.Channels;
        _nominalInRate = source.WaveFormat.SampleRate;
        _label = label;
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
            _lastCorrection = Math.Clamp(error * Gain, -MaxCorrection, MaxCorrection);
            _resampler.SetRates(_nominalInRate * (1.0 + _lastCorrection), WaveFormat.SampleRate);
            _windowMinFill = double.MaxValue;
            _windowFrames = 0;
        }

        Diagnose(fill, iterations, framesProduced);
        return framesProduced * _channels;
    }

    // Emits one summary line per ~second of output: backlog range, smoothed trough, applied
    // correction, worst-case loop depth. Temporary instrumentation for the crackle
    // investigation; throttled so it touches the log at most ~1/s on the hot path.
    private void Diagnose(double fillSeconds, int iterations, int framesProduced)
    {
        double fillMs = fillSeconds * 1000.0;
        if (fillMs < _logMinFillMs) _logMinFillMs = fillMs;
        if (fillMs > _logMaxFillMs) _logMaxFillMs = fillMs;
        if (iterations > _maxIterations) _maxIterations = iterations;

        _logFrames += framesProduced;
        if (_logFrames < WaveFormat.SampleRate) return;

        Log.Write($"AdaptiveResampler '{_label}': fill {_logMinFillMs:0.0}-{_logMaxFillMs:0.0}ms, " +
                  $"trough~{_smoothedTrough * 1000:0.0}ms, corr={_lastCorrection * 100:0.000}%, maxIter={_maxIterations}");
        _logFrames = 0;
        _logMinFillMs = double.MaxValue;
        _logMaxFillMs = 0;
        _maxIterations = 0;
    }
}
