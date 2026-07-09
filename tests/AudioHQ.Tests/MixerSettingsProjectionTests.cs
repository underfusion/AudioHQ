using AudioHQ.App;
using AudioHQ.Core;

namespace AudioHQ.Tests;

public sealed class MixerSettingsProjectionTests
{
    [Fact]
    public void Apply_ReplacesOnlyRuntimeProjectedFields()
    {
        var settings = new MixerSettings
        {
            SourceDeviceId = "old-source",
            LatencyMs = 30,
            MasterName = "Keep me",
            CloseToTray = true,
            Channels = { new ChannelDefinition { DeviceId = "old" } },
            EqPresets = { new EqPreset { Name = "Old", Eq = new EqSettings() } },
            AppMixerApps = { new AppMixerDefinition { Key = "keep", Pinned = true } },
        };

        var channels = new[]
        {
            new ChannelDefinition
            {
                DeviceId = "new-device",
                Name = "New",
                Gain = 1.5,
                Active = true,
            },
        };
        var presets = new[]
        {
            new EqPreset
            {
                Name = "New preset",
                Eq = new EqSettings { Enabled = true, Bands = 3, GainsDb = new[] { 1.0, 2.0, 3.0 } },
            },
        };

        MixerSettingsProjection.Apply(settings, "new-source", 60, channels, presets);

        Assert.Equal("new-source", settings.SourceDeviceId);
        Assert.Equal(60, settings.LatencyMs);
        Assert.Equal("Keep me", settings.MasterName);
        Assert.True(settings.CloseToTray);
        Assert.Equal("new-device", settings.Channels.Single().DeviceId);
        Assert.Equal("New preset", settings.EqPresets.Single().Name);
        Assert.Equal("keep", settings.AppMixerApps.Single().Key);
    }
}
