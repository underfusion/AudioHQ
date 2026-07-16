using AudioHQ.App;
using AudioHQ.Core;

namespace AudioHQ.Tests;

/// <summary>
/// The EQ editor's preset rules: which saved preset the live curve matches, whether it has
/// unsaved edits, and what Save/Reset/Delete should offer. These decide what the user sees
/// on the preset picker, so the tolerances matter - too tight and simply loading a preset
/// reports it as edited.
/// </summary>
public sealed class EqPresetMatcherTests
{
    private static EqSettings Curve(
        bool enabled = true,
        int bands = 3,
        double[]? gains = null,
        double[]? qs = null,
        bool lowPass = false,
        double lowPassHz = 120,
        int slope = 1) => new()
        {
            Enabled = enabled,
            Bands = bands,
            GainsDb = gains ?? new[] { 0.0, 0.0, 0.0 },
            QValues = qs,
            LowPassEnabled = lowPass,
            LowPassHz = lowPassHz,
            LowPassSlope = slope,
        };

    [Fact]
    public void CurveEquals_IdenticalCurves_Match() =>
        Assert.True(EqPresetMatcher.CurveEquals(
            Curve(gains: new[] { 3.0, -2.0, 1.0 }),
            Curve(gains: new[] { 3.0, -2.0, 1.0 })));

    [Fact]
    public void CurveEquals_DifferentEnabledState_DoesNotMatch() =>
        Assert.False(EqPresetMatcher.CurveEquals(Curve(enabled: true), Curve(enabled: false)));

    [Fact]
    public void CurveEquals_DifferentBandCount_DoesNotMatch() =>
        Assert.False(EqPresetMatcher.CurveEquals(
            Curve(bands: 3, gains: new[] { 0.0, 0.0, 0.0 }),
            Curve(bands: 6, gains: new[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 })));

    [Fact]
    public void CurveEquals_BandCountOtherThan6_IsTreatedAs3()
    {
        // Settings written by an older build (or hand-edited) must not read as a new curve.
        Assert.True(EqPresetMatcher.CurveEquals(
            Curve(bands: 0, gains: new[] { 1.0, 2.0, 3.0 }),
            Curve(bands: 3, gains: new[] { 1.0, 2.0, 3.0 })));
    }

    [Fact]
    public void CurveEquals_GainDifferenceBelowTolerance_StillMatches() =>
        Assert.True(EqPresetMatcher.CurveEquals(
            Curve(gains: new[] { 3.00, 0.0, 0.0 }),
            Curve(gains: new[] { 3.04, 0.0, 0.0 })));

    [Fact]
    public void CurveEquals_AudibleGainDifference_DoesNotMatch() =>
        Assert.False(EqPresetMatcher.CurveEquals(
            Curve(gains: new[] { 3.0, 0.0, 0.0 }),
            Curve(gains: new[] { 3.5, 0.0, 0.0 })));

    [Fact]
    public void CurveEquals_UnsetQ_MatchesTheBandDefault()
    {
        // A null/zero Q means "never set", so it must compare equal to the explicit default
        // rather than reporting a saved preset as edited the moment the UI fills Q in.
        double defaultQ = EqBands.Q(3);
        Assert.True(EqPresetMatcher.CurveEquals(
            Curve(qs: null),
            Curve(qs: new[] { defaultQ, defaultQ, defaultQ })));
        Assert.True(EqPresetMatcher.CurveEquals(
            Curve(qs: new[] { 0.0, 0.0, 0.0 }),
            Curve(qs: new[] { defaultQ, defaultQ, defaultQ })));
    }

    [Fact]
    public void CurveEquals_DifferentQ_DoesNotMatch() =>
        Assert.False(EqPresetMatcher.CurveEquals(
            Curve(qs: new[] { 0.7, 0.7, 0.7 }),
            Curve(qs: new[] { 2.0, 0.7, 0.7 })));

