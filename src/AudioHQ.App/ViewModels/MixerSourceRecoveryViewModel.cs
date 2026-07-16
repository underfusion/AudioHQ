using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using AudioHQ.Core;
using NAudio.CoreAudioApi;

namespace AudioHQ.App.ViewModels;

/// <summary>Owns source selection, device-list refresh, capture restart and resume recovery.</summary>
public sealed class MixerSourceRecoveryViewModel
{
    private readonly MirrorEngine _engine;
    private readonly ObservableCollection<MMDevice> _sources;
    private readonly ObservableCollection<ChannelViewModel> _channels;
    private readonly Action<string, bool> _setStatus;
    private readonly Action _clearStatus;
    private readonly Action _save;
    private readonly Action _sourceChanged;
    private readonly HashSet<string> _unstartableSources = new();
    private readonly SourceDeviceSync _deviceSync;
    private bool _recovering;
    private int _resumeTicksLeft;
    private DateTime _lastTickUtc = DateTime.UtcNow;

    public MixerSourceRecoveryViewModel(
        MirrorEngine engine,
        ObservableCollection<MMDevice> sources,
        ObservableCollection<ChannelViewModel> channels,
        Action<string, bool> setStatus,
        Action clearStatus,
        Action save,
        Action sourceChanged)
    {
        _engine = engine;
        _sources = sources;
        _channels = channels;
        _setStatus = setStatus;
        _clearStatus = clearStatus;
        _save = save;
        _sourceChanged = sourceChanged;
        // This class decides WHEN to look at the devices; _deviceSync does the reconciling.
        _deviceSync = new SourceDeviceSync(
            sources, channels, _unstartableSources,
            () => SelectedSource,
            replacement => SelectedSource = replacement,
            save);
    }

    /// <summary>How often the background watchdog re-checks devices and engine health.</summary>
    public static TimeSpan HealthInterval { get; } = TimeSpan.FromSeconds(3);

    /// <summary>Watchdog ticks with forced channel-retry budgets after a resume (~30 s).</summary>
    private const int ResumeGraceTicks = 10;

    /// <summary>A tick gap this large means the machine slept through timer time - treat as resume.</summary>
    private static readonly TimeSpan ClockJumpThreshold = TimeSpan.FromSeconds(20);

    public MMDevice? SelectedSource { get; private set; }
    public string? PreferredSourceId { get; private set; }
    public string SourceName => SelectedSource?.FriendlyName ?? "(no source)";

    public void Initialize(string? preferredSourceId)
    {
        PreferredSourceId = preferredSourceId;
        SelectedSource = ResolveSource();
    }

    public void SelectSource(MMDevice source)
    {
        if (source == SelectedSource) return;
        SelectedSource = source;
        PreferredSourceId = source.ID;
        _unstartableSources.Remove(source.ID);
        RestartEngine(source);
        _save();
        _sourceChanged();
    }

    public void HandleSourceLost(Exception? error)
    {
        Log.Write($"MixerSourceRecovery: source lost ({error?.Message ?? "device removed"}), recovering");
        _setStatus($"Source '{SourceName}' disconnected - recovering...", false);
        TryRecover();
    }

    public void HealthCheck()
    {
        try
        {
            var now = DateTime.UtcNow;
            bool sleptThrough = now - _lastTickUtc > HealthInterval + ClockJumpThreshold;
            _lastTickUtc = now;
            if (sleptThrough)
            {
                Log.Write("HealthCheck: timer gap detected (sleep/resume), forcing full recovery");
                BeginResumeRecovery();
                return;
            }

            _deviceSync.Refresh();

            bool sourceGone = _engine.SourceId is not null
                              && _sources.All(d => d.ID != _engine.SourceId);

            if (_engine.IsCapturing && !sourceGone)
            {
                TrySwitchToPreferred();
                ReactivateWantedChannels();
                return;
            }

            if (_sources.Count == 0)
            {
                _setStatus("No audio output device available. Connect a device.", true);
                return;
            }

            Log.Write($"HealthCheck: recovery needed (capturing={_engine.IsCapturing}, sourceGone={sourceGone})");
            TryRecover();
            ReactivateWantedChannels();
        }
        catch (Exception ex)
        {
            Log.Write($"HealthCheck failed: {ex}");
        }
    }

