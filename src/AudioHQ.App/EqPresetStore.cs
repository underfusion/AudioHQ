using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AudioHQ.Core;

namespace AudioHQ.App;

/// <summary>One named, reusable EQ curve, persisted in settings.json and shared across channels.</summary>
public sealed class EqPreset
{
    public string Name { get; set; } = "";
    public EqSettings Eq { get; set; } = new();
}

/// <summary>
/// The app-wide list of saved EQ presets. Owns an observable collection the EQ editor
/// binds to and a persist callback (the mixer's Save) invoked on every change.
/// </summary>
public sealed class EqPresetStore
{
    /// <summary>The built-in flat preset. Always present, cannot be overwritten or deleted.</summary>
    public const string DefaultName = "Default";

    private readonly Action _persist;

    public ObservableCollection<EqPreset> Presets { get; }

    public EqPresetStore(IEnumerable<EqPreset> initial, Action persist)
    {
        _persist = persist;
        // Always lead with a fresh built-in Default; drop any saved copy so it can't be tampered with.
        Presets = new ObservableCollection<EqPreset> { CreateDefault() };
        foreach (var preset in (initial ?? Enumerable.Empty<EqPreset>()).Where(p => !IsDefaultName(p.Name)))
            Presets.Add(preset);
    }

    public static bool IsDefaultName(string? name) =>
        string.Equals((name ?? "").Trim(), DefaultName, StringComparison.OrdinalIgnoreCase);

    public static bool IsDefault(EqPreset? preset) => preset is not null && IsDefaultName(preset.Name);

    private static EqPreset CreateDefault() => new()
    {
        Name = DefaultName,
        Eq = new EqSettings { Enabled = true, Bands = 3, GainsDb = new double[3] },
    };

    /// <summary>Presets that should be persisted - everything except the built-in Default.</summary>
    public IEnumerable<EqPreset> Persistable => Presets.Where(p => !IsDefault(p));

    /// <summary>Add a preset, or overwrite an existing one with the same name (case-insensitive).</summary>
    public EqPreset? Save(string name, EqSettings eq)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0 || IsDefaultName(trimmed)) return null; // Default is read-only

        var existing = Presets.FirstOrDefault(
            p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Eq = eq.Clone();
            _persist();
            return existing;
        }

        var preset = new EqPreset { Name = trimmed, Eq = eq.Clone() };
        Presets.Add(preset);
        _persist();
        return preset;
    }

    public void Delete(EqPreset? preset)
    {
        if (preset is null || IsDefault(preset) || !Presets.Remove(preset)) return;
        _persist();
    }
}
