using System;
using System.Collections.Generic;
using System.Linq;
using AudioHQ.Core;

namespace AudioHQ.App.ViewModels;

/// <summary>
/// The pure half of the per-app mixer refresh: which sessions deserve a row. Filters out
/// the host app itself and System Sounds, collapses the several WASAPI sessions an app can
/// expose into one row per stable app identity, and separates what should be updated in
/// place from what should be appended - so a refresh never disturbs the user's arrangement.
/// <see cref="AppMixerViewModel"/> applies the result to its observable row list.
/// </summary>
public static class AppSessionReconciler
{
    /// <summary>What a refresh should do to the row list.</summary>
    public sealed class Result<T>
    {
        public Result(IReadOnlyDictionary<string, T> current, IReadOnlyList<T> added)
        {
            Current = current;
            Added = added;
        }

        /// <summary>Sessions that should have a row, keyed by app identity. An existing row
        /// whose key is absent here has ended and should be dropped.</summary>
        public IReadOnlyDictionary<string, T> Current { get; }

        /// <summary>Sessions with no existing row, in the order they should be appended.</summary>
        public IReadOnlyList<T> Added { get; }
    }

    public static Result<T> Reconcile<T>(
        IReadOnlyList<T> sessions, IReadOnlySet<string> existingKeys, int selfProcessId)
        where T : IAppSessionInfo
    {
        var current = new Dictionary<string, T>();
        foreach (var session in sessions)
        {
            // Exclude AudioHQ itself - it always appears in the session list but controlling
            // its own volume here would be circular.
            if (session.ProcessId == selfProcessId) continue;

            // Filter out System Sounds - the user does not want it in the mixer.
            if (session.IsSystemSounds) continue;

            // Deduplicate by stable application identity. Browsers/Electron apps often expose
            // multiple WASAPI sessions or processes, but the panel should show one row per app.
            if (!current.ContainsKey(session.AppKey))
                current[session.AppKey] = session;
        }

        var added = current.Values
            .Where(s => !existingKeys.Contains(s.AppKey))
            .OrderBy(s => s.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new Result<T>(current, added);
    }
}
