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
    // Last known Windows friendly name, used only to recover when an HDMI/USB driver
    // recreates the same endpoint under a different volatile id.
    public string DeviceName { get; set; } = "";
    public string Name { get; set; } = "";
    public double Gain { get; set; } = 1.0;
    public bool Active { get; set; }
    public bool Focused { get; set; }
    // Per-channel graphic EQ; null on channels saved before EQ existed (treated as off).
    public EqSettings? Eq { get; set; }
}

/// <summary>Persisted per-app mixer row state: stable app identity, pin state and order.</summary>
public sealed class AppMixerDefinition
{
    public string Key { get; set; } = "";
    public bool Pinned { get; set; }
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

    // Per-app mixer layout. Entries are kept even while an app is not currently playing so
    // pinned/order state comes back when the app creates a new audio session later.
    public List<AppMixerDefinition> AppMixerApps { get; set; } = new();

    // The app mixer can live inside the main window or in its own panel beside it.
    // Expanded distinguishes the attached-open and attached-closed states.
    public bool AppMixerDetached { get; set; }
    public bool AppMixerExpanded { get; set; }

    // Last normal main-window position. Null means first launch and uses CenterScreen.
    public double? MainWindowLeft { get; set; }
    public double? MainWindowTop { get; set; }

    // App-wide saved EQ presets (shared across channels).
    public List<EqPreset> EqPresets { get; set; } = new();

    // Tray behaviour (see TrayController). RunWithWindows mirrors the HKCU Run key.
    public bool CloseToTray { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool LaunchMinimized { get; set; }
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

    /// <summary>
    /// Writes settings.json atomically: serialize to a temp file next to it, flush to disk, then
    /// swap it in. Autosave means this runs while the user is still working, so a forced kill or
    /// power loss mid-write must never leave a truncated settings.json behind - the old file
    /// survives intact until the replace succeeds. Safe to call repeatedly; never throws.
    /// </summary>
    public void Save()
    {
        var path = FilePath;
        var temp = path + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(this, JsonOptions);

            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                // Force the bytes to disk before the swap, or a crash right after Replace can
                // leave an entry pointing at unwritten data.
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
                File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temp, path);

            Log.Write($"MixerSettings: saved {Channels.Count} channels");
        }
        catch (Exception ex)
        {
            Log.Write($"MixerSettings.Save FAILED: {ex}");
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch (Exception cleanupEx)
            {
                Log.Write($"MixerSettings: temp cleanup failed: {cleanupEx.Message}");
            }
        }
    }
}
