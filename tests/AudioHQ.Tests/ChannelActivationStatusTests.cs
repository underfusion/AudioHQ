using System;
using System.Runtime.InteropServices;
using AudioHQ.App.ViewModels;
using AudioHQ.Core;

namespace AudioHQ.Tests;

/// <summary>
/// The short strings a failing channel shows on its strip. They are the only explanation the
/// user gets, so a wrong or vague one is a real bug.
/// </summary>
public sealed class ChannelActivationStatusTests
{
    private static COMException Com(uint hresult) => new("com failure", unchecked((int)hresult));

    [Fact]
    public void ExclusiveModeLock_SaysSoPlainly() =>
        Assert.Equal("In use (exclusive)", ChannelActivationService.StatusFor(Com(0x8889000A)));

    [Fact]
    public void DeviceUnavailable_SaysSoPlainly() =>
        Assert.Equal("Device unavailable", ChannelActivationService.StatusFor(Com(0x88890004)));

    [Fact]
    public void UnsupportedFormat_FallsBackToTheGenericFormatMessage() =>
        Assert.Equal("Format not supported", ChannelActivationService.StatusFor(Com(0x88890008)));

    [Fact]
    public void UnknownComError_ShowsTheHResultSoItCanBeLookedUp() =>
        Assert.Equal("Error 0x8889FFFF", ChannelActivationService.StatusFor(Com(0x8889FFFF)));

    [Fact]
    public void ChannelCountMismatch_NamesBothCountsInsteadOfSayingFormatNotSupported()
    {
        // The whole point: this wraps a 0x88890008, which on its own tells the user nothing.
        var ex = new ChannelCountMismatchException(2, 6, Com(0x88890008));

        Assert.Equal("Needs 6ch, source is 2ch", ChannelActivationService.StatusFor(ex));
    }

    [Fact]
    public void ChannelCountMismatch_WinsOverTheComExceptionItWraps()
    {
        // It must not fall through to the inner COMException's generic mapping.
        var ex = new ChannelCountMismatchException(2, 8, Com(0x88890008));

        Assert.NotEqual("Format not supported", ChannelActivationService.StatusFor(ex));
    }

    [Fact]
    public void EngineNotStarted_SaysTheSourceIsNotCapturing() =>
        Assert.Equal("Source not capturing",
            ChannelActivationService.StatusFor(new InvalidOperationException("Engine is not started.")));

    [Fact]
    public void UnexpectedError_IsTruncatedSoItFitsOnAStrip()
    {
        var status = ChannelActivationService.StatusFor(new Exception(new string('x', 200)));

        Assert.Equal(80, status.Length);
    }
}
