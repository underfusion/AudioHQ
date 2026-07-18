using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioHQ.App.ViewModels;

/// <summary>
/// The pure rules for which source the mixer should capture.
/// <see cref="MixerSourceRecoveryViewModel"/> owns the live devices and decides WHEN to
/// re-evaluate; these decide WHAT the outcome is, so the preferred-versus-default fallback
/// can be tested without audio endpoints.
/// </summary>
public static class SourceSelectionRules
{
    /// <summary>
    /// The source to capture after a start, restart or recovery: the user's preferred device
    /// when present, else the system default, else the first available; null when there is
    /// nothing to capture from. <paramref name="defaultId"/> is a callback because reading
    /// the system default touches COM - it is only consulted when the preferred device is
    /// absent.
    /// </summary>
    public static string? Resolve(IReadOnlyList<string> sourceIds, string? preferredId, Func<string?> defaultId)
    {
        if (preferredId is not null && sourceIds.Contains(preferredId)) return preferredId;
        var fallback = defaultId();
        if (fallback is not null && sourceIds.Contains(fallback)) return fallback;
        return sourceIds.Count > 0 ? sourceIds[0] : null;
    }

    /// <summary>
    /// Whether the watchdog should switch capture back to the user's preferred device: only
    /// when one is saved, it is not already the captured source, it has not proven
    /// unstartable, and it is currently present.
    /// </summary>
    public static bool ShouldSwitchToPreferred(
        string? preferredId,
        string? capturedId,
        IReadOnlyCollection<string> unstartableIds,
        IReadOnlyList<string> presentIds)
    {
        return preferredId is not null
               && preferredId != capturedId
               && !unstartableIds.Contains(preferredId)
               && presentIds.Contains(preferredId);
    }

    /// <summary>
    /// Whether the watchdog should move capture onto the current system default device: only
    /// when the user has no saved preference (the mixer then tracks whatever Windows plays
    /// to), the default is known and present, it is not already the captured source, and it
    /// has not proven unstartable. Without this, a brief unplug of the default (a USB replug
    /// flap) leaves capture stranded on the emergency fallback forever.
    /// </summary>
    public static bool ShouldFollowDefault(
        string? preferredId,
        string? defaultId,
        string? capturedId,
        IReadOnlyCollection<string> unstartableIds,
        IReadOnlyList<string> presentIds)
    {
        return preferredId is null
               && defaultId is not null
               && defaultId != capturedId
               && !unstartableIds.Contains(defaultId)
               && presentIds.Contains(defaultId);
    }
}
