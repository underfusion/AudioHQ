using AudioHQ.App.ViewModels;

namespace AudioHQ.Tests;

public sealed class ChannelRetryBudgetTests
{
    [Fact]
    public void TryConsume_AllowsThreeNormalAttempts()
    {
        var budget = new ChannelRetryBudget();

        Assert.True(budget.TryConsume(force: false));
        Assert.True(budget.TryConsume(force: false));
        Assert.True(budget.TryConsume(force: false));
        Assert.False(budget.TryConsume(force: false));
    }

    [Fact]
    public void Reset_RestoresNormalAttempts()
    {
        var budget = new ChannelRetryBudget();
        budget.TryConsume(force: false);
        budget.TryConsume(force: false);
        budget.TryConsume(force: false);
        budget.TryConsume(force: false);

        budget.Reset();

        Assert.True(budget.TryConsume(force: false));
    }

    [Fact]
    public void Force_DoesNotConsumeNormalBudget()
    {
        var budget = new ChannelRetryBudget();

        Assert.True(budget.TryConsume(force: true));
        Assert.True(budget.TryConsume(force: true));
        Assert.True(budget.TryConsume(force: false));
        Assert.True(budget.TryConsume(force: false));
        Assert.True(budget.TryConsume(force: false));
        Assert.False(budget.TryConsume(force: false));
    }
}
