using System;
using System.Windows;

namespace AudioHQ.App;

/// <summary>Shared placement helpers for the app's child dialogs.</summary>
public static class WindowPlacement
{
    public const double ChildWindowGap = 8;

    /// <summary>Place a panel immediately left of its owner with their top edges aligned.</summary>
    public static void LeftOfOwner(Window window)
    {
        if (window.Owner is not { } owner) return;
        var area = SystemParameters.WorkArea;
        window.Left = Math.Max(area.Left, owner.Left - window.ActualWidth - ChildWindowGap);
        window.Top = Math.Min(Math.Max(area.Top, owner.Top), Math.Max(area.Top, area.Bottom - window.ActualHeight));
    }

    /// <summary>
    /// Dock <paramref name="window"/> just off its owner's right edge (or left, if there is no
    /// room on screen) instead of stacking it on top. The window stays normal and movable once
    /// placed. Call after the window has measured (e.g. from its Loaded handler).
    /// </summary>
    public static void BesideOwner(Window window)
    {
        if (window.Owner is not { } owner) return;
        var area = SystemParameters.WorkArea;

        double right = owner.Left + owner.ActualWidth + ChildWindowGap;
        if (right + window.ActualWidth <= area.Right)
            window.Left = right;
        else
        {
            double left = owner.Left - window.ActualWidth - ChildWindowGap;
            window.Left = left >= area.Left ? left : Math.Max(area.Left, area.Right - window.ActualWidth);
        }

        window.Top = Math.Min(Math.Max(area.Top, owner.Top), Math.Max(area.Top, area.Bottom - window.ActualHeight));
    }

    /// <summary>
    /// Keep an owned window at its current offset from the owner. Moving the child manually
    /// establishes a new offset; moving the owner then carries the child with the app group.
    /// Register before showing the child, after its own placement handler has been registered.
    /// </summary>
    public static void FollowOwner(Window window)
    {
        if (window.Owner is not { } owner) return;
        double offsetLeft = 0;
        double offsetTop = 0;
        bool ready = false;
        bool movingWithOwner = false;

        RoutedEventHandler? loaded = null;
        EventHandler? ownerMoved = null;
        EventHandler? childMoved = null;
        EventHandler? closed = null;

        loaded = (_, _) =>
        {
            offsetLeft = window.Left - owner.Left;
            offsetTop = window.Top - owner.Top;
            ready = true;
        };
        childMoved = (_, _) =>
        {
            if (!ready || movingWithOwner) return;
            offsetLeft = window.Left - owner.Left;
            offsetTop = window.Top - owner.Top;
        };
        ownerMoved = (_, _) =>
        {
            if (!ready) return;
            movingWithOwner = true;
            window.Left = owner.Left + offsetLeft;
            window.Top = owner.Top + offsetTop;
            movingWithOwner = false;
        };
        closed = (_, _) =>
        {
            window.Loaded -= loaded;
            window.LocationChanged -= childMoved;
            owner.LocationChanged -= ownerMoved;
            window.Closed -= closed;
        };

        window.Loaded += loaded;
        window.LocationChanged += childMoved;
        owner.LocationChanged += ownerMoved;
        window.Closed += closed;
    }
}
