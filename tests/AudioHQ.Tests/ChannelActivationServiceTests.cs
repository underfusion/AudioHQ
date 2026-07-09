using System.Runtime.InteropServices;
using AudioHQ.App.ViewModels;

namespace AudioHQ.Tests;

public sealed class ChannelActivationServiceTests
{
    [Theory]
    [InlineData(unchecked((int)0x8889000A), "In use (exclusive)")]
    [InlineData(unchecked((int)0x88890008), "Format not supported")]
    [InlineData(unchecked((int)0x88890004), "Device unavailable")]
    public void StatusFor_MapsKnownComErrors(int hresult, string expected)
    {
        var ex = new COMException("driver failed", hresult);

        var status = ChannelActivationService.StatusFor(ex);

        Assert.Equal(expected, status);
    }

    [Fact]
    public void StatusFor_TruncatesLongGenericMessages()
    {
        var ex = new Exception(new string('x', 90));

        var status = ChannelActivationService.StatusFor(ex);

        Assert.Equal(80, status.Length);
    }
}
