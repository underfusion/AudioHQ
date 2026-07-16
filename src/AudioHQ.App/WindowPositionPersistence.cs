using System;
using System.Windows;

namespace AudioHQ.App;

/// <summary>
/// Remembers the main window's position across runs.
///
/// Restoring is deliberately conditional: a saved position can point at a monitor that has
/// since been unplugged or a resolution that shrank, which would put the window somewhere the
/// user cannot reach it. In that case we drop the saved position and let WPF place the window.
/// </summary>
public static class WindowPositionPersistence
{
    /// <summary>
    /// How much of the window's top-left corner must land inside the virtual desktop for the
    /// position to count as reachable - roughly "enough title bar to grab".
    /// </summary>
    public const double VisibleEdge = 50;

    /// <summary>
    /// True when a window placed at this point would still be reachable on the given virtual
    /// desktop. Pure, so the rule can be tested without a Window or real monitors.
    /// </summary>
    public static bool IsReachable(double left, double top, Rect virtualScreen)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top)) return false;

        return left + VisibleEdge >= virtualScreen.Left &&
               left <= virtualScreen.Left + virtualScreen.Width - VisibleEdge &&
               top + VisibleEdge >= virtualScreen.Top &&
               top <= virtualScreen.Top + virtualScreen.Height - VisibleEdge;
    }

    /// <summary>The current virtual desktop across all monitors.</summary>
    public static Rect VirtualScreen => new(
        SystemParameters.VirtualScreenLeft,
        SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth,
        SystemParameters.VirtualScreenHeight);

    /// <summary>
    /// Applies the saved position, unless there is none or it is no longer reachable (first
    /// run, or the monitor it was on is gone) - then the caller's default placement stands.
    /// </summary>
    public static void Restore(Window window, MixerSettings settings)
    {
        if (settings is not { MainWindowLeft: double left, MainWindowTop: double top }) return;
        if (!IsReachable(left, top, VirtualScreen)) return;

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = left;
        window.Top = top;
    }

    /// <summary>
    /// Records the window's normal position. While minimized or maximized, Left/Top describe
    /// the transient state, so RestoreBounds - the last normal position - is what to keep.
    /// </summary>
    public static void Save(Window window, MixerSettings settings)
    {
        Rect placement = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
            : window.RestoreBounds;

        if (!double.IsFinite(placement.Left) || !double.IsFinite(placement.Top)) return;

        settings.MainWindowLeft = placement.Left;
        settings.MainWindowTop = placement.Top;
    }
}
