using System;

namespace AudioHQ.App.ViewModels;

/// <summary>Tray and startup preferences backed by <see cref="MixerSettings"/>.</summary>
public sealed class MixerTrayOptionsViewModel : ViewModelBase
{
    private readonly MixerSettings _settings;
    private readonly Action _save;
    private readonly Action<bool> _setRunWithWindows;

    public MixerTrayOptionsViewModel(
        MixerSettings settings,
        Action save,
        Action<bool>? setRunWithWindows = null)
    {
        _settings = settings;
        _save = save;
        _setRunWithWindows = setRunWithWindows ?? StartupRegistration.Set;

        // Keep the HKCU Run entry in step with the saved preference (refreshes the
        // exe path if the app was moved since it was first enabled).
        _setRunWithWindows(_settings.RunWithWindows);
    }

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set
        {
            if (_settings.CloseToTray == value) return;
            _settings.CloseToTray = value;
            OnPropertyChanged();
            _save();
        }
    }

    public bool MinimizeToTray
    {
        get => _settings.MinimizeToTray;
        set
        {
            if (_settings.MinimizeToTray == value) return;
            _settings.MinimizeToTray = value;
            OnPropertyChanged();
            _save();
        }
    }

    public bool RunWithWindows
    {
        get => _settings.RunWithWindows;
        set
        {
            if (_settings.RunWithWindows == value) return;
            _settings.RunWithWindows = value;
            _setRunWithWindows(value);
            OnPropertyChanged();
            _save();
        }
    }

    public bool LaunchMinimized
    {
        get => _settings.LaunchMinimized;
        set
        {
            if (_settings.LaunchMinimized == value) return;
            _settings.LaunchMinimized = value;
            OnPropertyChanged();
            _save();
        }
    }
}
