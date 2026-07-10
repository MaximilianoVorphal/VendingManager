using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VendingManager.Infrastructure.Data;

namespace VendingManager.Infrastructure.Services;

/// <summary>Overall health of the OurVend polling sync, derived from the persisted circuit-breaker
/// state plus staleness of the last successful sync. See <see cref="LastSyncTracker.GetHealthStatus"/>.</summary>
public enum SyncHealthStatus
{
    Healthy,
    Degraded
}

public class LastSyncTracker
{
    private const string LastSyncAtKey = "LastSyncAt";
    private const string BreakerStateKey = "BreakerState";
    private const string BreakerConsecutiveFailuresKey = "BreakerConsecutiveFailures";
    private const string BreakerCooldownUntilKey = "BreakerCooldownUntil";
    private const string BreakerOpenCycleCountKey = "BreakerOpenCycleCount";

    /// <summary>How stale <see cref="GetLastSync"/> may get, past which health is reported as
    /// Degraded even if the breaker itself is Closed (~2× the default 2h polling interval + a
    /// jitter/backoff margin).</summary>
    public static readonly TimeSpan StalenessThreshold = TimeSpan.FromHours(4.5);

    private readonly IServiceScopeFactory _scopeFactory;
    private DateTime? _lastSyncAt;
    private readonly object _lock = new();
    private bool _loaded;

    private BreakerSnapshot? _breakerSnapshot;
    private bool _breakerLoaded;

    public LastSyncTracker(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public DateTime? GetLastSync()
    {
        lock (_lock)
        {
            if (!_loaded)
            {
                var (value, success) = LoadFromDb();
                _lastSyncAt = value;
                // Only mark as loaded when the DB was reachable (success=true).
                // On failure, leave _loaded=false so the next call retries the DB.
                _loaded = success;
            }
            return _lastSyncAt;
        }
    }

    /// <summary>Records a successful sync timestamp. Callers MUST only invoke this for successful
    /// (Ok/Empty) poll-cycle outcomes — a Blocked/Error/Timeout outcome must NOT advance this value,
    /// so "time since last success" stays accurate through a degrade (see <see cref="GetHealthStatus"/>).</summary>
    public void SetLastSync(DateTime when)
    {
        lock (_lock)
        {
            _lastSyncAt = when;
        }
        SaveToDb(when);
    }

    /// <summary>Lazily loads the persisted <see cref="PollingCircuitBreaker"/> state. Defaults to a
    /// fresh Closed snapshot if nothing has been persisted yet (first run) or the DB is unavailable.</summary>
    public BreakerSnapshot GetBreakerSnapshot()
    {
        lock (_lock)
        {
            if (!_breakerLoaded)
            {
                var (value, success) = LoadBreakerFromDb();
                _breakerSnapshot = value;
                // Only mark as loaded when the DB was reachable (success=true).
                // On failure, leave _breakerLoaded=false so the next call retries the DB.
                _breakerLoaded = success;
            }
            return _breakerSnapshot!;
        }
    }

    /// <summary>Persists the breaker's current state. Must be called after every breaker transition
    /// so a restart/redeploy during an active Open/HalfOpen/Halted cooldown does not silently reset
    /// polling to Closed (see design: WAF-Signal Detection + Circuit Breaker → Persistence).</summary>
    public void SaveBreakerSnapshot(BreakerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_lock)
        {
            _breakerSnapshot = snapshot;
            _breakerLoaded = true;
        }
        SaveBreakerToDb(snapshot);
    }

    /// <summary>
    /// Derives overall sync health from the persisted breaker state plus staleness of the last
    /// successful sync. Healthy requires BOTH: the breaker is Closed AND a successful sync has
    /// happened within <see cref="StalenessThreshold"/>. Degraded otherwise — including when no
    /// successful sync has ever been recorded, when the breaker is Degraded/Open/HalfOpen/Halted,
    /// or when the last successful sync has simply aged out (covers a manually-reset breaker with
    /// no successful cycle yet).
    /// </summary>
    public SyncHealthStatus GetHealthStatus()
    {
        var breaker = GetBreakerSnapshot();
        if (breaker.State != BreakerState.Closed)
        {
            return SyncHealthStatus.Degraded;
        }

        var lastSync = GetLastSync();
        if (lastSync is null)
        {
            return SyncHealthStatus.Degraded;
        }

        return DateTime.UtcNow - lastSync.Value > StalenessThreshold
            ? SyncHealthStatus.Degraded
            : SyncHealthStatus.Healthy;
    }

