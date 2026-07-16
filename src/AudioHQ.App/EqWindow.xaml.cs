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
    private EqPreset? _activePreset;
    private bool _isPresetDirty;
    private bool _syncingPresetSelection;
    private bool _loadingPreset;
    private const double MinDb = -36.0, MaxDb = 12.0;

    public EqWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    // The band collection and its items outlive this dialog (they belong to the channel);
    // unhook on close or every opened-and-closed editor stays rooted by the channel's EQ model.
    private void OnClosed(object? sender, EventArgs e)
    {
        if (_eq is null) return;
        _eq.PropertyChanged -= Eq_PropertyChanged;
        _eq.Bands.CollectionChanged -= Bands_CollectionChanged;
        foreach (var band in _eq.Bands)
            band.PropertyChanged -= Band_PropertyChanged;
        _eq = null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ChannelViewModel channel) return;
        _eq = channel.Eq;
        _eq.PropertyChanged += Eq_PropertyChanged;
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

    private void Eq_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EqViewModel.Enabled)
            or nameof(EqViewModel.LowPassEnabled)
            or nameof(EqViewModel.LowPassHz)
            or nameof(EqViewModel.LowPassSlope))
            SyncPresetSelection();
    }

    // --- Preset reconciliation -------------------------------------------------

    /// <summary>
    /// Keep one active preset as the reset/overwrite baseline. Edits retain that selection
    /// and display "Preset name (not saved)" instead of replacing it with anonymous Custom.
    /// </summary>
    private void SyncPresetSelection()
    {
        if (DataContext is not ChannelViewModel channel) return;
        var current = channel.Eq.ToSettings();
        if (_loadingPreset) return;

        if (_activePreset is null || !channel.EqPresets.Presets.Contains(_activePreset))
            _activePreset = channel.EqPresets.Presets.FirstOrDefault(p => CurveEquals(p.Eq, current));

        _isPresetDirty = _activePreset is not null && !CurveEquals(_activePreset.Eq, current);
        _syncingPresetSelection = true;
        PresetCombo.SelectedItem = _activePreset;
        _syncingPresetSelection = false;
        UpdatePresetPresentation();
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingPresetSelection) return;
        if (DataContext is not ChannelViewModel channel || PresetCombo.SelectedItem is not EqPreset preset)
            return;

        _activePreset = preset;
        _isPresetDirty = false;
        _loadingPreset = true;
        channel.Eq.Load(preset.Eq);
        _loadingPreset = false;
        SyncPresetSelection();
        RedrawLater();
    }

    private void UpdatePresetPresentation()
    {
        if (PresetStatusLabel is not null)
        {
            PresetStatusLabel.Text = _activePreset is null
                ? "Custom (not saved)"
                : $"{_activePreset.Name} (not saved)";
            PresetStatusLabel.Visibility = _activePreset is null || _isPresetDirty
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        if (ResetPresetButton is not null)
            ResetPresetButton.IsEnabled = _activePreset is not null && _isPresetDirty;
        if (DeletePresetButton is not null)
            DeletePresetButton.IsEnabled = _activePreset is not null && !EqPresetStore.IsDefault(_activePreset);
        UpdatePresetSaveAction();
    }

    private void PresetName_TextChanged(object sender, TextChangedEventArgs e) => UpdatePresetSaveAction();

    private void UpdatePresetSaveAction()
    {
        if (SavePresetButton is null || PresetName is null) return;
        bool willOverwrite = CanOverwriteSelectedPreset();
        SavePresetButton.Content = willOverwrite ? "Overwrite preset" : "Save preset";
    }

    private bool CanOverwriteSelectedPreset() =>
        string.IsNullOrWhiteSpace(PresetName.Text) &&
        _isPresetDirty &&
        _activePreset is not null &&
        !EqPresetStore.IsDefault(_activePreset);

    /// <summary>True when two complete preset states match.</summary>
    private static bool CurveEquals(EqSettings a, EqSettings b)
    {
        if (a.Enabled != b.Enabled) return false;
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
        if (a.LowPassEnabled != b.LowPassEnabled) return false;
        if (a.LowPassEnabled &&
            (Math.Abs(a.LowPassHz - b.LowPassHz) > 0.5 || a.LowPassSlope != b.LowPassSlope))
            return false;
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
        double baselineY = 0, sliderH = 0, faderTopY = 0;
        foreach (var slider in sliders)
        {
            if (slider.DataContext is not EqBandViewModel band) continue;
            Point top = slider.TranslatePoint(new Point(slider.ActualWidth / 2, 0), CurveCanvas);
            double h = slider.ActualHeight;
            sliderH = h;
            faderTopY = top.Y;
            // Range is asymmetric (+12 .. -36 dB), so 0 dB is not the fader centre: it sits
            // MaxDb/(MaxDb-MinDb) of the way down from the top (a quarter of the travel).
            baselineY = top.Y + h * (MaxDb / (MaxDb - MinDb));
            xs.Add(top.X);
            amps.Add(h * (band.GainDb / (MaxDb - MinDb))); // pixels = dB * (height / full range)
            qs.Add(band.Q);
        }
        if (xs.Count == 0) return;

        double w = CurveCanvas.ActualWidth;
        CurveCanvas.Children.Add(new Line
        {
            X1 = 0, X2 = w, Y1 = baselineY, Y2 = baselineY,
            Stroke = ThemeResources.Brush("Brush.AccentPositive"), StrokeThickness = 2,
        });

        // Average fader spacing sets the bell width scale; Q narrows or widens it per band.
        double spacing = xs.Count > 1 ? (xs[xs.Count - 1] - xs[0]) / (xs.Count - 1) : w;
        double topLimit = faderTopY;                 // +MaxDb (top of the fader travel)
        double bottomLimit = faderTopY + sliderH;    // -|MinDb| (bottom of the travel)

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
            Stroke = ThemeResources.Brush("Brush.AccentInfo"),
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
        string typedName = PresetName.Text.Trim();
        string targetName = typedName.Length > 0
            ? typedName
            : CanOverwriteSelectedPreset() ? _activePreset!.Name : "";
        var saved = channel.EqPresets.Save(targetName, channel.Eq.ToSettings());
        if (saved is null) return;
        _activePreset = saved;
        _isPresetDirty = false;
        PresetName.Clear();
        _syncingPresetSelection = true;
        PresetCombo.SelectedItem = saved;
        _syncingPresetSelection = false;
        UpdatePresetPresentation();
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChannelViewModel channel && _activePreset is { } preset)
        {
            channel.EqPresets.Delete(preset);
            _activePreset = null;
            SyncPresetSelection();
        }
    }

    private void ResetPreset_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ChannelViewModel channel || _activePreset is null || !_isPresetDirty) return;
        _loadingPreset = true;
        channel.Eq.Load(_activePreset.Eq);
        _loadingPreset = false;
        SyncPresetSelection();
        RedrawLater();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
