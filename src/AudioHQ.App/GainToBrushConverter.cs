using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AudioHQ.App;

/// <summary>Fader color follows gain: green (up to 100%) -> amber (boost) -> red (max boost).</summary>
public sealed class GainToBrushConverter : IValueConverter
{
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
    private static readonly Brush Amber = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double gain = value is double d ? d : 0;
        // Green up to (and at) 100% unity; amber in the boost zone; red near max boost.
        return gain <= 1.0 ? Green : gain <= 1.25 ? Amber : Red;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
