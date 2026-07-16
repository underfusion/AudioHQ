namespace AudioHQ.Core;

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

    // Asymmetric fader range: modest boost headroom, deep cut so a band can be taken
    // (almost) out of the mix. Matches the "keep only the low end" workflow.
    public const double MaxBoostDb = 12.0;
    public const double MaxCutDb = 36.0;

    // "Bass-only" low-pass cutoff range (Hz) for the high-cut knob.
    public const double LowPassMinHz = 30.0;
    public const double LowPassMaxHz = 500.0;
    public const double LowPassDefaultHz = 120.0;
    public const int LowPassMaxStages = 2;

    public static float[] Frequencies(int bands) => bands == 6 ? Bands6 : Bands3;
    public static float Q(int bands) => bands == 6 ? Q6 : Q3;

    /// <summary>Short axis label for a centre frequency (e.g. "100", "1.2k", "8k").</summary>
    public static string Label(float hz) =>
        hz >= 1000f ? (hz % 1000f == 0f ? $"{hz / 1000f:0}k" : $"{hz / 1000f:0.0}k") : $"{hz:0}";
}
