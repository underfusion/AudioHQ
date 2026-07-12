using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AudioHQ.App;

namespace AudioHQ.App.ViewModels;

public static class AppMixerLayout
{
    public static bool TogglePin<T>(ObservableCollection<T> rows, T row)
        where T : class, IAppMixerRow
    {
        int current = rows.IndexOf(row);
        if (current < 0) return false;

        row.IsPinned = !row.IsPinned;

        int target = rows.Count(a => a.IsPinned && !ReferenceEquals(a, row));
        target = Math.Clamp(target, 0, rows.Count - 1);
        if (current != target) rows.Move(current, target);
        return true;
    }

    public static bool MoveWithinPinGroup<T>(ObservableCollection<T> rows, T source, T target)
        where T : class, IAppMixerRow
    {
        if (source is null || target is null || ReferenceEquals(source, target)) return false;
        if (source.IsPinned != target.IsPinned) return false;

        int from = rows.IndexOf(source), to = rows.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return false;

        rows.Move(from, to);
        return true;
    }

    public static void ApplySavedOrder<T>(ObservableCollection<T> rows, IReadOnlyList<AppMixerDefinition> savedRows)
        where T : class, IAppMixerRow
    {
        if (savedRows.Count == 0 || rows.Count < 2) return;

        var savedOrder = savedRows
            .Select((state, index) => new { state.Key, state.Pinned, Index = index })
            .ToDictionary(x => x.Key);

        var ordered = rows
            .Select((row, index) => new { Row = row, CurrentIndex = index })
            .OrderBy(x => x.Row.IsPinned ? 0 : 1)
            .ThenBy(x => savedOrder.TryGetValue(x.Row.Key, out var state) ? state.Index : int.MaxValue)
            .ThenBy(x => x.CurrentIndex)
            .Select(x => x.Row)
            .ToList();

        for (int target = 0; target < ordered.Count; target++)
        {
            int current = rows.IndexOf(ordered[target]);
            if (current >= 0 && current != target)
                rows.Move(current, target);
        }
    }

    public static List<AppMixerDefinition> PersistLayout<T>(
        IEnumerable<T> rows,
        IEnumerable<AppMixerDefinition> previousRows)
        where T : IAppMixerRow
    {
        var currentKeys = rows.Select(a => a.Key).ToHashSet();
        var current = rows.Select(a => new AppMixerDefinition { Key = a.Key, Pinned = a.IsPinned });
        var absent = previousRows.Where(a => !currentKeys.Contains(a.Key));
        return current.Concat(absent).ToList();
    }
}
