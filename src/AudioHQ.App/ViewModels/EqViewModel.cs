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

    public ObservableCollection<EqBandViewModel> Bands { get; } = new();

    public EqViewModel(EqSettings? settings, Action onChanged)
    {
        _onChanged = onChanged;
        _enabled = settings?.Enabled ?? false;
        _bandCount = settings?.Bands == 6 ? 6 : 3;
        BuildBands(settings?.GainsDb);
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
            BuildBands(null);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Is3Band));
            OnPropertyChanged(nameof(Is6Band));
            _onChanged();
        }
    }

    // Radio-button bindings for the band-count selector.
    public bool Is3Band { get => _bandCount == 3; set { if (value) BandCount = 3; } }
    public bool Is6Band { get => _bandCount == 6; set { if (value) BandCount = 6; } }

    private void BuildBands(double[]? gains)
    {
        Bands.Clear();
        var freqs = EqBands.Frequencies(_bandCount);
        for (int i = 0; i < freqs.Length; i++)
        {
            double g = gains is not null && i < gains.Length ? gains[i] : 0.0;
            Bands.Add(new EqBandViewModel(freqs[i], g, _onChanged));
        }
    }

    /// <summary>Replace the whole curve from a saved preset (band count, gains and enable).</summary>
    public void Load(EqSettings settings)
    {
        if (settings is null) return;
        _bandCount = settings.Bands == 6 ? 6 : 3;
        _enabled = settings.Enabled;
        BuildBands(settings.GainsDb);
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(BandCount));
        OnPropertyChanged(nameof(Is3Band));
        OnPropertyChanged(nameof(Is6Band));
        _onChanged();
    }

    /// <summary>Flatten every band to 0 dB (single change notification).</summary>
    public void Reset()
    {
        foreach (var band in Bands) band.SetGainSilently(0.0);
        _onChanged();
    }

    public EqSettings ToSettings() => new()
    {
        Enabled = _enabled,
        Bands = _bandCount,
        GainsDb = Bands.Select(b => b.GainDb).ToArray(),
    };
}
