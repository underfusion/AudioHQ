using System.Linq;
using AudioHQ.App.ViewModels;
using AudioHQ.Core;

namespace AudioHQ.Tests;

/// <summary>
/// The per-app mixer's reconcile rules against plain fakes - no live WASAPI sessions.
/// These decide which apps get a row, so the filters (self, System Sounds), the one-row-
/// per-app dedup and the update/drop/append split all have to hold for every refresh.
/// </summary>
public sealed class AppSessionReconcilerTests
{
    private const int SelfPid = 42;

    private sealed record FakeSession(
        string AppKey,
        string FriendlyName,
        uint ProcessId = 100,
        bool IsSystemSounds = false) : IAppSessionInfo;

    private static AppSessionReconciler.Result<FakeSession> Reconcile(
        FakeSession[] sessions, params string[] existingKeys) =>
        AppSessionReconciler.Reconcile(sessions, existingKeys.ToHashSet(), SelfPid);

    [Fact]
    public void Reconcile_ExcludesTheHostAppItself()
    {
        // AudioHQ always appears in the session list; a row for it would be circular.
        var result = Reconcile(new[]
        {
            new FakeSession("exe:SELF", "AudioHQ", ProcessId: SelfPid),
            new FakeSession("exe:GAME", "Game"),
        });

        var only = Assert.Single(result.Current);
        Assert.Equal("exe:GAME", only.Key);
    }

    [Fact]
    public void Reconcile_ExcludesSystemSounds()
    {
        var result = Reconcile(new[]
        {
            new FakeSession("system-sounds", "System sounds", IsSystemSounds: true),
            new FakeSession("exe:GAME", "Game"),
        });

        var only = Assert.Single(result.Current);
        Assert.Equal("exe:GAME", only.Key);
    }

    [Fact]
    public void Reconcile_CollapsesAnAppsManySessionsIntoOneRow_FirstWins()
    {
        // A browser exposes one session per tab/process; the panel shows one row per app.
        var first = new FakeSession("exe:BROWSER", "Browser", ProcessId: 200);
        var second = new FakeSession("exe:BROWSER", "Browser", ProcessId: 201);

        var result = Reconcile(new[] { first, second });

        Assert.Same(first, Assert.Single(result.Current).Value);
    }

    [Fact]
    public void Reconcile_SplitsSessionsIntoKeptDroppedAndAdded()
    {
        // Rows exist for "old" and "kept"; the snapshot has "kept" and "new". So: "kept"
        // survives (update in place), "old" is absent from Current (drop), "new" is appended.
        var kept = new FakeSession("exe:KEPT", "Kept");
        var added = new FakeSession("exe:NEW", "New");

        var result = Reconcile(new[] { kept, added }, "exe:OLD", "exe:KEPT");

        Assert.True(result.Current.ContainsKey("exe:KEPT"));
        Assert.False(result.Current.ContainsKey("exe:OLD"));
        Assert.Same(added, Assert.Single(result.Added));
    }

    [Fact]
    public void Reconcile_AppendsNewAppsAlphabetically_CaseInsensitively()
    {
        // New rows land at the bottom in a predictable order; existing rows keep the
        // user's arrangement (they never appear in Added).
        var result = Reconcile(new[]
        {
            new FakeSession("exe:Z", "zulu"),
            new FakeSession("exe:A", "Alpha"),
            new FakeSession("exe:M", "Mike"),
        });

        Assert.Equal(new[] { "Alpha", "Mike", "zulu" }, result.Added.Select(s => s.FriendlyName));
    }

    [Fact]
    public void Reconcile_NeverReAddsAnExistingRow()
    {
        var result = Reconcile(new[] { new FakeSession("exe:GAME", "Game") }, "exe:GAME");

        Assert.Empty(result.Added);
        Assert.True(result.Current.ContainsKey("exe:GAME"));
    }
}
