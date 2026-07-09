using System;
using AudioHQ.Core;

namespace AudioHQ.App.ViewModels;

/// <summary>One equalizer band: a fixed centre frequency with a user-set gain in dB.</summary>
public sealed class EqBandViewModel : ViewModelBase
{
    private readonly Action _onChanged;
    private double _gainDb;
    private double _q;

    public float Frequency { get; }

    /// <summary>Short axis label shown under the fader (e.g. "100", "1.2k").</summary>
    public string Label { get; }

    /// <summary>The band-count default Q (the knob's reset/double-click target).</summary>
    public double DefaultQ { get; }

    public EqBandViewModel(float frequency, double gainDb, double q, double defaultQ, Action onChanged)
    {
        Frequency = frequency;
        Label = EqBands.Label(frequency);
        _gainDb = gainDb;
        DefaultQ = defaultQ;
        _q = Math.Clamp(q, EqBands.QMin, EqBands.QMax);
        _onChanged = onChanged;
    }

    /// <summary>Band gain in dB, clamped to the EQ range.</summary>
    public double GainDb
    {
        get => _gainDb;
        set
        {
            double v = Math.Clamp(value, -EqBands.MaxCutDb, EqBands.MaxBoostDb);
            if (Math.Abs(_gainDb - v) < 0.001) return;
            _gainDb = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GainText));
            _onChanged();
        }
    }

    public string GainText => $"{(_gainDb >= 0 ? "+" : "")}{_gainDb:0.0} dB";

    /// <summary>Bell width (Q) for this band: low = wide/round, high = narrow/sharp.</summary>
    public double Q
    {
        get => _q;
        set
        {
            double v = Math.Clamp(value, EqBands.QMin, EqBands.QMax);
            if (Math.Abs(_q - v) < 0.001) return;
            _q = v;
            OnPropertyChanged();
            _onChanged();
        }
    }

    /// <summary>Set the gain without invoking the change callback (used by Reset, which fires once).</summary>
    public void SetGainSilently(double value)
    {
        _gainDb = Math.Clamp(value, -EqBands.MaxCutDb, EqBands.MaxBoostDb);
        OnPropertyChanged(nameof(GainDb));
        OnPropertyChanged(nameof(GainText));
    }

    /// <summary>Reset the bell width to the band-count default without firing the change callback.</summary>
    public void SetQSilently(double value)
    {
        _q = Math.Clamp(value, EqBands.QMin, EqBands.QMax);
        OnPropertyChanged(nameof(Q));
    }
}
