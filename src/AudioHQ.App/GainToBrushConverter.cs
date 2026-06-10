using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AudioHQ.App;

/// <summary>Fader color follows gain: green (normal) -> amber (hot) -> red (boost).</summary>
public sealed class GainToBrushConverter : IValueConverter
{
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
    private static readonly Brush Amber = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double gain = value is double d ? d : 0;
        return gain <= 0.8 ? Green : gain <= 1.0 ? Amber : Red;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
