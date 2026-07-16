using System;
using System.IO;
using AudioHQ.App;

namespace AudioHQ.Tests;

/// <summary>
/// Covers the real settings.json write path. Autosave writes while the user is still working,
/// so Save must be atomic (temp file + swap) and must leave a file Load can actually read -
/// on first run (no existing file) and on every later overwrite.
/// </summary>
public sealed class MixerSettingsAtomicSaveTests : IDisposable
{
    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "settings.json");
    private readonly string _tempPath = Path.Combine(AppContext.BaseDirectory, "settings.json.tmp");
    private readonly string? _original;

    public MixerSettingsAtomicSaveTests()
    {
        if (File.Exists(_path)) _original = File.ReadAllText(_path);
        File.Delete(_path);
    }

    public void Dispose()
    {
        if (_original is not null) File.WriteAllText(_path, _original);
        else File.Delete(_path);
        File.Delete(_tempPath);
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
}
