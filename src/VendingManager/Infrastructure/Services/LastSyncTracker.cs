namespace VendingManager.Infrastructure.Services;

public class LastSyncTracker
{
    private DateTime? _lastSyncAt;
    private readonly object _lock = new();

    public DateTime? GetLastSync()
    {
        lock (_lock) { return _lastSyncAt; }
    }

    public void SetLastSync(DateTime when)
    {
        lock (_lock) { _lastSyncAt = when; }
    }
}
