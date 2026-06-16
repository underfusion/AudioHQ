using System;
using System.Windows;

namespace AudioHQ.App;

/// <summary>Shared placement helpers for the app's child dialogs.</summary>
public static class WindowPlacement
{
    /// <summary>
    /// Dock <paramref name="window"/> just off its owner's right edge (or left, if there is no
    /// room on screen) instead of stacking it on top. The window stays normal and movable once
    /// placed. Call after the window has measured (e.g. from its Loaded handler).
    /// </summary>
    public static void BesideOwner(Window window)
    {
        if (window.Owner is not { } owner) return;
        const double gap = 8;
        var area = SystemParameters.WorkArea;

        double right = owner.Left + owner.ActualWidth + gap;
        if (right + window.ActualWidth <= area.Right)
            window.Left = right;
        else
        {
            double left = owner.Left - window.ActualWidth - gap;
            window.Left = left >= area.Left ? left : Math.Max(area.Left, area.Right - window.ActualWidth);
        }

        window.Top = Math.Min(Math.Max(area.Top, owner.Top), Math.Max(area.Top, area.Bottom - window.ActualHeight));
    }
}