    private (DateTime? Value, bool Success) LoadFromDb()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var meta = db.SyncMetadata.FirstOrDefault(m => m.Key == LastSyncAtKey);
            // RoundtripKind preserves the original DateTimeKind: a plain DateTime.TryParse silently
            // converts a "...Z" (UTC) ISO string to local machine time, which would corrupt
            // GetHealthStatus' staleness comparison against DateTime.UtcNow (see PollScheduler's
            // UTC-domain convention, which the polling flow follows end-to-end).
            if (meta?.Value != null
                && DateTime.TryParse(meta.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return (parsed, true);
            return (null, true);
        }
        catch
        {
            // DB unreachable — return null fallback, but signal failure so the caller
            // does not mark the value as "loaded" and retries on the next access.
        }
        return (null, false);
    }

    private void SaveToDb(DateTime when)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var meta = db.SyncMetadata.FirstOrDefault(m => m.Key == LastSyncAtKey);
            if (meta == null)
            {
                meta = new Core.Entities.SyncMetadata
                {
                    Key = LastSyncAtKey,
                    Value = when.ToString("o"),
                    UpdatedAt = DateTime.UtcNow
                };
                db.SyncMetadata.Add(meta);
            }
            else
            {
                meta.Value = when.ToString("o");
                meta.UpdatedAt = DateTime.UtcNow;
            }
            db.SaveChanges();
        }
        catch
        {
            // Si la DB no está disponible, el valor en memoria sigue válido
            // Se reintentará en el próximo SetLastSync
        }
    }

    private (BreakerSnapshot Value, bool Success) LoadBreakerFromDb()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var keys = new[] { BreakerStateKey, BreakerConsecutiveFailuresKey, BreakerCooldownUntilKey, BreakerOpenCycleCountKey };
            var rows = db.SyncMetadata.Where(m => keys.Contains(m.Key)).ToDictionary(m => m.Key, m => m.Value);

            var state = rows.TryGetValue(BreakerStateKey, out var stateRaw)
                && stateRaw != null
                && Enum.TryParse<BreakerState>(stateRaw, out var parsedState)
                    ? parsedState
                    : BreakerState.Closed;

            var consecutiveFailures = rows.TryGetValue(BreakerConsecutiveFailuresKey, out var failuresRaw)
                && int.TryParse(failuresRaw, out var parsedFailures)
                    ? parsedFailures
                    : 0;

            // BreakerCooldownUntil is always persisted in the UTC domain (see PollingCircuitBreaker,
            // whose nowUtc/CooldownUntil are UTC), so parsing MUST preserve Kind via RoundtripKind —
            // plain DateTime.TryParse silently converts a "...Z" ISO string to local machine time.
            DateTime? cooldownUntil = rows.TryGetValue(BreakerCooldownUntilKey, out var cooldownRaw)
                && !string.IsNullOrEmpty(cooldownRaw)
                && DateTime.TryParse(cooldownRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedCooldown)
                    ? parsedCooldown
                    : null;

            var openCycleCount = rows.TryGetValue(BreakerOpenCycleCountKey, out var cyclesRaw)
                && int.TryParse(cyclesRaw, out var parsedCycles)
                    ? parsedCycles
                    : 0;

            return (new BreakerSnapshot
            {
                State = state,
                ConsecutiveFailures = consecutiveFailures,
                CooldownUntil = cooldownUntil,
                OpenCycleCount = openCycleCount,
                LoadedFromDb = true
            }, true);
        }
        catch
        {
            // DB unreachable — return default Closed snapshot as fallback, but signal failure
            // so the caller does not mark the value as "loaded" and retries on the next access.
        }
        return (new BreakerSnapshot(), false);
    }

    private void SaveBreakerToDb(BreakerSnapshot snapshot)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTime.UtcNow;

            UpsertBreakerKey(db, BreakerStateKey, snapshot.State.ToString(), now);
            UpsertBreakerKey(db, BreakerConsecutiveFailuresKey, snapshot.ConsecutiveFailures.ToString(), now);
            UpsertBreakerKey(db, BreakerCooldownUntilKey, snapshot.CooldownUntil?.ToString("o"), now);
            UpsertBreakerKey(db, BreakerOpenCycleCountKey, snapshot.OpenCycleCount.ToString(), now);

            db.SaveChanges();
        }
        catch
        {
            // Si la DB no está disponible, el valor en memoria sigue válido
            // Se reintentará en el próximo SaveBreakerSnapshot
        }
    }

    private static void UpsertBreakerKey(ApplicationDbContext db, string key, string? value, DateTime updatedAt)
    {
        var meta = db.SyncMetadata.FirstOrDefault(m => m.Key == key);
        if (meta == null)
        {
            db.SyncMetadata.Add(new Core.Entities.SyncMetadata
            {
                Key = key,
                Value = value,
                UpdatedAt = updatedAt
            });
        }
        else
        {
            meta.Value = value;
            meta.UpdatedAt = updatedAt;
        }
    }
}
