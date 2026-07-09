using AudioHQ.Core;

namespace AudioHQ.Tests;

public sealed class EqSettingsTests
{
    [Fact]
    public void Clone_CopiesMutableArrays()
    {
        var original = new EqSettings
        {
            Enabled = true,
            Bands = 6,
            GainsDb = new[] { 1.0, -2.0, 3.0, -4.0, 5.0, -6.0 },
            QValues = new[] { 0.7, 0.8, 0.9, 1.0, 1.1, 1.2 },
            LowPassEnabled = true,
            LowPassHz = 180.0,
            LowPassSlope = 1,
        };

        var clone = original.Clone();
        original.GainsDb[0] = 12.0;
        original.QValues![0] = 4.0;

        Assert.NotSame(original.GainsDb, clone.GainsDb);
        Assert.NotSame(original.QValues, clone.QValues);
        Assert.Equal(1.0, clone.GainsDb[0]);
        Assert.Equal(0.7, clone.QValues![0]);
        Assert.True(clone.LowPassEnabled);
        Assert.Equal(180.0, clone.LowPassHz);
        Assert.Equal(1, clone.LowPassSlope);
    }

    [Theory]
    [InlineData(3, new[] { 100f, 1000f, 8000f }, EqBands.Q3)]
    [InlineData(6, new[] { 80f, 200f, 500f, 1200f, 3000f, 8000f }, EqBands.Q6)]
    [InlineData(4, new[] { 100f, 1000f, 8000f }, EqBands.Q3)]
    public void EqBands_UsesOnlySupportedBandLayouts(int bands, float[] expectedFrequencies, float expectedQ)
    {
        Assert.Equal(expectedFrequencies, EqBands.Frequencies(bands));
        Assert.Equal(expectedQ, EqBands.Q(bands));
    }
}
