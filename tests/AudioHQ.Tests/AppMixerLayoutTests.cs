using System.Collections.ObjectModel;
using AudioHQ.App;
using AudioHQ.App.ViewModels;

namespace AudioHQ.Tests;

public sealed class AppMixerLayoutTests
{
    [Fact]
    public void TogglePin_MovesRowToPinnedBoundary()
    {
        var rows = Rows(("a", true), ("b", false), ("c", false));

        var changed = AppMixerLayout.TogglePin(rows, rows[2]);

        Assert.True(changed);
        Assert.Equal(new[] { "a", "c", "b" }, rows.Select(r => r.Key));
        Assert.True(rows[1].IsPinned);
    }

    [Fact]
    public void MoveWithinPinGroup_RejectsCrossGroupMove()
    {
        var rows = Rows(("a", true), ("b", false));

        var changed = AppMixerLayout.MoveWithinPinGroup(rows, rows[0], rows[1]);

        Assert.False(changed);
        Assert.Equal(new[] { "a", "b" }, rows.Select(r => r.Key));
    }

    [Fact]
    public void ApplySavedOrder_KeepsPinnedRowsFirstAndUsesSavedOrder()
    {
        var rows = Rows(("c", false), ("b", true), ("a", true), ("d", false));
        var saved = new[]
        {
            new AppMixerDefinition { Key = "a", Pinned = true },
            new AppMixerDefinition { Key = "b", Pinned = true },
            new AppMixerDefinition { Key = "d", Pinned = false },
            new AppMixerDefinition { Key = "c", Pinned = false },
        };

        AppMixerLayout.ApplySavedOrder(rows, saved);

        Assert.Equal(new[] { "a", "b", "d", "c" }, rows.Select(r => r.Key));
    }

    [Fact]
    public void PersistLayout_KeepsAbsentSavedRowsAfterCurrentRows()
    {
        var rows = Rows(("b", true), ("a", false));
        var previous = new[]
        {
            new AppMixerDefinition { Key = "a", Pinned = false },
            new AppMixerDefinition { Key = "missing", Pinned = true },
        };

        var persisted = AppMixerLayout.PersistLayout(rows, previous);

        Assert.Equal(new[] { "b", "a", "missing" }, persisted.Select(r => r.Key));
        Assert.True(persisted[0].Pinned);
        Assert.True(persisted[2].Pinned);
    }

    private static ObservableCollection<Row> Rows(params (string Key, bool Pinned)[] rows) =>
        new(rows.Select(r => new Row(r.Key, r.Pinned)));

    private sealed class Row : IAppMixerRow
    {
        public Row(string key, bool isPinned)
        {
            Key = key;
            IsPinned = isPinned;
        }

        public string Key { get; }
        public bool IsPinned { get; set; }
    }
}
