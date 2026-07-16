using System;
using AudioHQ.Core;
using NAudio.Wave;

namespace AudioHQ.Tests;

/// <summary>
/// Guards the equalizer's flattened filter bank ([channel * bands + band]). The layout is an
/// indexing detail, so the tests pin the observable contract instead: pass-through when off,
/// per-channel isolation (a wrong index would leak one channel's delay line into another),
/// and in-place reconfigure keeping filter state.
/// </summary>
public sealed class EqualizerProviderTests
{
    private sealed class Fixed : ISampleProvider
    {
        private readonly float[] _frame;
        public Fixed(WaveFormat format, params float[] frame) { WaveFormat = format; _frame = frame; }
        public WaveFormat WaveFormat { get; }
        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++) buffer[offset + i] = _frame[i % _frame.Length];
            return count;
        }
    }

    private sealed class Tone : ISampleProvider
    {
        private readonly float _hz;
        private int _frame;
        public Tone(float hz) => _hz = hz;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                buffer[offset + i] = 0.2f * MathF.Sin(2f * MathF.PI * _hz * _frame / 48000f);
                if (i % 2 == 1) _frame++; // one phase step per stereo frame
            }
            return count;
        }
    }

    private static WaveFormat Stereo => WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    private static float Peak(float[] buffer, int from)
    {
        float peak = 0f;
        for (int i = from; i < buffer.Length; i++) peak = Math.Max(peak, Math.Abs(buffer[i]));
        return peak;
    }

    private static EqSettings ThreeBand(params double[] gains) =>
        new() { Enabled = true, Bands = 3, GainsDb = gains };

    [Fact]
    public void PassesThroughUntouchedWhileDisabled()
    {
        var eq = new EqualizerProvider(new Fixed(Stereo, 0.25f, -0.5f));
        var buffer = new float[8];

        int read = eq.Read(buffer, 0, buffer.Length);

        Assert.Equal(8, read);
        for (int i = 0; i < buffer.Length; i++)
            Assert.Equal(i % 2 == 0 ? 0.25f : -0.5f, buffer[i]);
    }

    [Fact]
    public void ReturnsToPassThroughWhenDisabledAgain()
    {
        var eq = new EqualizerProvider(new Fixed(Stereo, 0.25f, -0.5f));
        var buffer = new float[8];
        eq.Configure(ThreeBand(9.0, 9.0, 9.0));
        eq.Read(buffer, 0, buffer.Length);

        eq.Configure(null);
        eq.Read(buffer, 0, buffer.Length);

        for (int i = 0; i < buffer.Length; i++)
            Assert.Equal(i % 2 == 0 ? 0.25f : -0.5f, buffer[i]);
    }

    [Fact]
    public void KeepsChannelsIsolated()
    {
        // Channel 1 is fed pure silence. Its output must stay silent: if the flattened index
        // collided, channel 0's delay line would bleed into channel 1 here.
        var eq = new EqualizerProvider(new Fixed(Stereo, 0.8f, 0.0f));
        eq.Configure(ThreeBand(12.0, -12.0, 12.0));
        var buffer = new float[256];

        eq.Read(buffer, 0, buffer.Length);

        for (int i = 1; i < buffer.Length; i += 2)
            Assert.Equal(0.0f, buffer[i]);
        Assert.Contains(buffer, s => s != 0.0f); // channel 0 really was processed
    }

    [Fact]
    public void GivesEveryChannelItsOwnFilterState()
    {
        // Same signal on both channels must yield the same output on both channels - only
        // holds if each channel owns a distinct filter instance per band.
        var eq = new EqualizerProvider(new Fixed(Stereo, 0.4f, 0.4f));
        eq.Configure(ThreeBand(6.0, -3.0, 4.5));
        var buffer = new float[256];

        eq.Read(buffer, 0, buffer.Length);

        for (int i = 0; i < buffer.Length; i += 2)
            Assert.Equal(buffer[i], buffer[i + 1]);
    }

    [Fact]
    public void BoostingABandRaisesAToneAtItsCentreFrequency()
    {
        // 1 kHz is the middle band's centre; +12 dB there must lift the tone measurably.
        var flat = new EqualizerProvider(new Tone(1000f));
        var boosted = new EqualizerProvider(new Tone(1000f));
        boosted.Configure(ThreeBand(0.0, 12.0, 0.0));
        var flatBuffer = new float[4096];
        var boostedBuffer = new float[4096];

        flat.Read(flatBuffer, 0, flatBuffer.Length);
        boosted.Read(boostedBuffer, 0, boostedBuffer.Length);

        Assert.True(Peak(boostedBuffer, 2048) > Peak(flatBuffer, 2048) * 2f);
    }

    [Fact]
    public void CuttingABandLowersAToneAtItsCentreFrequency()
    {
        var flat = new EqualizerProvider(new Tone(1000f));
        var cut = new EqualizerProvider(new Tone(1000f));
        cut.Configure(ThreeBand(0.0, -24.0, 0.0));
        var flatBuffer = new float[4096];
        var cutBuffer = new float[4096];

        flat.Read(flatBuffer, 0, flatBuffer.Length);
        cut.Read(cutBuffer, 0, cutBuffer.Length);

        Assert.True(Peak(cutBuffer, 2048) < Peak(flatBuffer, 2048) * 0.5f);
    }

    [Fact]
    public void ReconfiguringSameTopologyKeepsFilterState()
    {
        // Re-applying identical settings must not rebuild the bank: a rebuild would reset the
        // delay lines and the next block would differ from an uninterrupted run.
        var steady = new EqualizerProvider(new Fixed(Stereo, 0.3f, -0.3f));
        var reconfigured = new EqualizerProvider(new Fixed(Stereo, 0.3f, -0.3f));
        steady.Configure(ThreeBand(6.0, -3.0, 4.5));
        reconfigured.Configure(ThreeBand(6.0, -3.0, 4.5));
        var steadyBuffer = new float[256];
        var reconfiguredBuffer = new float[256];
        steady.Read(steadyBuffer, 0, steadyBuffer.Length);
        reconfigured.Read(reconfiguredBuffer, 0, reconfiguredBuffer.Length);

        reconfigured.Configure(ThreeBand(6.0, -3.0, 4.5));
        steady.Read(steadyBuffer, 0, steadyBuffer.Length);
        reconfigured.Read(reconfiguredBuffer, 0, reconfiguredBuffer.Length);

        Assert.Equal(steadyBuffer, reconfiguredBuffer);
    }

    [Fact]
    public void SwitchingBandCountRebuildsTheBank()
    {
        var eq = new EqualizerProvider(new Fixed(Stereo, 0.3f, -0.3f));
        eq.Configure(ThreeBand(6.0, -3.0, 4.5));
        var buffer = new float[256];
        eq.Read(buffer, 0, buffer.Length);

        eq.Configure(new EqSettings
        {
            Enabled = true,
            Bands = 6,
            GainsDb = new[] { 1.0, 2.0, 3.0, -4.0, -5.0, 6.0 },
        });
        eq.Read(buffer, 0, buffer.Length);

        Assert.All(buffer, s => Assert.False(float.IsNaN(s)));
        Assert.Contains(buffer, s => s != 0.0f);
    }

    [Fact]
    public void LowPassCascadeAttenuatesContentAboveTheCutoff()
    {
        // Alternating +/- per frame is the Nyquist tone - far above a 300 Hz cutoff, so a
        // two-stage low-pass must crush it.
        var eq = new EqualizerProvider(new Fixed(Stereo, 0.5f, 0.5f, -0.5f, -0.5f));
        eq.Configure(new EqSettings
        {
            Enabled = true,
            Bands = 3,
            GainsDb = new[] { 0.0, 0.0, 0.0 },
            LowPassEnabled = true,
            LowPassHz = 300,
            LowPassSlope = 2,
        });
        var buffer = new float[2048];

        eq.Read(buffer, 0, buffer.Length);

        Assert.True(Math.Abs(buffer[^1]) < 0.05f);
    }
}
