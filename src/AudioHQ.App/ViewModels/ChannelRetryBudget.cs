namespace AudioHQ.App.ViewModels;

/// <summary>Budgets watchdog auto-reactivation attempts for a persistently failing channel.</summary>
public sealed class ChannelRetryBudget
{
    private const int MaxAutoRetries = 3;
    private int _remaining = MaxAutoRetries;

    public bool TryConsume(bool force)
    {
        if (force) return true;
        if (_remaining <= 0) return false;
        _remaining--;
        return true;
    }

    public void Reset() => _remaining = MaxAutoRetries;
}
