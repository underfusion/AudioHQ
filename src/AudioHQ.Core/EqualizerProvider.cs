using System;
using NAudio.Dsp;
using NAudio.Wave;

namespace AudioHQ.Core;

/// <summary>
/// Inserts a bank of peaking-EQ biquad filters into the sample chain - one filter per
/// band per audio channel. Disabled by default (pure pass-through); reconfigured live
/// from the UI. Filter swaps are locked against the audio Read so a gain change can
/// never tear a filter mid-block.
/// </summary>
public sealed class EqualizerProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly object _lock = new();

    private BiQuadFilter[,]? _filters;  // [channel, band]; null while disabled
    private BiQuadFilter[,]? _lowPass;  // [channel, stage]; null when the high-cut is off
    private bool _enabled;

    public EqualizerProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _sampleRate = source.WaveFormat.SampleRate;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>
    /// Apply settings to the filter bank. Safe to call on the UI thread while audio runs.
    /// When the topology (band count, low-pass stages) is unchanged, the existing filters'
    /// coefficients are updated IN PLACE under the lock: their delay-line state survives, so
    /// dragging a fader (which calls this dozens of times per second) never resets the
    /// filters and never clicks. The bank is only rebuilt on a topology change.
    /// </summary>
    public void Configure(EqSettings? eq)
    {
        if (eq is null || !eq.Enabled)
        {
            bool wasEnabled;
            lock (_lock) { wasEnabled = _enabled; _enabled = false; _filters = null; _lowPass = null; }
            if (wasEnabled) Log.Write($"Equalizer: disabled ({_channels}ch, {_sampleRate}Hz)");
            return;
        }

        int bands = eq.Bands == 6 ? 6 : 3;
        var freqs = EqBands.Frequencies(bands);
        float defaultQ = EqBands.Q(bands);
        var qs = eq.QValues;

        int lpStages = eq.LowPassEnabled ? Math.Clamp(eq.LowPassSlope, 1, EqBands.LowPassMaxStages) : 0;
        double cutoff = Math.Clamp(eq.LowPassHz, EqBands.LowPassMinHz, EqBands.LowPassMaxHz);
        cutoff = Math.Min(cutoff, _sampleRate / 2f - 1f); // keep below Nyquist on low rates

        bool rebuilt;
        lock (_lock)
        {
            rebuilt = _filters is null || _filters.GetLength(1) != bands;
            if (rebuilt) _filters = new BiQuadFilter[_channels, bands];

            for (int b = 0; b < bands; b++)
            {
                float gainDb = (float)(b < eq.GainsDb.Length ? eq.GainsDb[b] : 0.0);
                // Per-band Q if the user set one, else the band-count default. Clamp to the knob range.
                float q = qs is not null && b < qs.Length && qs[b] > 0
                    ? (float)Math.Clamp(qs[b], EqBands.QMin, EqBands.QMax)
                    : defaultQ;
                // Centre frequency must stay below Nyquist; clamp the top band on low rates.
                float freq = Math.Min(freqs[b], _sampleRate / 2f - 1f);
                for (int c = 0; c < _channels; c++)
                {
                    if (rebuilt)
                        _filters![c, b] = BiQuadFilter.PeakingEQ(_sampleRate, freq, q, gainDb);
                    else
                        _filters![c, b].SetPeakingEq(_sampleRate, freq, q, gainDb);
                }
            }

            // "Bass-only" high-cut: a cascade of identical low-pass biquads. Each Butterworth
            // stage is 12 dB/oct, so two stages give a 24 dB/oct rolloff above the cutoff.
            if (lpStages == 0)
            {
                _lowPass = null;
            }
            else
            {
                bool rebuildLp = _lowPass is null || _lowPass.GetLength(1) != lpStages;
                if (rebuildLp) _lowPass = new BiQuadFilter[_channels, lpStages];
                for (int s = 0; s < lpStages; s++)
                    for (int c = 0; c < _channels; c++)
                    {
                        if (rebuildLp)
                            _lowPass![c, s] = BiQuadFilter.LowPassFilter(_sampleRate, (float)cutoff, 0.707f);
                        else
                            _lowPass![c, s].SetLowPassFilter(_sampleRate, (float)cutoff, 0.707f);
                    }
            }

            _enabled = true;
        }

        // Log only structural changes, not every fader tick.
        if (rebuilt)
            Log.Write($"Equalizer: {bands} bands enabled ({_channels}ch, {_sampleRate}Hz)" +
                      (lpStages > 0 ? $", low-pass {cutoff:0}Hz x{lpStages} stage(s)" : ""));
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        lock (_lock)
        {
            if (!_enabled || _filters is null) return read;
            int bands = _filters.GetLength(1);
            int lpStages = _lowPass?.GetLength(1) ?? 0;
            for (int n = 0; n < read; n++)
            {
                int c = n % _channels;
                float s = buffer[offset + n];
                for (int b = 0; b < bands; b++)
                    s = _filters[c, b].Transform(s);
                for (int st = 0; st < lpStages; st++)
                    s = _lowPass![c, st].Transform(s);
                buffer[offset + n] = s;
            }
        }
        return read;
    }
}
