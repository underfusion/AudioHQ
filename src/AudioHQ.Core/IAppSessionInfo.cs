namespace AudioHQ.Core;

/// <summary>
/// The identity slice of an app audio session that list reconciliation needs.
/// <see cref="AppSession"/> wraps a live COM session and cannot be constructed in tests;
/// this seam lets the per-app mixer's reconcile rules run against plain fakes.
/// </summary>
public interface IAppSessionInfo
{
    /// <summary>OS process id behind the session (0 when unknown).</summary>
    uint ProcessId { get; }

    /// <summary>True for the aggregate "System sounds" session.</summary>
    bool IsSystemSounds { get; }

    /// <summary>Stable application identity used to group multiple sessions into one UI row.</summary>
    string AppKey { get; }

    /// <summary>Best human label for the app (drives the append order of new rows).</summary>
    string FriendlyName { get; }
}
