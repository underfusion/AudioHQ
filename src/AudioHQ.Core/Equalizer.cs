using System;
using NAudio.Dsp;
using NAudio.Wave;

namespace AudioHQ.Core;

/// <summary>
/// Per-channel graphic-equalizer state: on/off, band count (3 or 6) and the gain
/// (in dB) of each band. Plain data - persisted with the channel definition and
/// handed to <see cref="EqualizerProvider"/> to build the actual filters.
/// </summary>
public sealed class EqSettings
{
    public bool Enabled { get; set; }
    public int Bands { get; set; } = 3;
    public double[] GainsDb { get; set; } = new double[3];

    /// <summary>Per-band Q (bell width). Null/empty means "use the band-count default".</summary>
    public double[]? QValues { get; set; }

    public EqSettings Clone() => new()
    {
        Enabled = Enabled,
        Bands = Bands,
        GainsDb = (double[])GainsDb.Clone(),
        QValues = QValues is null ? null : (double[])QValues.Clone(),
    };
}

/// <summary>Fixed centre frequencies (Hz) and Q for the 3- and 6-band graphic-EQ presets.</summary>
public static class EqBands
{
    public static readonly float[] Bands3 = { 100f, 1000f, 8000f };
    public static readonly float[] Bands6 = { 80f, 200f, 500f, 1200f, 3000f, 8000f };

    // Lower Q = wider, more overlapping bands. The sparse 3-band wants wide bells so the
    // whole spectrum is covered; the denser 6-band uses a tighter Q.
    public const float Q3 = 0.7f;
    public const float Q6 = 1.1f;

    // User-adjustable Q range for the per-band knobs: low = wide/round bell,
    // high = narrow/sharp bell.
    public const double QMin = 0.3;
    public const double QMax = 4.0;

    public const double MaxGainDb = 12.0;

    public static float[] Frequencies(int bands) => bands == 6 ? Bands6 : Bands3;
    public static float Q(int bands) => bands == 6 ? Q6 : Q3;

    /// <summary>Short axis label for a centre frequency (e.g. "100", "1.2k", "8k").</summary>
    public static string Label(float hz) =>
        hz >= 1000f ? (hz % 1000f == 0f ? $"{hz / 1000f:0}k" : $"{hz / 1000f:0.0}k") : $"{hz:0}";
}

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

    private BiQuadFilter[,]? _filters; // [channel, band]; null while disabled
    private bool _enabled;

    public EqualizerProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _sampleRate = source.WaveFormat.SampleRate;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>
    /// Rebuild the filter bank from settings. Safe to call on the UI thread while audio
    /// runs: the new bank is published atomically under the lock.
    /// </summary>
    public void Configure(EqSettings? eq)
    {
        if (eq is null || !eq.Enabled)
        {
            lock (_lock) { _enabled = false; _filters = null; }
            Log.Write($"Equalizer: disabled ({_channels}ch, {_sampleRate}Hz)");
            return;
        }

        int bands = eq.Bands == 6 ? 6 : 3;
        var freqs = EqBands.Frequencies(bands);
        float defaultQ = EqBands.Q(bands);
        var qs = eq.QValues;

        var filters = new BiQuadFilter[_channels, bands];
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
                filters[c, b] = BiQuadFilter.PeakingEQ(_sampleRate, freq, q, gainDb);
        }

        lock (_lock) { _filters = filters; _enabled = true; }
        Log.Write($"Equalizer: {bands} bands enabled ({_channels}ch, {_sampleRate}Hz)");
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        lock (_lock)
        {
            if (!_enabled || _filters is null) return read;
            int bands = _filters.GetLength(1);
            for (int n = 0; n < read; n++)
            {
                int c = n % _channels;
                float s = buffer[offset + n];
                for (int b = 0; b < bands; b++)
                    s = _filters[c, b].Transform(s);
                buffer[offset + n] = s;
            }
        }
        return read;
    }
}