    public void BeginResumeRecovery()
    {
        try
        {
            Log.Write("Resume detected: refreshing devices and restarting the engine");
            _resumeTicksLeft = ResumeGraceTicks;
            _unstartableSources.Clear();
            foreach (var channel in _channels) channel.ResetAutoRetry();
            HardRefreshSources();
            TryRecover();
        }
        catch (Exception ex)
        {
            Log.Write($"Resume recovery failed: {ex}");
        }
    }

    public void RestartEngine(MMDevice source)
    {
        foreach (var channel in _channels) channel.Suspend();

        string sourceId = source.ID;
        bool started = false;
        try
        {
            var live = AudioDevices.FindRenderById(sourceId)
                ?? throw new InvalidOperationException($"Device '{source.FriendlyName}' is not active.");
            _engine.Start(live);
            _clearStatus();
            started = true;
        }
        catch (COMException ex)
        {
            Log.Write($"Engine.Start FAILED for '{source.FriendlyName}': {ex}");
            _engine.Stop();
            _setStatus((uint)ex.HResult == 0x8889000A
                ? $"Cannot capture '{source.FriendlyName}': locked in exclusive mode by another app. Pick a different source."
                : $"Cannot capture '{source.FriendlyName}': error 0x{ex.HResult:X8}. Pick a different source.",
                true);
        }
        catch (Exception ex)
        {
            Log.Write($"Engine.Start FAILED for '{source.FriendlyName}': {ex}");
            _engine.Stop();
            _setStatus($"Cannot capture '{source.FriendlyName}': {ex.Message}", true);
        }

        foreach (var channel in _channels)
            channel.IsSource = channel.DeviceId == sourceId;

        if (started)
            foreach (var channel in _channels)
                channel.TryAutoReactivate(force: true);
    }

    private void ReactivateWantedChannels()
    {
        bool resumeGrace = _resumeTicksLeft > 0;
        if (resumeGrace) _resumeTicksLeft--;

        foreach (var channel in _channels)
        {
            if (resumeGrace) channel.ResetAutoRetry();
            channel.TryAutoReactivate();
        }
    }

    /// <summary>Re-enumerate every endpoint with a fresh COM instance (post-resume).</summary>
    private void HardRefreshSources()
    {
        _deviceSync.HardRefresh();
        _sourceChanged();
    }

    private void TryRecover()
    {
        if (_recovering) return;
        _recovering = true;
        try
        {
            _deviceSync.Refresh();

            var source = ResolveSource();
            if (source is null)
            {
                _setStatus("No audio output device available. Connect a device.", true);
                return;
            }

            bool switched = source.ID != SelectedSource?.ID;
            SelectedSource = source;
            RestartEngine(source);

            if (_engine.IsCapturing)
            {
                if (switched)
                    _setStatus($"Source switched to '{source.FriendlyName}'.", false);
                else
                    _clearStatus();
                _sourceChanged();
                _save();
            }
        }
        finally
        {
            _recovering = false;
        }
    }

    private void TrySwitchToPreferred()
    {
        if (!SourceSelectionRules.ShouldSwitchToPreferred(
                PreferredSourceId, _engine.SourceId, _unstartableSources,
                _sources.Select(d => d.ID).ToList())) return;

        var preferred = _sources.First(d => d.ID == PreferredSourceId);
        var fallback = SelectedSource;
        Log.Write($"HealthCheck: preferred source '{preferred.FriendlyName}' is back, switching from fallback '{fallback?.FriendlyName ?? "(none)"}'");
        SelectedSource = preferred;
        RestartEngine(preferred);

        if (_engine.IsCapturing)
        {
            _setStatus($"Source restored to '{preferred.FriendlyName}'.", false);
            _sourceChanged();
            return;
        }

        _unstartableSources.Add(preferred.ID);
        Log.Write($"Preferred source '{preferred.FriendlyName}' would not start; staying on fallback");
        if (fallback is not null)
        {
            SelectedSource = fallback;
            RestartEngine(fallback);
        }
        _sourceChanged();
    }

    private MMDevice? ResolveSource()
    {
        var id = SourceSelectionRules.Resolve(
            _sources.Select(d => d.ID).ToList(),
            PreferredSourceId,
            () => AudioDevices.GetDefaultRender().ID);
        return id is null ? null : _sources.First(d => d.ID == id);
    }
}
