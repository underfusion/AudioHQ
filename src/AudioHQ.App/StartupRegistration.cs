using System;
using System.Diagnostics;
using Microsoft.Win32;
using AudioHQ.Core;

namespace AudioHQ.App;

/// <summary>
/// "Run with Windows" toggle: writes/removes an HKCU Run entry pointing at this
/// exe. Per-user, no admin rights needed. All calls are best-effort and never throw.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AudioHQ";

    private static string? ExePath
    {
        get
        {
            var path = Process.GetCurrentProcess().MainModule?.FileName;
            return string.IsNullOrEmpty(path) ? null : path;
        }
    }

    /// <summary>True if the Run entry exists and points at the current exe.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch (Exception ex)
        {
            Log.Write($"StartupRegistration.IsEnabled FAILED: {ex.Message}");
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return;

            if (enabled)
            {
                var exe = ExePath;
                if (exe is null) { Log.Write("StartupRegistration.Set: no exe path, skipped"); return; }
                key.SetValue(ValueName, $"\"{exe}\"");
                Log.Write($"StartupRegistration: enabled -> {exe}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Write("StartupRegistration: disabled");
            }
        }
        catch (Exception ex)
        {
            Log.Write($"StartupRegistration.Set({enabled}) FAILED: {ex.Message}");
        }
    }
}
