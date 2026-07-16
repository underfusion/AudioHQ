using System;
using AudioHQ.Core;

namespace AudioHQ.App.ViewModels;

/// <summary>What the live output needs from the strip in order to open. Snapshotted at the
/// moment of activation, so the controller never reaches back into the view model's state.</summary>
public readonly record struct ChannelActivationRequest(
    string Name, double Gain, bool Muted, EqSettings Eq);

/// <summary>
/// The device half of a channel: whether a live output is open, whether its endpoint exists,
/// and the retry budget behind auto-reconnect. <see cref="ChannelViewModel"/> keeps the
/// bindable state (name, gain, mute, EQ, focus) and observes this through callbacks.
///
/// The split matters because these are different clocks: the view model changes when the USER
/// does something, this changes when the DEVICE does (dies, returns, gets replaced).
///
/// Every transition here is mechanical - none of them touch the user's ON intent, which stays
/// in the view model. That is what lets a channel come back by itself after a device drops.
/// </summary>
public sealed class ChannelLifecycleController
{
    private readonly MirrorEngine _engine;
    private readonly IChannelActivationService _activation;
    private readonly ChannelRetryBudget _retryBudget = new();

    private readonly Func<ChannelActivationRequest> _request;
    private readonly Action _activeChanged;
    private readonly Action _availabilityChanged;
    private readonly Action _refreshStatus;
    private readonly Action<string> _setStatus;
    private readonly Func<string> _channelName;

    private OutputChannel? _channel;

    public ChannelLifecycleController(
        MirrorEngine engine,
        string deviceId,
        Func<int> latencyMs,
        bool present,
        Func<ChannelActivationRequest> request,
        Func<string> channelName,
        Action activeChanged,
        Action availabilityChanged,
        Action refreshStatus,
        Action<string> setStatus,
        IChannelActivationService? activation = null)
    {
        _engine = engine;
        // Default to the real WASAPI service; tests pass a fake. Constructor injection with a
        // default argument, matching the rest of the codebase (no DI framework).
        _activation = activation ?? new ChannelActivationService(engine, deviceId, latencyMs);
        IsPresent = present;
        _request = request;
        _channelName = channelName;
        _activeChanged = activeChanged;
        _availabilityChanged = availabilityChanged;
        _refreshStatus = refreshStatus;
        _setStatus = setStatus;
    }

    /// <summary>True while a live output is open on the device.</summary>
    public bool IsActive { get; private set; }

    /// <summary>True while the saved device is currently enumerated as active.</summary>
    public bool IsPresent { get; private set; }

    /// <summary>The live output's gain/mute/EQ, or null when the channel is not running.</summary>
    public OutputChannel? Channel => _channel;

    /// <summary>Open a live output on a FRESH device instance. Sets <see cref="IsActive"/> on success.</summary>
    public void Activate()
    {
        var request = _request();
        var result = _activation.Activate(request.Name, request.Gain, request.Muted, request.Eq, OnPlaybackStopped);
        if (result.DeviceMissing)
        {
            SetPresent(false);
            return;
        }

        _channel = result.Channel;
        IsActive = result.IsActive;
        _setStatus(result.Status);
        if (IsActive) _retryBudget.Reset();
    }

    /// <summary>Close the live output (if any) without touching intent or the toggle state.</summary>
    public void Detach()
    {
        if (_channel is null) return;
        _channel.PlaybackStopped -= OnPlaybackStopped;
        _engine.RemoveOutput(_channel);
        _channel = null;
    }

    /// <summary>
    /// Close the output on the USER's request. Same effect as <see cref="Suspend"/> minus the
    /// change callback: the caller is the view-model setter, which notifies for itself.
    /// </summary>
    public void Deactivate()
    {
        Detach();
        IsActive = false;
    }

    /// <summary>
    /// Deactivate WITHOUT clearing the ON intent - used for mechanical stops (engine restart,
    /// device loss, sleep). The watchdog reactivates the channel when it becomes possible.
    /// </summary>
    public void Suspend()
    {
        Detach();
        if (!IsActive) return;
        IsActive = false;
        _activeChanged();
    }

    /// <summary>
    /// Watchdog hook: bring the channel back if it can run. Retries are budgeted so a
    /// persistently failing device is not hammered every tick; the budget resets when the
    /// device reappears, on resume, or on an explicit user action.
    /// <paramref name="force"/> (engine restart, resume) bypasses the budget.
    /// </summary>
    public void TryAutoReactivate(bool available, bool force)
    {
        if (IsActive || !available) return;
        if (!_retryBudget.TryConsume(force)) return;

        Activate();
        if (IsActive)
        {
            Log.Write($"Channel '{_channelName()}': auto-reactivated");
            _activeChanged();
        }
    }

    /// <summary>Give a failing device a fresh retry budget (called on resume).</summary>
    public void ResetAutoRetry() => _retryBudget.Reset();

    /// <summary>Mark the saved device as (re)appeared or gone, from the mixer's device sync.</summary>
    public void SetPresent(bool present)
    {
        if (IsPresent == present) return;
        IsPresent = present;
        _availabilityChanged();
        if (!present) Suspend();    // keep the ON intent - it comes back with the device
        else _retryBudget.Reset();  // fresh device, fresh retry budget
        // After Suspend, so the status reflects the channel having actually stopped.
        _refreshStatus();
    }

    /// <summary>Adopt a replacement endpoint id for the same uniquely named physical output.</summary>
    public void RebindDevice(string deviceId)
    {
        Suspend();
        _activation.RebindDevice(deviceId);
        IsPresent = true;
        _retryBudget.Reset();
    }

    /// <summary>Engine callback (render thread) for an unsolicited output stop.</summary>
    private void OnPlaybackStopped(OutputChannel channel, Exception? error) =>
        UiDispatcher.Post(() => HandlePlaybackStopped(channel, error));

    private void HandlePlaybackStopped(OutputChannel channel, Exception? error)
    {
        if (!ReferenceEquals(channel, _channel)) return; // already detached or replaced
        Log.Write($"Channel '{_channelName()}': output died ({error?.Message ?? "no error"}), will reconnect");
        Detach();
        IsActive = false;
        _activeChanged();
        _setStatus("Reconnecting...");
        // Intent is preserved; the mixer watchdog (or resume recovery) reactivates it.
    }
}
