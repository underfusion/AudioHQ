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

    /// <summary>
    /// "Bass-only" high-cut: a low-pass filter applied on top of the peaking bands. Passes
    /// everything below <see cref="LowPassHz"/> and rolls off above it - the correct tool for a
    /// bass shaker (keep the deep rumble, kill the rest). Off by default.
    /// </summary>
    public bool LowPassEnabled { get; set; }

    /// <summary>Low-pass cutoff in Hz (only used when <see cref="LowPassEnabled"/>).</summary>
    public double LowPassHz { get; set; } = 120.0;

    /// <summary>Low-pass slope as cascaded biquad stages: 1 = 12 dB/oct, 2 = 24 dB/oct.</summary>
    public int LowPassSlope { get; set; } = 2;

    public EqSettings Clone() => new()
    {
        Enabled = Enabled,
        Bands = Bands,
        GainsDb = (double[])GainsDb.Clone(),
        QValues = QValues is null ? null : (double[])QValues.Clone(),
        LowPassEnabled = LowPassEnabled,
        LowPassHz = LowPassHz,
        LowPassSlope = LowPassSlope,
    };
}
