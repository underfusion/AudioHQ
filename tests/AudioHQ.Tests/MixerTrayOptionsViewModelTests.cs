using AudioHQ.App;
using AudioHQ.App.ViewModels;

namespace AudioHQ.Tests;

public sealed class MixerTrayOptionsViewModelTests
{
    [Fact]
    public void Constructor_SyncsSavedStartupPreference()
    {
        var settings = new MixerSettings { RunWithWindows = true };
        bool? registered = null;

        _ = new MixerTrayOptionsViewModel(settings, () => { }, value => registered = value);

        Assert.True(registered);
    }

    [Fact]
    public void Setters_UpdateSettingsAndSaveOnlyWhenChanged()
    {
        var settings = new MixerSettings();
        var registered = new List<bool>();
        int saves = 0;
        var options = new MixerTrayOptionsViewModel(settings, () => saves++, registered.Add);
        saves = 0;
        registered.Clear();

        options.CloseToTray = true;
        options.MinimizeToTray = true;
        options.LaunchMinimized = true;
        options.RunWithWindows = true;
        options.RunWithWindows = true;

        Assert.True(settings.CloseToTray);
        Assert.True(settings.MinimizeToTray);
        Assert.True(settings.LaunchMinimized);
        Assert.True(settings.RunWithWindows);
        Assert.Equal(4, saves);
        Assert.Equal(new[] { true }, registered);
    }
}
