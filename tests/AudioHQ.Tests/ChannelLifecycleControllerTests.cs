using System;
using System.Collections.Generic;
using AudioHQ.App.ViewModels;
using AudioHQ.Core;

namespace AudioHQ.Tests;

/// <summary>
/// The channel state machine driven against a fake activation service - no WASAPI, no real
/// device. These cover the paths that are impractical to arrange on a real desk: an endpoint
/// that vanishes exactly at activation, and a device that keeps failing until the retry
/// budget runs out.
/// </summary>
public sealed class ChannelLifecycleControllerTests
{
    /// <summary>Stands in for real hardware: records calls and returns whatever the test wants.</summary>
    private sealed class FakeActivation : IChannelActivationService
    {
        public int ActivateCalls { get; private set; }
        public string? BoundDeviceId { get; private set; }
        public List<ChannelActivationRequest> Requests { get; } = new();
        public Func<ChannelActivationResult> Result { get; set; } =
            () => new ChannelActivationResult(null, DeviceMissing: false, "");

        public void RebindDevice(string deviceId) => BoundDeviceId = deviceId;

        public ChannelActivationResult Activate(
            string channelName, double gain, bool muted, EqSettings eq,
            Action<OutputChannel, Exception?> playbackStopped)
        {
            ActivateCalls++;
            Requests.Add(new ChannelActivationRequest(channelName, gain, muted, eq));
            return Result();
        }
    }

    private sealed class Harness
    {
        public FakeActivation Activation { get; } = new();
        public ChannelLifecycleController Controller { get; }
        public string Status { get; private set; } = "";
        public int ActiveChangedCount { get; private set; }
        public int AvailabilityChangedCount { get; private set; }
        public int RefreshStatusCount { get; private set; }
        public ChannelActivationRequest Request { get; set; } =
            new("Speakers", 1.0, false, new EqSettings());

        public Harness(bool present = true)
        {
            Controller = new ChannelLifecycleController(
                new MirrorEngine(), "device-1", () => 30, present,
                request: () => Request,
                channelName: () => Request.Name,
                activeChanged: () => ActiveChangedCount++,
                availabilityChanged: () => AvailabilityChangedCount++,
                refreshStatus: () => RefreshStatusCount++,
                setStatus: s => Status = s,
                activation: Activation);
        }
    }

    [Fact]
    public void Controller_CanBeConstructedAgainstAFake_WithNoRealDevice()
    {
        var h = new Harness();

        Assert.False(h.Controller.IsActive);
        Assert.True(h.Controller.IsPresent);
        Assert.Null(h.Controller.Channel);
    }

    [Fact]
    public void Activate_PassesTheStripsCurrentValuesThrough()
    {
        var h = new Harness { Request = new ChannelActivationRequest("Desk", 0.5, true, new EqSettings { Bands = 6 }) };

        h.Controller.Activate();

        var sent = Assert.Single(h.Activation.Requests);
        Assert.Equal("Desk", sent.Name);
        Assert.Equal(0.5, sent.Gain);
        Assert.True(sent.Muted);
        Assert.Equal(6, sent.Eq.Bands);
    }

    [Fact]
    public void Activate_WhenTheDeviceVanished_MarksItAbsentRatherThanActive()
    {
        var h = new Harness();
        h.Activation.Result = () => new ChannelActivationResult(null, DeviceMissing: true, "");

        h.Controller.Activate();

        Assert.False(h.Controller.IsPresent);
        Assert.False(h.Controller.IsActive);
        // Presence changed, so availability was announced and the status refreshed.
        Assert.Equal(1, h.AvailabilityChangedCount);
        Assert.Equal(1, h.RefreshStatusCount);
    }

    [Fact]
    public void Activate_SurfacesTheFailureStatus()
    {
        var h = new Harness();
        h.Activation.Result = () => new ChannelActivationResult(null, DeviceMissing: false, "in use (exclusive)");

        h.Controller.Activate();

        Assert.Equal("in use (exclusive)", h.Status);
        Assert.False(h.Controller.IsActive);
    }

    [Fact]
    public void TryAutoReactivate_DoesNothingWhenTheChannelIsNotAvailable()
    {
        var h = new Harness();

        h.Controller.TryAutoReactivate(available: false, force: false);

        Assert.Equal(0, h.Activation.ActivateCalls);
    }

    [Fact]
    public void TryAutoReactivate_StopsHammeringAFailingDevice()
    {
        // The whole point of the retry budget: a device that never comes back must not be
        // retried on every 3s watchdog tick forever.
        var h = new Harness();
        h.Activation.Result = () => new ChannelActivationResult(null, DeviceMissing: false, "failed");

        for (int tick = 0; tick < 20; tick++)
            h.Controller.TryAutoReactivate(available: true, force: false);

        Assert.InRange(h.Activation.ActivateCalls, 1, 5);
    }

    [Fact]
    public void TryAutoReactivate_ForceBypassesTheExhaustedBudget()
    {
        var h = new Harness();
        h.Activation.Result = () => new ChannelActivationResult(null, DeviceMissing: false, "failed");
        for (int tick = 0; tick < 20; tick++)
            h.Controller.TryAutoReactivate(available: true, force: false);
        int afterBudgetRanOut = h.Activation.ActivateCalls;

        // Engine restart / resume must always get a fresh attempt.
        h.Controller.TryAutoReactivate(available: true, force: true);

        Assert.Equal(afterBudgetRanOut + 1, h.Activation.ActivateCalls);
    }

    [Fact]
    public void ResetAutoRetry_GivesAFailingDeviceAFreshBudget()
    {
        var h = new Harness();
        h.Activation.Result = () => new ChannelActivationResult(null, DeviceMissing: false, "failed");
        for (int tick = 0; tick < 20; tick++)
            h.Controller.TryAutoReactivate(available: true, force: false);
        int exhausted = h.Activation.ActivateCalls;

        h.Controller.ResetAutoRetry();
        h.Controller.TryAutoReactivate(available: true, force: false);

        Assert.True(h.Activation.ActivateCalls > exhausted, "reset should allow another attempt");
    }

    [Fact]
    public void SetPresent_IsIdempotent()
    {
        var h = new Harness(present: true);

        h.Controller.SetPresent(true);

        Assert.Equal(0, h.AvailabilityChangedCount);
    }

    [Fact]
    public void SetPresent_WhenTheDeviceReturns_RefillsTheRetryBudget()
    {
        var h = new Harness(present: true);
        h.Activation.Result = () => new ChannelActivationResult(null, DeviceMissing: false, "failed");
        for (int tick = 0; tick < 20; tick++)
            h.Controller.TryAutoReactivate(available: true, force: false);
        int exhausted = h.Activation.ActivateCalls;

        // The device goes away and comes back: a fresh device deserves a fresh budget.
        h.Controller.SetPresent(false);
        h.Controller.SetPresent(true);
        h.Controller.TryAutoReactivate(available: true, force: false);

        Assert.True(h.Activation.ActivateCalls > exhausted, "a returning device should be retried again");
    }

    [Fact]
    public void RebindDevice_PointsFutureActivationsAtTheNewEndpoint()
    {
        var h = new Harness(present: false);

        h.Controller.RebindDevice("device-2");

        Assert.Equal("device-2", h.Activation.BoundDeviceId);
        // The replacement endpoint exists by definition, so the channel counts as present.
        Assert.True(h.Controller.IsPresent);
    }
}
