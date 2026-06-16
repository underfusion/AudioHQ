using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AudioHQ.App.ViewModels;
using AudioHQ.Core;

namespace AudioHQ.App;

/// <summary>Per-channel graphic-EQ editor. Binds to the channel's <see cref="ChannelViewModel"/>.</summary>
public partial class EqWindow : Window
{
    private EqViewModel? _eq;
    private const double MinDb = -12.0, MaxDb = 12.0;

    public EqWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ChannelViewModel channel) return;
        _eq = channel.Eq;
        _eq.Bands.CollectionChanged += Bands_CollectionChanged;
        HookBands();
        CurveCanvas.SizeChanged += (_, _) => RedrawCurve();
        WindowPlacement.BesideOwner(this);
        SyncPresetSelection(); // show "Default" (or Custom) for the current curve
        RedrawLater();
    }

    private void Bands_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HookBands();
        SyncPresetSelection();
        RedrawLater(); // wait for the new fader containers to lay out
    }

    private void HookBands()
    {
        if (_eq is null) return;
        foreach (var band in _eq.Bands)
        {
            band.PropertyChanged -= Band_PropertyChanged;
            band.PropertyChanged += Band_PropertyChanged;
        }
    }

    private void Band_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EqBandViewModel.GainDb) or nameof(EqBandViewModel.Q))
        {
            RedrawCurve();
            SyncPresetSelection();
        }
    }

    // --- Preset reconciliation -------------------------------------------------

    /// <summary>
    /// Select the preset whose curve matches the live one, or - if none does - show
    /// "Custom (not saved)". Driven by the current curve, so it survives reopening the editor.
    /// </summary>
    private void SyncPresetSelection()
    {
        if (DataContext is not ChannelViewModel channel) return;
        var current = channel.Eq.ToSettings();
        var match = channel.EqPresets.Presets.FirstOrDefault(p => CurveEquals(p.Eq, current));
        PresetCombo.SelectedItem = match; // null -> the custom label shows through
        UpdateCustomLabel();
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCustomLabel();

    private void UpdateCustomLabel()
    {
        if (CustomLabel is not null)
            CustomLabel.Visibility = PresetCombo.SelectedItem is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>True when two curves are the same shape (band count, gains and effective Q),
    /// ignoring the on/off flag - flipping Enable should not change which preset is shown.</summary>
    private static bool CurveEquals(EqSettings a, EqSettings b)
    {
        int bands = a.Bands == 6 ? 6 : 3;
        if (bands != (b.Bands == 6 ? 6 : 3)) return false;
        double defaultQ = EqBands.Q(bands);
        for (int i = 0; i < bands; i++)
        {
            double ga = i < a.GainsDb.Length ? a.GainsDb[i] : 0.0;
            double gb = i < b.GainsDb.Length ? b.GainsDb[i] : 0.0;
            if (Math.Abs(ga - gb) > 0.05) return false;

            double qa = a.QValues is not null && i < a.QValues.Length && a.QValues[i] > 0 ? a.QValues[i] : defaultQ;
            double qb = b.QValues is not null && i < b.QValues.Length && b.QValues[i] > 0 ? b.QValues[i] : defaultQ;
            if (Math.Abs(qa - qb) > 0.01) return false;
        }
        return true;
    }

    private void RedrawLater() =>
        Dispatcher.BeginInvoke(new Action(RedrawCurve), DispatcherPriority.Loaded);

    /// <summary>
    /// Draw the green 0 dB baseline (aligned to the fader centres) and the blue response curve
    /// behind the faders. Each band contributes a bell whose height is its gain and whose width
    /// follows its Q (low Q = wide/round, high Q = narrow/sharp), summed across the spectrum -
    /// so the curve deforms with both the faders and the Q knobs, like a real EQ.
    /// </summary>
    private void RedrawCurve()
    {
        if (CurveCanvas is null) return;
        CurveCanvas.Children.Clear();

        var sliders = new List<Slider>();
        CollectSliders(BandItems, sliders);
        if (sliders.Count == 0) return;

        // Per-band peak position (x), signed pixel amplitude (up = boost) and Q.
        var xs = new List<double>();
        var amps = new List<double>();
        var qs = new List<double>();
        double baselineY = 0, sliderH = 0;
        foreach (var slider in sliders)
        {
            if (slider.DataContext is not EqBandViewModel band) continue;
            Point top = slider.TranslatePoint(new Point(slider.ActualWidth / 2, 0), CurveCanvas);
            double h = slider.ActualHeight;
            sliderH = h;
            baselineY = top.Y + h / 2;                 // 0 dB sits at the fader's vertical centre
            xs.Add(top.X);
            amps.Add(h * (band.GainDb / (MaxDb - MinDb))); // +/-half-height at +/-full gain
            qs.Add(band.Q);
        }
        if (xs.Count == 0) return;

        double w = CurveCanvas.ActualWidth;
        CurveCanvas.Children.Add(new Line
        {
            X1 = 0, X2 = w, Y1 = baselineY, Y2 = baselineY,
            Stroke = (Brush)FindResource("AccentGreen"), StrokeThickness = 2,
        });

        // Average fader spacing sets the bell width scale; Q narrows or widens it per band.
        double spacing = xs.Count > 1 ? (xs[xs.Count - 1] - xs[0]) / (xs.Count - 1) : w;
        double topLimit = baselineY - sliderH / 2;
        double bottomLimit = baselineY + sliderH / 2;

        var curve = new PointCollection();
        for (double x = 0; x <= w; x += 2)
        {
            double sum = 0;
            for (int i = 0; i < xs.Count; i++)
            {
                double hw = spacing / qs[i];           // half-width in px: lower Q = wider bell
                double t = (x - xs[i]) / hw;
                sum += amps[i] / (1 + t * t);          // Lorentzian bell, summed
            }
            double y = Math.Clamp(baselineY - sum, topLimit, bottomLimit);
            curve.Add(new Point(x, y));
        }

        CurveCanvas.Children.Add(new Polyline
        {
            Points = curve,
            Stroke = (Brush)FindResource("AccentBlue"),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
        });
    }

    private static void CollectSliders(DependencyObject? parent, List<Slider> result)
    {
        if (parent is null) return;
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Slider slider) result.Add(slider);
            else CollectSliders(child, result);
        }
    }

    // Double-click a fader to reset that band to 0 dB.
    private void BandSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is Slider slider && slider.DataContext is EqBandViewModel band)
        {
            band.GainDb = 0;
            e.Handled = true;
        }
    }

    // --- Presets ---------------------------------------------------------------

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ChannelViewModel channel) return;
        var saved = channel.EqPresets.Save(PresetName.Text, channel.Eq.ToSettings());
        if (saved is null) return;
        PresetName.Clear();
        PresetCombo.SelectedItem = saved;
        UpdateCustomLabel();
    }

    private void LoadPreset_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChannelViewModel channel && PresetCombo.SelectedItem is EqPreset preset)
            channel.Eq.Load(preset.Eq);
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChannelViewModel channel && PresetCombo.SelectedItem is EqPreset preset)
            channel.EqPresets.Delete(preset);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ChannelViewModel channel) return;
        channel.Eq.Reset();
        SyncPresetSelection(); // a flat curve is the Default preset
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
