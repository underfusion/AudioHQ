using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using AudioHQ.Core;

namespace AudioHQ.App.ViewModels;

/// <summary>
/// The slide-out per-application mixer: the apps currently playing on the default output
/// device, each with its own volume and mute (the Windows volume mixer, in app). Sessions
/// are refreshed automatically while the panel is open, using the same reconcile path as
/// the initial panel-open refresh.
///
/// The row order is user-controlled: pinned rows are kept at the top, and rows can be
/// dragged to reorder within their pin group. <see cref="Refresh"/> preserves that order -
/// it only updates rows in place, drops ended ones, and appends newly-seen apps at the
/// bottom - so a refresh never disturbs the user's arrangement.
/// </summary>
public sealed class AppMixerViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(2);
    private readonly MixerSettings _settings;
    private readonly Action _saveSettings;
    private readonly DispatcherTimer _refreshTimer;
    private bool _isExpanded;
    private bool _isEmpty = true;

    public ObservableCollection<AppSessionViewModel> Apps { get; } = new();

    /// <summary>Toggles a row's pinned state (parameter = the <see cref="AppSessionViewModel"/>).</summary>
    public ICommand PinCommand { get; }

    public AppMixerViewModel(MixerSettings settings, Action saveSettings)
    {
        _settings = settings;
        _saveSettings = saveSettings;
        PinCommand = new RelayCommand(p => { if (p is AppSessionViewModel vm) TogglePin(vm); });
        _refreshTimer = new DispatcherTimer { Interval = AutoRefreshInterval };
        _refreshTimer.Tick += (_, _) => Refresh();
    }

    /// <summary>Whether the panel is open. Opening it starts automatic refresh.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            if (value)
            {
                Refresh();
                _refreshTimer.Start();
            }
            else
            {
                _refreshTimer.Stop();
            }
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
        var selfPid = Environment.ProcessId;
        sessions = sessions.Where(s => s.ProcessId != selfPid).ToList();

        // Filter out System Sounds - the user does not want it in the mixer.
        sessions = sessions.Where(s => !s.IsSystemSounds).ToList();

        // Deduplicate by stable application identity. Browsers/Electron apps often expose
        // multiple WASAPI sessions or processes, but the panel should show one row per app.
        var incoming = new Dictionary<string, AppSession>();
        foreach (var session in sessions)
        {
            if (!incoming.ContainsKey(session.AppKey))
                incoming[session.AppKey] = session;
        }

        var existing = Apps.ToDictionary(a => a.Key);
        foreach (var (key, session) in incoming)
            if (existing.TryGetValue(key, out var vm)) vm.Update(session);

        for (int i = Apps.Count - 1; i >= 0; i--)
            if (!incoming.ContainsKey(Apps[i].Key))
                Apps.RemoveAt(i);

        // Append newly-seen apps, restoring their pinned state if this app was seen before.
        // Saved ordering is applied after the append so returning pinned apps land back in place.
        var present = Apps.Select(a => a.Key).ToHashSet();
        var saved = _settings.AppMixerApps.ToDictionary(a => a.Key);
        var added = incoming.Values
            .Where(s => !present.Contains(s.AppKey))
            .OrderBy(s => s.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        foreach (var session in added)
        {
            saved.TryGetValue(session.AppKey, out var state);
            Apps.Add(new AppSessionViewModel(session, state?.Pinned == true));
        }

        ApplySavedOrder();

        IsEmpty = Apps.Count == 0;
    }

    // Flip the pinned state and slide the row to the pinned/unpinned boundary so pinned rows
    // always sit above unpinned ones. FluidMoveBehavior animates the move.
    private void TogglePin(AppSessionViewModel vm)
    {
        if (AppMixerLayout.TogglePin(Apps, vm))
            SaveLayout();
    }

    /// <summary>Drag-reorder: move <paramref name="source"/> to <paramref name="target"/>'s slot.
    /// Reordering is confined to the same pin group so the pinned block stays on top.</summary>
    public void MoveApp(AppSessionViewModel source, AppSessionViewModel target)
    {
        if (AppMixerLayout.MoveWithinPinGroup(Apps, source, target))
            SaveLayout();
    }

    private void ApplySavedOrder()
    {
        AppMixerLayout.ApplySavedOrder(Apps, _settings.AppMixerApps);
    }

    private void SaveLayout()
    {
        _settings.AppMixerApps = AppMixerLayout.PersistLayout(Apps, _settings.AppMixerApps);
        _saveSettings();
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
    }
}
