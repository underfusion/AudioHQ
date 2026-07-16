using System;
using AudioHQ.App.ViewModels;

namespace AudioHQ.Tests;

/// <summary>
/// The rules for which device the mirror captures: the user's saved choice wins when it is
/// plugged in, the system default is the fallback, and the watchdog only switches back to
/// the saved device when that can actually work. These drive what happens after restarts,
/// unplugs and resumes.
/// </summary>
public sealed class SourceSelectionRulesTests
{
    private static readonly string[] Sources = { "dev-a", "dev-b", "dev-c" };

    [Fact]
    public void Resolve_PicksThePreferredDevice_WithoutTouchingTheDefault()
    {
        // The default lookup hits COM, so it must not run when the saved device is present.
        var resolved = SourceSelectionRules.Resolve(
            Sources, preferredId: "dev-b",
            defaultId: () => throw new InvalidOperationException("default must not be consulted"));

        Assert.Equal("dev-b", resolved);
    }

    [Theory]
    [InlineData(null)]           // nothing saved (first run)
    [InlineData("dev-gone")]     // saved device is unplugged
    public void Resolve_FallsBackToTheSystemDefault(string? preferredId)
    {
        Assert.Equal("dev-c", SourceSelectionRules.Resolve(Sources, preferredId, () => "dev-c"));
    }

    [Fact]
    public void Resolve_FallsBackToTheFirstDevice_WhenEvenTheDefaultIsAbsent()
    {
        Assert.Equal("dev-a", SourceSelectionRules.Resolve(Sources, "dev-gone", () => "dev-also-gone"));
        Assert.Equal("dev-a", SourceSelectionRules.Resolve(Sources, null, () => null));
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenThereIsNothingToCaptureFrom()
    {
        Assert.Null(SourceSelectionRules.Resolve(Array.Empty<string>(), "dev-a", () => null));
    }

    [Fact]
    public void ShouldSwitchToPreferred_WhenTheSavedDeviceIsBackAndUsable()
    {
        Assert.True(SourceSelectionRules.ShouldSwitchToPreferred(
            "dev-a", capturedId: "dev-b", unstartableIds: Array.Empty<string>(), presentIds: Sources));
    }

    [Fact]
    public void ShouldSwitchToPreferred_NeverWithoutASavedDevice()
    {
        Assert.False(SourceSelectionRules.ShouldSwitchToPreferred(
            null, capturedId: "dev-b", unstartableIds: Array.Empty<string>(), presentIds: Sources));
    }

    [Fact]
    public void ShouldSwitchToPreferred_NotWhenAlreadyCapturingIt()
    {
        Assert.False(SourceSelectionRules.ShouldSwitchToPreferred(
            "dev-a", capturedId: "dev-a", unstartableIds: Array.Empty<string>(), presentIds: Sources));
    }

    [Fact]
    public void ShouldSwitchToPreferred_NotWhenItAlreadyProvedUnstartable()
    {
        // A preferred device that failed to start once must not be retried every watchdog
        // tick - it stays on the fallback until something resets the unstartable set.
        Assert.False(SourceSelectionRules.ShouldSwitchToPreferred(
            "dev-a", capturedId: "dev-b", unstartableIds: new[] { "dev-a" }, presentIds: Sources));
    }

    [Fact]
    public void ShouldSwitchToPreferred_NotWhileItIsStillUnplugged()
    {
        Assert.False(SourceSelectionRules.ShouldSwitchToPreferred(
            "dev-gone", capturedId: "dev-b", unstartableIds: Array.Empty<string>(), presentIds: Sources));
    }
}
