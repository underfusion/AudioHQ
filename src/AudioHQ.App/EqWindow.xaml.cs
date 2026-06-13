using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AudioHQ.App.ViewModels;

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
        if (PresetCombo.SelectedItem is null && PresetCombo.Items.Count > 0)
            PresetCombo.SelectedIndex = 0; // the built-in Default
        RedrawLater();
    }

    private void Bands_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HookBands();
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
        if (e.PropertyName == nameof(EqBandViewModel.GainDb)) RedrawCurve();
    }

    private void RedrawLater() =>
        Dispatcher.BeginInvoke(new Action(RedrawCurve), DispatcherPriority.Loaded);

    /// <summary>
    /// Draw the green 0 dB baseline (aligned to the fader centres) and the blue response
    /// curve through the current band gains, behind the faders.
    /// </summary>
    private void RedrawCurve()
    {
        if (CurveCanvas is null) return;
        CurveCanvas.Children.Clear();

        var sliders = new List<Slider>();
        CollectSliders(BandItems, sliders);
        if (sliders.Count == 0) return;

        var points = new PointCollection();
        double baselineY = 0;
        foreach (var slider in sliders)
        {
            if (slider.DataContext is not EqBandViewModel band) continue;
            Point top = slider.TranslatePoint(new Point(slider.ActualWidth / 2, 0), CurveCanvas);
            double h = slider.ActualHeight;
            double frac = (band.GainDb - MinDb) / (MaxDb - MinDb); // 0 (bottom) .. 1 (top)
            points.Add(new Point(top.X, top.Y + (1 - frac) * h));
            baselineY = top.Y + h / 2; // 0 dB sits at the fader's vertical centre
        }
        if (points.Count == 0) return;

        double w = CurveCanvas.ActualWidth;
        CurveCanvas.Children.Add(new Line
        {
            X1 = 0, X2 = w, Y1 = baselineY, Y2 = baselineY,
            Stroke = (Brush)FindResource("AccentGreen"), StrokeThickness = 2,
        });

        // Extend the curve flat to both edges so it spans the whole graph.
        var curvePoints = new PointCollection { new Point(0, points[0].Y) };
        foreach (var p in points) curvePoints.Add(p);
        curvePoints.Add(new Point(w, points[points.Count - 1].Y));

        CurveCanvas.Children.Add(new Polyline
        {
            Points = curvePoints,
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
        if (DataContext is ChannelViewModel channel) channel.Eq.Reset();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
