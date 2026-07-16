using System.Windows;
using AudioHQ.App;

namespace AudioHQ.Tests;

/// <summary>
/// The rule that decides whether a saved window position is still usable. It matters on a
/// real desk: unplug the second monitor and yesterday's position can put AudioHQ somewhere
/// with no way to drag it back.
/// </summary>
public sealed class WindowPositionPersistenceTests
{
    // A single 1920x1080 monitor at the origin.
    private static readonly Rect SingleScreen = new(0, 0, 1920, 1080);
    // A second monitor to the LEFT of the primary, so the virtual desktop starts negative.
    private static readonly Rect DualScreenLeft = new(-1920, 0, 3840, 1080);

    [Fact]
    public void PositionWellInsideTheScreen_IsReachable() =>
        Assert.True(WindowPositionPersistence.IsReachable(600, 400, SingleScreen));

    [Fact]
    public void PositionOnASecondaryMonitorAtNegativeCoordinates_IsReachable() =>
        Assert.True(WindowPositionPersistence.IsReachable(-1500, 200, DualScreenLeft));

    [Fact]
    public void PositionFromAnUnpluggedSecondMonitor_IsNotReachable() =>
        // Saved while the left-hand monitor existed; now only the primary remains.
        Assert.False(WindowPositionPersistence.IsReachable(-1500, 200, SingleScreen));

    [Fact]
    public void PositionPastTheRightEdge_IsNotReachable() =>
        Assert.False(WindowPositionPersistence.IsReachable(1900, 400, SingleScreen));

    [Fact]
    public void PositionAboveTheTopEdge_IsNotReachable() =>
        // Far enough up that the title bar would be unreachable.
        Assert.False(WindowPositionPersistence.IsReachable(600, -60, SingleScreen));

    [Fact]
    public void PositionBelowTheBottomEdge_IsNotReachable() =>
        Assert.False(WindowPositionPersistence.IsReachable(600, 1060, SingleScreen));

    [Fact]
    public void EdgeIsAllowedExactlyAtTheVisibleMargin()
    {
        double edge = WindowPositionPersistence.VisibleEdge;

        // Exactly VisibleEdge of the window still lands on screen - allowed.
        Assert.True(WindowPositionPersistence.IsReachable(-edge, 0, SingleScreen));
        // One pixel further out and it is not.
        Assert.False(WindowPositionPersistence.IsReachable(-edge - 1, 0, SingleScreen));
    }

    [Theory]
    [InlineData(double.NaN, 100)]
    [InlineData(100, double.NaN)]
    [InlineData(double.PositiveInfinity, 100)]
    [InlineData(100, double.NegativeInfinity)]
    public void NonFinitePositions_AreNeverReachable(double left, double top) =>
        // A corrupt settings.json must fall back to default placement, not throw or fly off.
        Assert.False(WindowPositionPersistence.IsReachable(left, top, SingleScreen));
}
