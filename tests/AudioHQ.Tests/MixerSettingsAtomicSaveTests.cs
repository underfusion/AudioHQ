using System;
using System.IO;
using AudioHQ.App;

namespace AudioHQ.Tests;

/// <summary>
/// Covers the real settings.json write path. Autosave writes while the user is still working,
/// so Save must be atomic (temp file + swap) and must leave a file Load can actually read -
/// on first run (no existing file) and on every later overwrite. Also covers the one-time
/// migration of a pre-0.5.35 file sitting next to the exe.
///
/// SettingsLocation is redirected to a scratch folder for the duration: these tests must never
/// touch the real %APPDATA%\AudioHQ settings.
/// </summary>
public sealed class MixerSettingsAtomicSaveTests : IDisposable
{
    private readonly string _dir;
    private readonly string _legacyDir;
    private readonly string _path;
    private readonly string _tempPath;
    private readonly string _originalDir;
    private readonly string _originalLegacyDir;

    public MixerSettingsAtomicSaveTests()
    {
        var scratch = Path.Combine(AppContext.BaseDirectory, "settings-tests", Guid.NewGuid().ToString("N"));
        _dir = Path.Combine(scratch, "live");
        _legacyDir = Path.Combine(scratch, "legacy");
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(_legacyDir);

        _originalDir = SettingsLocation.Directory;
        _originalLegacyDir = SettingsLocation.LegacyDirectory;
        SettingsLocation.Directory = _dir;
        SettingsLocation.LegacyDirectory = _legacyDir;

        _path = Path.Combine(_dir, SettingsLocation.FileName);
        _tempPath = _path + ".tmp";
    }

    public void Dispose()
    {
        SettingsLocation.Directory = _originalDir;
        SettingsLocation.LegacyDirectory = _originalLegacyDir;
        try
        {
            Directory.Delete(Path.GetDirectoryName(_dir)!, recursive: true);
        }
        catch (IOException)
        {
            // A leftover scratch folder is not worth failing a green test over.
        }
    }

    [Fact]
    public void Save_WritesReadableFile_WhenNoFileExists()
    {
        // First run takes the Move branch: no settings.json to replace.
        new MixerSettings { SourceDeviceId = "first-run", LatencyMs = 40 }.Save();

        Assert.True(File.Exists(_path));
        var loaded = MixerSettings.Load();
        Assert.Equal("first-run", loaded.SourceDeviceId);
        Assert.Equal(40, loaded.LatencyMs);
    }

    [Fact]
    public void Save_CreatesSettingsDirectory_WhenItDoesNotExistYet()
    {
        // %APPDATA%\AudioHQ is absent before the very first save.
        Directory.Delete(_dir, recursive: true);

        new MixerSettings { SourceDeviceId = "fresh-profile" }.Save();

        Assert.True(File.Exists(_path));
        Assert.Equal("fresh-profile", MixerSettings.Load().SourceDeviceId);
    }

    [Fact]
    public void Save_ReplacesExistingFile_AndKeepsItReadable()
    {
        new MixerSettings { SourceDeviceId = "old", LatencyMs = 10 }.Save();
        new MixerSettings { SourceDeviceId = "new", LatencyMs = 90 }.Save();

        var loaded = MixerSettings.Load();
        Assert.Equal("new", loaded.SourceDeviceId);
        Assert.Equal(90, loaded.LatencyMs);
    }

    [Fact]
    public void Save_RepeatedCalls_LeaveNoTempFileBehind()
    {
        // A leftover .tmp would mean a swap that never completed.
        for (int i = 0; i < 5; i++)
            new MixerSettings { SourceDeviceId = $"run-{i}" }.Save();

        Assert.False(File.Exists(_tempPath));
        Assert.Equal("run-4", MixerSettings.Load().SourceDeviceId);
    }

    [Fact]
    public void Save_DoesNotTruncateExistingFile_WhenTargetIsLocked()
    {
        new MixerSettings { SourceDeviceId = "survivor", LatencyMs = 55 }.Save();

        // Hold the target open exclusively so the swap cannot succeed - the closest we can get
        // to "killed mid-write" without killing the process. The previous contents must survive
        // rather than end up truncated, and Save must not throw.
        using (File.Open(_path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            new MixerSettings { SourceDeviceId = "doomed", LatencyMs = 99 }.Save();
        }

        var loaded = MixerSettings.Load();
        Assert.Equal("survivor", loaded.SourceDeviceId);
        Assert.Equal(55, loaded.LatencyMs);
    }

    [Fact]
    public void Load_MigratesLegacyFileFromBesideTheExe_WhenNoLiveFileExists()
    {
        WriteLegacy("legacy-source", latencyMs: 75);

        var loaded = MixerSettings.Load();

        Assert.Equal("legacy-source", loaded.SourceDeviceId);
        Assert.Equal(75, loaded.LatencyMs);
        // The copy must land in the new location so later saves have one home...
        Assert.True(File.Exists(_path));
        // ...and the original stays put rather than being destroyed.
        Assert.True(File.Exists(Path.Combine(_legacyDir, SettingsLocation.FileName)));
    }

    [Fact]
    public void Load_PrefersLiveFile_OverStaleLegacyFile()
    {
        WriteLegacy("legacy-source", latencyMs: 75);
        new MixerSettings { SourceDeviceId = "live-source", LatencyMs = 20 }.Save();

        var loaded = MixerSettings.Load();

        Assert.Equal("live-source", loaded.SourceDeviceId);
        Assert.Equal(20, loaded.LatencyMs);
    }

    [Fact]
    public void Load_MigratesOnlyOnce_SoLaterEditsAreNotOverwritten()
    {
        WriteLegacy("legacy-source", latencyMs: 75);
        MixerSettings.Load();

        // The user changes the source after migrating; the stale legacy file must not win.
        new MixerSettings { SourceDeviceId = "changed-after-migration", LatencyMs = 15 }.Save();

        var loaded = MixerSettings.Load();
        Assert.Equal("changed-after-migration", loaded.SourceDeviceId);
        Assert.Equal(15, loaded.LatencyMs);
    }

    private void WriteLegacy(string sourceDeviceId, int latencyMs)
    {
        SettingsLocation.Directory = _legacyDir;
        new MixerSettings { SourceDeviceId = sourceDeviceId, LatencyMs = latencyMs }.Save();
        SettingsLocation.Directory = _dir;
    }
}
