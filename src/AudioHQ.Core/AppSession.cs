using System;
using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace AudioHQ.Core;

/// <summary>
/// One Windows audio session - a single application's playback stream on a render
/// device, exactly as listed by the Windows volume mixer. Wraps the WASAPI session so
/// the UI can read the app's name/icon and drive its own per-app volume and mute.
///
/// Sessions are volatile: an app can stop and its session expire at any moment, so every
/// COM access here is guarded and never throws - a dead session reports its last values
/// and silently ignores writes.
/// </summary>
public sealed class AppSession
{
    // Rooting the source device keeps the session's COM objects alive for as long as this
    // wrapper lives, even after the enumerator that produced it has gone out of scope.
    private readonly MMDevice _device;
    private readonly SimpleAudioVolume? _volume;

    internal AppSession(MMDevice device, AudioSessionControl control)
    {
        _device = device;
        _volume = TryGet(() => control.SimpleAudioVolume);

        ProcessId = TryGet(() => control.GetProcessID);
        IsSystemSounds = TryGet(() => control.IsSystemSoundsSession);
        IconPath = TryGet(() => control.IconPath) ?? "";
        Key = ResolveKey(control, ProcessId);

        var (exe, friendly) = ResolveProcess(ProcessId, TryGet(() => control.DisplayName) ?? "");
        ExecutablePath = exe;
        FriendlyName = IsSystemSounds ? "System sounds" : friendly;
        AppKey = ResolveAppKey(IsSystemSounds, ExecutablePath, FriendlyName, IconPath);
    }

    /// <summary>OS process id behind the session (0 when unknown).</summary>
    public uint ProcessId { get; }

    /// <summary>Best human label: app file description, else process name, else declared name.</summary>
    public string FriendlyName { get; }

    /// <summary>Full path to the app's exe (for icon extraction in the UI); empty when unavailable.</summary>
    public string ExecutablePath { get; }

    /// <summary>The session's own declared icon path (rarely set); a fallback for the UI.</summary>
    public string IconPath { get; }

    /// <summary>True for the aggregate "System sounds" session.</summary>
    public bool IsSystemSounds { get; }

    /// <summary>Stable identity used to match a session across refreshes.</summary>
    public string Key { get; }

    /// <summary>Stable application identity used to group multiple sessions/processes into one UI row.</summary>
    public string AppKey { get; }

    /// <summary>Per-app volume scalar 0..1 (the app's own slider in the Windows mixer).</summary>
    public float Volume
    {
        get => _volume is null ? 1f : TryGet(() => _volume.Volume);
        set { if (_volume is not null) TryRun(() => _volume.Volume = Math.Clamp(value, 0f, 1f)); }
    }

    /// <summary>Per-app mute state.</summary>
    public bool Muted
    {
        get => _volume is not null && TryGet(() => _volume.Mute);
        set { if (_volume is not null) TryRun(() => _volume.Mute = value); }
    }

    private static string ResolveKey(AudioSessionControl control, uint pid)
    {
        var id = TryGet(() => control.GetSessionInstanceIdentifier);
        return string.IsNullOrEmpty(id) ? $"pid:{pid}" : id!;
    }

    private static string ResolveAppKey(bool isSystemSounds, string exe, string friendly, string iconPath)
    {
        if (isSystemSounds) return "system-sounds";
        if (!string.IsNullOrWhiteSpace(exe))
            return "exe:" + exe.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(iconPath))
            return "icon:" + iconPath.Trim().ToUpperInvariant();
        return "name:" + friendly.Trim().ToUpperInvariant();
    }

    // Friendly name priority: exe FileDescription (what the Windows mixer shows) -> process
    // name -> the session's declared DisplayName -> "Unknown".
    private static (string exe, string friendly) ResolveProcess(uint pid, string displayName)
    {
        if (pid == 0) return ("", Clean(displayName) ?? "Unknown");
        try
        {
            using var process = Process.GetProcessById((int)pid);

            string exe = "";
            try { exe = process.MainModule?.FileName ?? ""; }
            catch (Exception ex) { Log.Write($"AppSession: exe path for pid {pid} unavailable: {ex.Message}"); }

            string? description = null;
            if (exe.Length > 0)
            {
                try { description = Clean(FileVersionInfo.GetVersionInfo(exe).FileDescription); }
                catch (Exception ex) { Log.Write($"AppSession: version info for '{exe}' failed: {ex.Message}"); }
            }

            string friendly = description ?? Clean(displayName) ?? Clean(process.ProcessName) ?? "Unknown";
            return (exe, friendly);
        }
        catch (Exception ex)
        {
            Log.Write($"AppSession: process {pid} lookup failed: {ex.Message}");
            return ("", Clean(displayName) ?? "Unknown");
        }
    }

    // Drop resource-string DisplayNames (e.g. "@%SystemRoot%\System32\...,-1") and blanks.
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("@")) return null;
        return value.Trim();
    }

    private static T TryGet<T>(Func<T> get)
    {
        try { return get(); }
        catch { return default!; }
    }

    private static void TryRun(Action run)
    {
        try { run(); }
        catch (Exception ex) { Log.Write($"AppSession: write failed: {ex.Message}"); }
    }
}