    [Fact]
    public void CurveEquals_LowPassOnlyComparedWhenEnabled()
    {
        // Both off: a stale Hz value must not count as a difference.
        Assert.True(EqPresetMatcher.CurveEquals(
            Curve(lowPass: false, lowPassHz: 100),
            Curve(lowPass: false, lowPassHz: 900)));
        // Both on: it must.
        Assert.False(EqPresetMatcher.CurveEquals(
            Curve(lowPass: true, lowPassHz: 100),
            Curve(lowPass: true, lowPassHz: 900)));
        Assert.False(EqPresetMatcher.CurveEquals(
            Curve(lowPass: true, slope: 1),
            Curve(lowPass: true, slope: 2)));
    }

    [Fact]
    public void FindMatching_ReturnsThePresetWithTheSameCurve()
    {
        var bass = new EqPreset { Name = "Bass", Eq = Curve(gains: new[] { 6.0, 0.0, 0.0 }) };
        var flat = new EqPreset { Name = "Flat", Eq = Curve(gains: new[] { 0.0, 0.0, 0.0 }) };

        Assert.Same(flat, EqPresetMatcher.FindMatching(new[] { bass, flat }, Curve(gains: new[] { 0.0, 0.0, 0.0 })));
        Assert.Null(EqPresetMatcher.FindMatching(new[] { bass, flat }, Curve(gains: new[] { -9.0, 0.0, 0.0 })));
    }

    [Fact]
    public void IsDirty_OnlyWhenAnActivePresetHasDrifted()
    {
        var preset = new EqPreset { Name = "Bass", Eq = Curve(gains: new[] { 6.0, 0.0, 0.0 }) };

        Assert.False(EqPresetMatcher.IsDirty(null, Curve()));
        Assert.False(EqPresetMatcher.IsDirty(preset, Curve(gains: new[] { 6.0, 0.0, 0.0 })));
        Assert.True(EqPresetMatcher.IsDirty(preset, Curve(gains: new[] { 2.0, 0.0, 0.0 })));
    }

    [Fact]
    public void StatusText_NamesThePresetBeingEdited()
    {
        Assert.Equal("Custom (not saved)", EqPresetMatcher.StatusText(null));
        Assert.Equal("Bass (not saved)", EqPresetMatcher.StatusText(new EqPreset { Name = "Bass" }));
    }

    [Fact]
    public void CanOverwrite_OnlyForADirtyNonDefaultPresetWithNoTypedName()
    {
        var bass = new EqPreset { Name = "Bass", Eq = Curve() };

        Assert.True(EqPresetMatcher.CanOverwrite("", bass, isDirty: true));
        Assert.True(EqPresetMatcher.CanOverwrite("   ", bass, isDirty: true));
        // Typing a name means "save as new", never overwrite.
        Assert.False(EqPresetMatcher.CanOverwrite("New name", bass, isDirty: true));
        Assert.False(EqPresetMatcher.CanOverwrite("", bass, isDirty: false));
        Assert.False(EqPresetMatcher.CanOverwrite("", null, isDirty: true));
    }

    [Fact]
    public void CanDeleteAndOverwrite_NeverApplyToTheBuiltInDefault()
    {
        // The built-in Default is read-only; it is identified by name, case-insensitively.
        var builtIn = new EqPreset { Name = "Default", Eq = Curve() };
        Assert.True(EqPresetStore.IsDefault(builtIn), "test assumes 'Default' is the built-in name");

        Assert.False(EqPresetMatcher.CanDelete(builtIn));
        Assert.False(EqPresetMatcher.CanOverwrite("", builtIn, isDirty: true));
    }

    [Fact]
    public void CanReset_OnlyWhenTheSelectionHasUnsavedEdits()
    {
        var bass = new EqPreset { Name = "Bass", Eq = Curve() };

        Assert.True(EqPresetMatcher.CanReset(bass, isDirty: true));
        Assert.False(EqPresetMatcher.CanReset(bass, isDirty: false));
        Assert.False(EqPresetMatcher.CanReset(null, isDirty: true));
    }
}
