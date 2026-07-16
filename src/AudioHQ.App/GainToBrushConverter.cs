using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AudioHQ.App;

/// <summary>Fader color follows gain: green (up to 100%) -> amber (boost) -> red (max boost).</summary>
public sealed class GainToBrushConverter : IValueConverter
{
    // Read from the theme instead of redefined here: a private copy is exactly how this
    // converter's green drifted away from the palette. Resolved on first use and cached -
    // the theme brushes are frozen and never change at runtime.
    private static Brush? _green, _amber, _red;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        _green ??= ThemeResources.Brush("Brush.AccentPositive");
        _amber ??= ThemeResources.Brush("Brush.FaderAmber");
        _red ??= ThemeResources.Brush("Brush.FaderRed");

        double gain = value is double d ? d : 0;
        // Green up to (and at) 100% unity; amber in the boost zone; red near max boost.
        return gain <= 1.0 ? _green : gain <= 1.25 ? _amber : _red;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
