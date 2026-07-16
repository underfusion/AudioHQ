using System;
using System.Collections.Generic;
using System.Linq;
using AudioHQ.Core;

namespace AudioHQ.App;

/// <summary>
/// The EQ editor's preset rules, with no WPF attached: which saved preset the live curve
/// corresponds to, whether it has unsaved edits, and what the save action should do.
/// <see cref="EqWindow"/> owns the controls; this owns the decisions (and the tests).
/// </summary>
public static class EqPresetMatcher
{
    // Tolerances: a curve is "the same" if no band differs audibly. Gains are shown to
    // 0.1 dB and Q to 0.01, so comparing tighter than this would report a preset as edited
    // purely from round-tripping it through the UI.
    private const double GainToleranceDb = 0.05;
    private const double QTolerance = 0.01;
    private const double LowPassToleranceHz = 0.5;

    /// <summary>True when two complete preset states match.</summary>
    public static bool CurveEquals(EqSettings a, EqSettings b)
    {
        if (a.Enabled != b.Enabled) return false;

        int bands = NormalizeBands(a.Bands);
        if (bands != NormalizeBands(b.Bands)) return false;

        double defaultQ = EqBands.Q(bands);
        for (int i = 0; i < bands; i++)
        {
            if (Math.Abs(GainAt(a, i) - GainAt(b, i)) > GainToleranceDb) return false;
            if (Math.Abs(QAt(a, i, defaultQ) - QAt(b, i, defaultQ)) > QTolerance) return false;
        }

        if (a.LowPassEnabled != b.LowPassEnabled) return false;
        if (a.LowPassEnabled &&
            (Math.Abs(a.LowPassHz - b.LowPassHz) > LowPassToleranceHz || a.LowPassSlope != b.LowPassSlope))
            return false;

        return true;
    }

    /// <summary>The saved preset whose curve matches <paramref name="current"/>, or null.</summary>
    public static EqPreset? FindMatching(IEnumerable<EqPreset> presets, EqSettings current) =>
        presets?.FirstOrDefault(p => CurveEquals(p.Eq, current));

    /// <summary>True when the live curve has drifted from the preset it was loaded from.</summary>
    public static bool IsDirty(EqPreset? active, EqSettings current) =>
        active is not null && !CurveEquals(active.Eq, current);

    /// <summary>The label shown over the picker while the curve does not match a saved preset.</summary>
    public static string StatusText(EqPreset? active) =>
        active is null ? "Custom (not saved)" : $"{active.Name} (not saved)";

    /// <summary>Reset reverts to the active preset, so it only applies to a dirty selection.</summary>
    public static bool CanReset(EqPreset? active, bool isDirty) => active is not null && isDirty;

    /// <summary>The built-in Default preset cannot be deleted.</summary>
    public static bool CanDelete(EqPreset? active) =>
        active is not null && !EqPresetStore.IsDefault(active);

    /// <summary>
    /// Save turns into Overwrite only when the user has not typed a new name and the
    /// selection is a dirty, non-Default preset. Typing a name always means "save as new".
    /// </summary>
    public static bool CanOverwrite(string? typedName, EqPreset? active, bool isDirty) =>
        string.IsNullOrWhiteSpace(typedName) && isDirty && CanDelete(active);

    // Band count is only ever 3 or 6; anything else is treated as 3 (settings written by an
    // older build, or hand-edited).
    private static int NormalizeBands(int bands) => bands == 6 ? 6 : 3;

    private static double GainAt(EqSettings s, int i) =>
        s.GainsDb is not null && i < s.GainsDb.Length ? s.GainsDb[i] : 0.0;

    // A missing or non-positive Q means "never set" - fall back to the band count's default.
    private static double QAt(EqSettings s, int i, double defaultQ) =>
        s.QValues is not null && i < s.QValues.Length && s.QValues[i] > 0 ? s.QValues[i] : defaultQ;
}
