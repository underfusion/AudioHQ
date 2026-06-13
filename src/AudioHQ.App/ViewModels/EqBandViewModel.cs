using System;
using AudioHQ.Core;

namespace AudioHQ.App.ViewModels;

/// <summary>One equalizer band: a fixed centre frequency with a user-set gain in dB.</summary>
public sealed class EqBandViewModel : ViewModelBase
{
    private readonly Action _onChanged;
    private double _gainDb;

    public float Frequency { get; }

    /// <summary>Short axis label shown under the fader (e.g. "100", "1.2k").</summary>
    public string Label { get; }

    public EqBandViewModel(float frequency, double gainDb, Action onChanged)
    {
        Frequency = frequency;
        Label = EqBands.Label(frequency);
        _gainDb = gainDb;
        _onChanged = onChanged;
    }

    /// <summary>Band gain in dB, clamped to the EQ range.</summary>
    public double GainDb
    {
        get => _gainDb;
        set
        {
            double v = Math.Clamp(value, -EqBands.MaxGainDb, EqBands.MaxGainDb);
            if (Math.Abs(_gainDb - v) < 0.001) return;
            _gainDb = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GainText));
            _onChanged();
        }
    }

    public string GainText => $"{(_gainDb >= 0 ? "+" : "")}{_gainDb:0.0} dB";

    /// <summary>Set the gain without invoking the change callback (used by Reset, which fires once).</summary>
    public void SetGainSilently(double value)
    {
        _gainDb = Math.Clamp(value, -EqBands.MaxGainDb, EqBands.MaxGainDb);
        OnPropertyChanged(nameof(GainDb));
        OnPropertyChanged(nameof(GainText));
    }
}
