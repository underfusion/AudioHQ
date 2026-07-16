using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AudioHQ.Core;
using NAudio.CoreAudioApi;

namespace AudioHQ.App.ViewModels;

/// <summary>
/// Keeps the source device list and each channel's device in step with what Windows currently
/// reports. Separate from the watchdog that decides WHEN to look (see
/// <see cref="MixerSourceRecoveryViewModel"/>): this only reconciles the lists.
///
/// Two flavours of reconcile:
/// - <see cref="Refresh"/> for the routine tick: add/remove endpoints by id.
/// - <see cref="HardRefresh"/> after sleep/resume, where the endpoint ids can survive but the
///   COM objects behind them are invalidated, so every entry is REPLACED with a fresh instance
///   rather than kept.
///
/// Device ownership: every MMDevice dropped from <c>Sources</c> is disposed here, since nothing
/// else holds it. See the lifetime rules in docs/ARCHITECTURE.md.
/// </summary>
internal sealed class SourceDeviceSync
{
    private readonly ObservableCollection<MMDevice> _sources;
    private readonly ObservableCollection<ChannelViewModel> _channels;
    private readonly HashSet<string> _unstartableSources;
    private readonly Func<MMDevice?> _getSelectedSource;
    private readonly Action<MMDevice> _setSelectedSource;
    private readonly Action _save;

    public SourceDeviceSync(
        ObservableCollection<MMDevice> sources,
        ObservableCollection<ChannelViewModel> channels,
        HashSet<string> unstartableSources,
        Func<MMDevice?> getSelectedSource,
        Action<MMDevice> setSelectedSource,
        Action save)
    {
        _sources = sources;
        _channels = channels;
        _unstartableSources = unstartableSources;
        _getSelectedSource = getSelectedSource;
        _setSelectedSource = setSelectedSource;
        _save = save;
    }

    /// <summary>
    /// Routine reconcile: drop endpoints Windows no longer lists, add ones that appeared.
    /// Entries that are still present keep their existing MMDevice.
    /// </summary>
    public void Refresh()
    {
        List<MMDevice> current;
        try { current = AudioDevices.GetActiveRenderDevices(); }
        catch (Exception ex) { Log.Write($"RefreshDevices failed: {ex.Message}"); return; }

        var currentIds = current.Select(d => d.ID).ToHashSet();

        for (int i = _sources.Count - 1; i >= 0; i--)
            if (!currentIds.Contains(_sources[i].ID))
            {
                var removed = _sources[i];
                Log.Write($"Device removed: '{removed.FriendlyName}'");
                _unstartableSources.Remove(removed.ID);
                _sources.RemoveAt(i);
                // An unreferenced MMDevice still holds its COM handles until disposed.
                // SelectedSource may still point here, which stays safe - a disposed MMDevice
                // keeps answering FriendlyName/ID.
                removed.Dispose();
            }

        var knownIds = _sources.Select(d => d.ID).ToHashSet();
        foreach (var device in current)
        {
            if (knownIds.Add(device.ID))
            {
                Log.Write($"Device appeared: '{device.FriendlyName}'");
                _sources.Add(device);
            }
            else
            {
                device.Dispose();
            }
        }

        RebindAndUpdateChannelPresence();
    }

    /// <summary>
    /// Post-resume reconcile: REPLACE every still-present endpoint with a freshly enumerated
    /// instance. After sleep/resume an id can enumerate fine while the old COM object behind it
    /// is invalidated, so keeping the old instance would fail at the next activation.
    /// </summary>
    public void HardRefresh()
    {
        List<MMDevice> current;
        try { current = AudioDevices.GetActiveRenderDevices(); }
        catch (Exception ex) { Log.Write($"HardRefreshSources failed: {ex.Message}"); return; }

        var fresh = current.ToDictionary(d => d.ID);
        var adopted = new HashSet<string>();

        for (int i = _sources.Count - 1; i >= 0; i--)
        {
            var old = _sources[i];
            if (fresh.TryGetValue(old.ID, out var replacement))
            {
                adopted.Add(old.ID);
                _sources[i] = replacement;
                if (ReferenceEquals(_getSelectedSource(), old)) _setSelectedSource(replacement);
                old.Dispose();
            }
            else
            {
                Log.Write($"Device gone after resume: '{old.FriendlyName}'");
                _unstartableSources.Remove(old.ID);
                _sources.RemoveAt(i);
                // Release it like the adopted branch above does: dropping the reference alone
                // holds the endpoint's COM handles until a GC that may never come.
                old.Dispose();
            }
        }

        foreach (var device in current)
            if (!adopted.Contains(device.ID))
            {
                Log.Write($"Device appeared after resume: '{device.FriendlyName}'");
                _sources.Add(device);
            }

        RebindAndUpdateChannelPresence();
    }

    /// <summary>
    /// Points each channel at the endpoint it should be using and updates its presence.
    /// A channel whose saved id has vanished may be adopted by a uniquely name-matched
    /// replacement (an HDMI/USB endpoint Windows recreated under a new volatile id) -
    /// <see cref="AudioEndpointIdentityResolver"/> owns that safety check.
    /// </summary>
    public void RebindAndUpdateChannelPresence()
    {
        var endpoints = _sources.Select(d => new AudioEndpointIdentity(d.ID, d.FriendlyName)).ToList();
        var activeIds = endpoints.Select(endpoint => endpoint.Id).ToHashSet();
        // Ids already claimed by a channel cannot be adopted by another one.
        var reservedIds = _channels
            .Where(channel => activeIds.Contains(channel.DeviceId))
            .Select(channel => channel.DeviceId)
            .ToHashSet();
        bool rebound = false;

        var selectedId = _getSelectedSource()?.ID;
        foreach (var channel in _channels)
        {
            if (!activeIds.Contains(channel.DeviceId))
            {
                string? replacementId = AudioEndpointIdentityResolver.Resolve(
                    channel.DeviceId, channel.DeviceName, endpoints, reservedIds);
                if (replacementId is not null)
                {
                    var replacement = _sources.First(device => device.ID == replacementId);
                    channel.RebindDevice(replacement.ID, replacement.FriendlyName);
                    reservedIds.Add(replacement.ID);
                    rebound = true;
                }
            }

            channel.IsSource = channel.DeviceId == selectedId;
            channel.SetPresent(activeIds.Contains(channel.DeviceId));
        }

        // A rebind changes the persisted endpoint id, so it has to reach settings.json.
        if (rebound) _save();
    }
}
