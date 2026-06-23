using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using AudioHQ.Core;

namespace AudioHQ.App.ViewModels;

/// <summary>
/// The slide-out per-application mixer: the apps currently playing on the default output
/// device, each with its own volume and mute (the Windows volume mixer, in app). Sessions
/// are read on demand - when the panel opens, when the window is activated, and from the
/// manual refresh button - never polled continuously.
///
/// The row order is user-controlled: pinned rows are kept at the top, and rows can be
/// dragged to reorder within their pin group. <see cref="Refresh"/> preserves that order -
/// it only updates rows in place, drops ended ones, and appends newly-seen apps at the
/// bottom - so a refresh never disturbs the user's arrangement.
/// </summary>
public sealed class AppMixerViewModel : ViewModelBase
{
    private bool _isExpanded;
    private bool _isEmpty = true;

    public ObservableCollection<AppSessionViewModel> Apps { get; } = new();

    /// <summary>Re-reads the session list (the refresh button and the code-behind both use it).</summary>
    public ICommand RefreshCommand { get; }

    /// <summary>Toggles a row's pinned state (parameter = the <see cref="AppSessionViewModel"/>).</summary>
    public ICommand PinCommand { get; }

    public AppMixerViewModel()
    {
        RefreshCommand = new RelayCommand(_ => Refresh());
        PinCommand = new RelayCommand(p => { if (p is AppSessionViewModel vm) TogglePin(vm); });
    }

    /// <summary>Whether the panel is open. Opening it triggers a refresh.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            if (value) Refresh();
        }
    }

    /// <summary>True when no app is currently playing (drives the "No apps playing" hint).</summary>
    public bool IsEmpty
    {
        get => _isEmpty;
        private set { if (_isEmpty == value) return; _isEmpty = value; OnPropertyChanged(); }
    }

    /// <summary>Reconcile the row list against a fresh session snapshot, preserving the user's
    /// pinned/dragged order: update in place by key, drop ended sessions, append new ones.</summary>
    public void Refresh()
    {
        List<AppSession> sessions;
        try { sessions = AppSessions.ForDefaultRender(); }
        catch (Exception ex) { Log.Write($"AppMixer.Refresh failed: {ex.Message}"); return; }

        // Exclude AudioHQ itself - it always appears in the session list but controlling
        // its own volume here would be circular.
        var selfPid = Process.GetCurrentProcess().Id;
        sessions = sessions.Where(s => s.ProcessId != selfPid).ToList();

        // Filter out System Sounds — the user does not want it in the mixer.
        sessions = sessions.Where(s => !s.IsSystemSounds).ToList();

        // Deduplicate: keep only the first session per ProcessId. The same process may
        // register multiple WASAPI sessions; we show it once.
        // Also keep one row per stable key in case two different processes somehow share a key.
        var seenPids = new HashSet<uint>();
        var incoming = new Dictionary<string, AppSession>();
        foreach (var session in sessions)
        {
            if (seenPids.Contains(session.ProcessId)) continue;
            seenPids.Add(session.ProcessId);
            incoming[session.Key] = session;
        }

        var existing = Apps.ToDictionary(a => a.Key);
        foreach (var (key, session) in incoming)
            if (existing.TryGetValue(key, out var vm)) vm.Update(session);

        for (int i = Apps.Count - 1; i >= 0; i--)
            if (!incoming.ContainsKey(Apps[i].Key))
                Apps.RemoveAt(i);

        // Append newly-seen apps (always unpinned) at the bottom in a sensible default order,
        // so the pinned block stays on top and existing rows never move under the user.
        var present = Apps.Select(a => a.Key).ToHashSet();
        var added = incoming.Values
            .Where(s => !present.Contains(s.Key))
            .OrderBy(s => s.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        foreach (var session in added) Apps.Add(new AppSessionViewModel(session));

        IsEmpty = Apps.Count == 0;
    }

    // Flip the pinned state and slide the row to the pinned/unpinned boundary so pinned rows
    // always sit above unpinned ones. FluidMoveBehavior animates the move.
    private void TogglePin(AppSessionViewModel vm)
    {
        int current = Apps.IndexOf(vm);
        if (current < 0) return;

        vm.IsPinned = !vm.IsPinned;

        // Pinning  -> land just after the other pinned rows (bottom of the pinned block).
        // Unpinning -> land just after all pinned rows (top of the unpinned block).
        int target = Apps.Count(a => a.IsPinned && !ReferenceEquals(a, vm));
        target = Math.Clamp(target, 0, Apps.Count - 1);
        if (current != target) Apps.Move(current, target);
    }

    /// <summary>Drag-reorder: move <paramref name="source"/> to <paramref name="target"/>'s slot.
    /// Reordering is confined to the same pin group so the pinned block stays on top.</summary>
    public void MoveApp(AppSessionViewModel source, AppSessionViewModel target)
    {
        if (source is null || target is null || ReferenceEquals(source, target)) return;
        if (source.IsPinned != target.IsPinned) return;

        int from = Apps.IndexOf(source), to = Apps.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;
        Apps.Move(from, to);
    }
}
