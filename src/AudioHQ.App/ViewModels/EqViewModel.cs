using System;
using System.Collections.ObjectModel;
using System.Linq;
using AudioHQ.Core;

namespace AudioHQ.App.ViewModels;

/// <summary>
/// Editable graphic-EQ model for one channel: enable flag, 3/6-band switch and the
/// per-band faders. Any edit invokes <c>onChanged</c>, which applies the new curve to
/// the live output (if active) and marks the settings dirty.
/// </summary>
public sealed class EqViewModel : ViewModelBase
{
    private readonly Action _onChanged;
    private bool _enabled;
    private int _bandCount;
    private bool _lowPassEnabled;
    private double _lowPassHz;
    private int _lowPassSlope;

    public ObservableCollection<EqBandViewModel> Bands { get; } = new();

    public EqViewModel(EqSettings? settings, Action onChanged)
    {
        _onChanged = onChanged;
        _enabled = settings?.Enabled ?? false;
        _bandCount = settings?.Bands == 6 ? 6 : 3;
        _lowPassEnabled = settings?.LowPassEnabled ?? false;
        _lowPassHz = settings?.LowPassHz is > 0 ? settings!.LowPassHz : EqBands.LowPassDefaultHz;
        _lowPassSlope = settings?.LowPassSlope == 1 ? 1 : 2;
        BuildBands(settings?.GainsDb, settings?.QValues);
    }

    public bool Enabled
    {
        get => _enabled;
        set { if (_enabled == value) return; _enabled = value; OnPropertyChanged(); _onChanged(); }
    }

    /// <summary>Band count: 3 or 6. Changing it resets the curve flat (the bands differ).</summary>
    public int BandCount
    {
        get => _bandCount;
        set
        {
            int b = value == 6 ? 6 : 3;
            if (_bandCount == b) return;
            _bandCount = b;
            BuildBands(null, null);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Is3Band));
            OnPropertyChanged(nameof(Is6Band));
            _onChanged();
        }
    }

    // Radio-button bindings for the band-count selector.
    public bool Is3Band { get => _bandCount == 3; set { if (value) BandCount = 3; } }
    public bool Is6Band { get => _bandCount == 6; set { if (value) BandCount = 6; } }

    /// <summary>"Bass-only" high-cut on/off. Applies on top of the peaking bands.</summary>
    public bool LowPassEnabled
    {
        get => _lowPassEnabled;
        set { if (_lowPassEnabled == value) return; _lowPassEnabled = value; OnPropertyChanged(); _onChanged(); }
    }

    /// <summary>Low-pass cutoff in Hz, clamped to the knob range.</summary>
    public double LowPassHz
    {
        get => _lowPassHz;
        set
        {
            double v = Math.Clamp(value, EqBands.LowPassMinHz, EqBands.LowPassMaxHz);
            if (Math.Abs(_lowPassHz - v) < 0.01) return;
            _lowPassHz = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LowPassText));
            _onChanged();
        }
    }

    public string LowPassText => $"{_lowPassHz:0} Hz";

    /// <summary>Slope in cascaded stages: 1 = 12 dB/oct, 2 = 24 dB/oct.</summary>
    public int LowPassSlope
    {
        get => _lowPassSlope;
        set
        {
            int s = value == 1 ? 1 : 2;
            if (_lowPassSlope == s) return;
            _lowPassSlope = s;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Is12dB));
            OnPropertyChanged(nameof(Is24dB));
            _onChanged();
        }
    }

    // Radio-button bindings for the slope selector.
    public bool Is12dB { get => _lowPassSlope == 1; set { if (value) LowPassSlope = 1; } }
    public bool Is24dB { get => _lowPassSlope == 2; set { if (value) LowPassSlope = 2; } }

    private void BuildBands(double[]? gains, double[]? qs)
    {
        Bands.Clear();
        var freqs = EqBands.Frequencies(_bandCount);
        double defaultQ = EqBands.Q(_bandCount);
        for (int i = 0; i < freqs.Length; i++)
        {
            double g = gains is not null && i < gains.Length ? gains[i] : 0.0;
            double q = qs is not null && i < qs.Length && qs[i] > 0 ? qs[i] : defaultQ;
            Bands.Add(new EqBandViewModel(freqs[i], g, q, defaultQ, _onChanged));
        }
    }

    /// <summary>Replace the whole curve from a saved preset (band count, gains, Q and enable).</summary>
    public void Load(EqSettings settings)
    {
        if (settings is null) return;
        _bandCount = settings.Bands == 6 ? 6 : 3;
        _enabled = settings.Enabled;
        _lowPassEnabled = settings.LowPassEnabled;
        _lowPassHz = settings.LowPassHz is > 0 ? settings.LowPassHz : EqBands.LowPassDefaultHz;
        _lowPassSlope = settings.LowPassSlope == 1 ? 1 : 2;
        BuildBands(settings.GainsDb, settings.QValues);
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(BandCount));
        OnPropertyChanged(nameof(Is3Band));
        OnPropertyChanged(nameof(Is6Band));
        OnPropertyChanged(nameof(LowPassEnabled));
        OnPropertyChanged(nameof(LowPassHz));
        OnPropertyChanged(nameof(LowPassText));
        OnPropertyChanged(nameof(LowPassSlope));
        OnPropertyChanged(nameof(Is12dB));
        OnPropertyChanged(nameof(Is24dB));
        _onChanged();
    }

    /// <summary>Flatten every band to 0 dB and the default Q (single change notification).</summary>
    public void Reset()
    {
        foreach (var band in Bands)
        {
            band.SetGainSilently(0.0);
            band.SetQSilently(band.DefaultQ);
        }
        _lowPassEnabled = false;
        _lowPassHz = EqBands.LowPassDefaultHz;
        _lowPassSlope = 2;
        OnPropertyChanged(nameof(LowPassEnabled));
        OnPropertyChanged(nameof(LowPassHz));
        OnPropertyChanged(nameof(LowPassText));
        OnPropertyChanged(nameof(LowPassSlope));
        OnPropertyChanged(nameof(Is12dB));
        OnPropertyChanged(nameof(Is24dB));
        _onChanged();
    }

    public EqSettings ToSettings() => new()
    {
        Enabled = _enabled,
        Bands = _bandCount,
        GainsDb = Bands.Select(b => b.GainDb).ToArray(),
        QValues = Bands.Select(b => b.Q).ToArray(),
        LowPassEnabled = _lowPassEnabled,
        LowPassHz = _lowPassHz,
        LowPassSlope = _lowPassSlope,
    };
}
