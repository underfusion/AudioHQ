using System.Linq;
using AudioHQ.App;
using AudioHQ.Core;

namespace AudioHQ.Tests;

/// <summary>
/// The preset store's saving rules: the built-in Default is always present and untouchable,
/// saving by an existing name overwrites (case-insensitively), and every accepted change
/// persists exactly once. These protect the user's saved presets in settings.json.
/// </summary>
public sealed class EqPresetStoreTests
{
    private static EqSettings Curve(double firstGain = 0) => new()
    {
        Enabled = true,
        Bands = 3,
        GainsDb = new[] { firstGain, 0.0, 0.0 },
    };

    private static EqPresetStore Store(out Counter persists, params EqPreset[] initial)
    {
        var counter = new Counter();
        persists = counter;
        return new EqPresetStore(initial, () => counter.Count++);
    }

    private sealed class Counter { public int Count; }

    [Fact]
    public void Constructor_AlwaysLeadsWithTheBuiltInDefault()
    {
        var store = Store(out _, new EqPreset { Name = "Bass", Eq = Curve(6) });

        Assert.Equal(new[] { "Default", "Bass" }, store.Presets.Select(p => p.Name));
    }

    [Fact]
    public void Constructor_DropsASavedCopyOfDefault_SoItCannotBeTamperedWith()
    {
        // A hand-edited settings.json could smuggle in a non-flat "default"; the store must
        // replace it with the fresh built-in flat curve.
        var tampered = new EqPreset { Name = "default", Eq = Curve(12) };

        var store = Store(out _, tampered);

        var only = Assert.Single(store.Presets);
        Assert.Equal("Default", only.Name);
        Assert.All(only.Eq.GainsDb, g => Assert.Equal(0.0, g));
    }

    [Fact]
    public void Save_AddsANewPresetWithTheTrimmedName_AndPersists()
    {
        var store = Store(out var persists);

        var saved = store.Save("  Bass  ", Curve(6));

        Assert.NotNull(saved);
        Assert.Equal("Bass", saved!.Name);
        Assert.Contains(saved, store.Presets);
        Assert.Equal(1, persists.Count);
    }

    [Fact]
    public void Save_WithAnExistingName_OverwritesThatPreset_CaseInsensitively()
    {
        var store = Store(out var persists);
        var original = store.Save("Bass", Curve(6));

        var overwritten = store.Save("  BASS ", Curve(-3));

        // Same row, updated curve, no duplicate - the name collision is the overwrite.
        Assert.Same(original, overwritten);
        Assert.Equal(-3, overwritten!.Eq.GainsDb[0]);
        Assert.Equal(2, store.Presets.Count); // Default + Bass
        Assert.Equal(2, persists.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Default")]
    [InlineData(" default ")]
    public void Save_RejectsEmptyNamesAndTheBuiltInDefault(string name)
    {
        var store = Store(out var persists);

        Assert.Null(store.Save(name, Curve(6)));
        Assert.Single(store.Presets);
        Assert.Equal(0, persists.Count);
    }

    [Fact]
    public void Save_ClonesTheCurve_SoLaterEditsDoNotBleedIntoThePreset()
    {
        var store = Store(out _);
        var live = Curve(6);

        var saved = store.Save("Bass", live);
        live.GainsDb[0] = -12;

        Assert.Equal(6, saved!.Eq.GainsDb[0]);
    }

    [Fact]
    public void Delete_RemovesThePresetAndPersists()
    {
        var store = Store(out var persists);
        var bass = store.Save("Bass", Curve(6));

        store.Delete(bass);

        Assert.DoesNotContain(bass, store.Presets);
        Assert.Equal(2, persists.Count); // one for Save, one for Delete
    }

    [Fact]
    public void Delete_IgnoresNullUnknownAndTheBuiltInDefault()
    {
        var store = Store(out var persists);
        var defaultPreset = store.Presets.Single();

        store.Delete(null);
        store.Delete(new EqPreset { Name = "Never saved", Eq = Curve() });
        store.Delete(defaultPreset);

        Assert.Contains(defaultPreset, store.Presets);
        Assert.Equal(0, persists.Count);
    }

    [Fact]
    public void Persistable_ExcludesTheBuiltInDefault()
    {
        var store = Store(out _);
        store.Save("Bass", Curve(6));

        Assert.Equal(new[] { "Bass" }, store.Persistable.Select(p => p.Name));
    }
}
