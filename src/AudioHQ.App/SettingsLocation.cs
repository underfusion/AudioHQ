using System;
using System.IO;

namespace AudioHQ.App;

/// <summary>
/// Resolves where settings.json lives.
///
/// The file is user data, not a build artifact, so it belongs in %APPDATA%\AudioHQ. It used to
/// sit next to the exe, where anything that moved the output folder took the user's setup with
/// it: retargeting net7.0 -> net10.0 changed the folder name and the app silently came up with
/// defaults, and a clean of bin/ deleted the settings outright.
///
/// <see cref="LegacyFilePath"/> still points beside the exe so an existing file is picked up
/// once and migrated across (see <c>MixerSettings.Load</c>).
///
/// Both directories are settable so tests can redirect them to a scratch folder instead of
/// writing to the real user profile.
/// </summary>
public static class SettingsLocation
{
    public const string FileName = "settings.json";

    private static string? _directory;
    private static string? _legacyDirectory;

    /// <summary>Directory holding the live settings.json. %APPDATA%\AudioHQ unless redirected.</summary>
    public static string Directory
    {
        get => _directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioHQ");
        set => _directory = value;
    }

    /// <summary>Pre-0.5.35 location: next to the exe. Read for migration only, never written.</summary>
    public static string LegacyDirectory
    {
        get => _legacyDirectory ??= AppContext.BaseDirectory;
        set => _legacyDirectory = value;
    }

    public static string FilePath => Path.Combine(Directory, FileName);

    public static string LegacyFilePath => Path.Combine(LegacyDirectory, FileName);
}
