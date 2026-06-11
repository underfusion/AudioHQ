using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AudioHQ.Core;

namespace AudioHQ.App;

/// <summary>One persisted output channel: which device, the user's label, its gain.</summary>
public sealed class ChannelDefinition
{
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public double Gain { get; set; } = 1.0;
    public bool Active { get; set; }
}

/// <summary>
/// User-curated mixer state persisted to settings.json next to the exe:
/// the chosen source, latency, and the ordered list of named channels.
/// Survives restarts; missing/corrupt file falls back to defaults (first run).
/// </summary>
public sealed class MixerSettings
{
    public string? SourceDeviceId { get; set; }
    // Optional user label for the master strip; null/empty falls back to the source device name.
    public string? MasterName { get; set; }
    public int LatencyMs { get; set; } = 30;
    public List<ChannelDefinition> Channels { get; set; } = new();

    // Tray behaviour (see TrayController). RunWithWindows mirrors the HKCU Run key.
    public bool CloseToTray { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool RunWithWindows { get; set; }

    private static string FilePath =>
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static MixerSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                Log.Write("MixerSettings: no settings.json, using defaults (first run)");
                return new MixerSettings();
            }

            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<MixerSettings>(json);
            if (settings is null)
            {
                Log.Write("MixerSettings: settings.json deserialized to null, using defaults");
                return new MixerSettings();
            }

            Log.Write($"MixerSettings: loaded {settings.Channels.Count} channels, source='{settings.SourceDeviceId}'");
            return settings;
        }
        catch (Exception ex)
        {
            Log.Write($"MixerSettings.Load FAILED, using defaults: {ex}");
            return new MixerSettings();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(FilePath, json);
            Log.Write($"MixerSettings: saved {Channels.Count} channels");
        }
        catch (Exception ex)
        {
            Log.Write($"MixerSettings.Save FAILED: {ex}");
        }
    }
}
