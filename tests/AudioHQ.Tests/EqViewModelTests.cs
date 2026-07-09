using AudioHQ.App.ViewModels;
using AudioHQ.Core;

namespace AudioHQ.Tests;

public sealed class EqViewModelTests
{
    [Fact]
    public void Constructor_NormalizesUnsupportedSettings()
    {
        var changes = 0;
        var vm = new EqViewModel(new EqSettings
        {
            Enabled = true,
            Bands = 5,
            GainsDb = new[] { 4.0 },
            QValues = new[] { -1.0 },
            LowPassEnabled = true,
            LowPassHz = -10.0,
            LowPassSlope = 99,
        }, () => changes++);

        Assert.True(vm.Enabled);
        Assert.Equal(3, vm.BandCount);
        Assert.Equal(3, vm.Bands.Count);
        Assert.Equal(4.0, vm.Bands[0].GainDb);
        Assert.Equal(EqBands.Q3, vm.Bands[0].Q);
        Assert.Equal(EqBands.LowPassDefaultHz, vm.LowPassHz);
        Assert.Equal(2, vm.LowPassSlope);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void LowPassHz_ClampsToSupportedRange()
    {
        var changes = 0;
        var vm = new EqViewModel(null, () => changes++);

        vm.LowPassHz = 1.0;
        Assert.Equal(EqBands.LowPassMinHz, vm.LowPassHz);

        vm.LowPassHz = 10_000.0;
        Assert.Equal(EqBands.LowPassMaxHz, vm.LowPassHz);

        Assert.Equal(2, changes);
    }

    [Fact]
    public void Reset_FlattensBandsAndLowPassWithOneChange()
    {
        var changes = 0;
        var vm = new EqViewModel(new EqSettings
        {
            Bands = 6,
            GainsDb = new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 },
            QValues = new[] { 1.2, 1.3, 1.4, 1.5, 1.6, 1.7 },
            LowPassEnabled = true,
            LowPassHz = 220.0,
            LowPassSlope = 1,
        }, () => changes++);

        vm.Reset();

        Assert.All(vm.Bands, band =>
        {
            Assert.Equal(0.0, band.GainDb);
            Assert.Equal(band.DefaultQ, band.Q);
        });
        Assert.False(vm.LowPassEnabled);
        Assert.Equal(EqBands.LowPassDefaultHz, vm.LowPassHz);
        Assert.Equal(2, vm.LowPassSlope);
        Assert.Equal(1, changes);
    }
}
