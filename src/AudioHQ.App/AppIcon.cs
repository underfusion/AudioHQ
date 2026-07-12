using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AudioHQ.Core;
using Drawing = System.Drawing;

namespace AudioHQ.App;

/// <summary>
/// Extracts a frozen WPF <see cref="ImageSource"/> from an app session's executable, for
/// the per-app mixer rows. UI-only (uses System.Drawing via the WinForms reference that is
/// already on for the tray). Never throws; returns null so the row shows a neutral
/// placeholder when no icon can be read (system sounds, protected/elevated apps).
/// </summary>
internal static class AppIcon
{
    public static ImageSource? ForSession(AppSession session)
    {
        if (session.IsSystemSounds) return null; // neutral placeholder; no speaker asset shipped
        return FromExe(session.ExecutablePath);
    }

    private static ImageSource? FromExe(string exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return null;
        try
        {
            using var icon = Drawing.Icon.ExtractAssociatedIcon(exePath);
            return icon is null ? null : Convert(icon);
        }
        catch (Exception ex)
        {
            Log.Write($"AppIcon: '{exePath}' failed: {ex.Message}");
            return null;
        }
    }

    private static ImageSource Convert(Drawing.Icon icon)
    {
        // CreateBitmapSourceFromHIcon copies the pixels, so the icon (and its handle) can be
        // disposed right after; freeze so the result is safe to bind from any thread.
        var source = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }
}
