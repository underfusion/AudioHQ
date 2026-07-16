using System;
using System.Windows;
using System.Windows.Media;

namespace AudioHQ.App;

/// <summary>
/// Typed access to the shared theme dictionaries (Resources/Theme) from C#.
///
/// Code must never keep its own copy of a theme colour: a private fallback silently drifts
/// from the palette the moment either side changes, which is exactly how the fader ramp and
/// the knob ended up with their own greens. The dictionaries are merged in App.xaml and ship
/// inside the app, so a missing key is a build/authoring mistake, not a runtime condition -
/// it throws rather than quietly painting the wrong colour.
/// </summary>
internal static class ThemeResources
{
    /// <summary>A frozen brush from the theme. Throws if <paramref name="key"/> is not defined.</summary>
    public static SolidColorBrush Brush(string key) => Get<SolidColorBrush>(key);

    /// <summary>A colour from the theme palette. Throws if <paramref name="key"/> is not defined.</summary>
    public static Color Color(string key) => Get<Color>(key);

    /// <summary>
    /// A theme colour as a GDI+ colour, for the places that draw with System.Drawing rather
    /// than WPF (the tray icon and the taskbar overlay dot).
    /// </summary>
    public static System.Drawing.Color DrawingColor(string key)
    {
        var c = Color(key);
        return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
    }

    private static T Get<T>(string key)
    {
        var app = Application.Current
            ?? throw new InvalidOperationException($"Theme resource '{key}' requested with no Application.");
        return app.Resources[key] is T value
            ? value
            : throw new InvalidOperationException($"Theme resource '{key}' is missing or is not a {typeof(T).Name}.");
    }
}
