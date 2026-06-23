using System;
using System.Windows.Media;
using AudioHQ.Core;

namespace AudioHQ.App.ViewModels;

/// <summary>
/// One row in the slide-out per-app mixer: an application's name, icon and its own Windows
/// volume + mute. Backed by a live <see cref="AppSession"/> that the mixer swaps in on each
/// refresh (<see cref="Update"/>), so writes always reach the current session object.
/// </summary>
public sealed class AppSessionViewModel : ViewModelBase
{
    private AppSession _session;
    private bool _isPinned;

    public AppSessionViewModel(AppSession session)
    {
        _session = session;
        Icon = AppIcon.ForSession(session);
    }

    /// <summary>Pinned rows are kept at the top of the list (toggled via the pin button).</summary>
    public bool IsPinned
    {
        get => _isPinned;
        set { if (_isPinned == value) return; _isPinned = value; OnPropertyChanged(); }
    }

    /// <summary>Stable key used by the mixer to match this row across refreshes.</summary>
    public string Key => _session.Key;

    /// <summary>True for the aggregate "System sounds" row (sorted to the top).</summary>
    public bool IsSystemSounds => _session.IsSystemSounds;

    public string Name => _session.FriendlyName;

    public ImageSource? Icon { get; private set; }

    /// <summary>App volume 0..1 (bound to the row's horizontal slider).</summary>
    public double Volume
    {
        get => _session.Volume;
        set
        {
            _session.Volume = (float)Math.Clamp(value, 0, 1);
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumePercent));
        }
    }

    public string VolumePercent => $"{Math.Round(_session.Volume * 100)}";

    public bool Muted
    {
        get => _session.Muted;
        set { _session.Muted = value; OnPropertyChanged(); }
    }

    /// <summary>Adopt the fresh session from a refresh, keeping this row instance so the
    /// slider does not flicker. Only re-reads the icon if the app behind the row changed.</summary>
    public void Update(AppSession session)
    {
        bool reicon = session.ExecutablePath != _session.ExecutablePath
                      || session.FriendlyName != _session.FriendlyName;
        _session = session;

        if (reicon)
        {
            Icon = AppIcon.ForSession(session);
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(Name));
        }
        OnPropertyChanged(nameof(Volume));
        OnPropertyChanged(nameof(VolumePercent));
        OnPropertyChanged(nameof(Muted));
    }
}
