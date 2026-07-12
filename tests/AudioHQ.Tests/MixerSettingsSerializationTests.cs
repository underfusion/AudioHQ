using System.Text.Json;
using AudioHQ.App;
using AudioHQ.Core;

namespace AudioHQ.Tests;

public sealed class MixerSettingsSerializationTests
{
    [Fact]
    public void MixerSettings_RoundTripsPersistedState()
    {
        var settings = new MixerSettings
        {
            SourceDeviceId = "source-1",
            MasterName = "Desk",
            LatencyMs = 60,
            CloseToTray = true,
            MinimizeToTray = true,
            RunWithWindows = true,
            AppMixerDetached = true,
            MainWindowLeft = 240,
            MainWindowTop = 120,
            Channels =
            {
                new ChannelDefinition
                {
                    DeviceId = "device-1",
                    Name = "Headphones",
                    Gain = 1.25,
                    Active = true,
                    Focused = true,
                    Eq = new EqSettings
                    {
                        Enabled = true,
                        Bands = 3,
                        GainsDb = new[] { 1.0, 0.0, -3.0 },
                        QValues = new[] { 0.7, 0.8, 0.9 },
                        LowPassEnabled = true,
                        LowPassHz = 140.0,
                        LowPassSlope = 2,
                    },
                },
            },
            AppMixerApps =
            {
                new AppMixerDefinition { Key = "app-a", Pinned = true },
                new AppMixerDefinition { Key = "app-b", Pinned = false },
            },
            EqPresets =
            {
                new EqPreset
                {
                    Name = "Bass",
                    Eq = new EqSettings
                    {
                        Enabled = true,
                        Bands = 3,
                        GainsDb = new[] { 5.0, -2.0, -6.0 },
                    },
                },
            },
        };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<MixerSettings>(json)!;

        Assert.Equal("source-1", restored.SourceDeviceId);
        Assert.Equal("Desk", restored.MasterName);
        Assert.Equal(60, restored.LatencyMs);
        Assert.True(restored.CloseToTray);
        Assert.True(restored.MinimizeToTray);
        Assert.True(restored.RunWithWindows);
        Assert.True(restored.AppMixerDetached);
        Assert.Equal(240, restored.MainWindowLeft);
        Assert.Equal(120, restored.MainWindowTop);
        Assert.Single(restored.Channels);
        Assert.Equal("Headphones", restored.Channels[0].Name);
        Assert.True(restored.Channels[0].Active);
        Assert.True(restored.Channels[0].Focused);
        Assert.Equal(new[] { 1.0, 0.0, -3.0 }, restored.Channels[0].Eq!.GainsDb);
        Assert.Equal(2, restored.AppMixerApps.Count);
        Assert.True(restored.AppMixerApps[0].Pinned);
        Assert.Single(restored.EqPresets);
        Assert.Equal("Bass", restored.EqPresets[0].Name);
    }
}
