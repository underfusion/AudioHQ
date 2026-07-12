using AudioHQ.App.ViewModels;

namespace AudioHQ.Tests;

public sealed class MixerStatusViewModelTests
{
    [Fact]
    public void Set_UpdatesMessageAndSeverity()
    {
        var status = new MixerStatusViewModel();

        status.Set("Source switched.", isError: false);

        Assert.Equal("Source switched.", status.Message);
        Assert.False(status.IsError);
    }

    [Fact]
    public void Clear_HidesMessageAndResetsSeverity()
    {
        var status = new MixerStatusViewModel();
        status.Set("Cannot capture source.", isError: true);

        status.Clear();

        Assert.Equal("", status.Message);
        Assert.False(status.IsError);
    }
}
