namespace VendingManager.Infrastructure.Services;

/// <summary>The circuit-breaker's operating states.</summary>
public enum BreakerState
{
    /// <summary>Normal operation — polling proceeds on the regular cadence.</summary>
    Closed,

    /// <summary>At least one recent failure, below the trip threshold. Polling still proceeds
    /// (<see cref="PollingCircuitBreaker.CanAttempt"/> is true) but the caller is expected to use
    /// <see cref="PollingCircuitBreaker.DegradedBackoff"/> to slow its cadence.</summary>
    Degraded,

    /// <summary>Tripped — polling is skipped entirely until <see cref="PollingCircuitBreaker.CooldownUntil"/>
    /// elapses, at which point a single cautious probe (HalfOpen) is allowed.</summary>
    Open,

    /// <summary>Exactly one cautious poll cycle is allowed. Success fully resumes (-&gt; Closed);
    /// failure trips back to Open with an escalated cooldown.</summary>
    HalfOpen,

    /// <summary>Terminal hard-stop reached after repeated failed Open/HalfOpen round-trips.
    /// Requires an explicit manual reset (a fresh <see cref="PollingCircuitBreaker"/> constructed
    /// with a reset <see cref="BreakerSnapshot"/>) before polling can resume.</summary>
    Halted
}

/// <summary>The outcome of a single poll cycle, as classified by the caller
/// (<c>ScraperClient</c>/<c>SyncOrchestratorService</c> in later work units).</summary>
public enum PollOutcome
{
    /// <summary>Valid response with data — success.</summary>
    Ok,

    /// <summary>Valid response, zero rows — success (there simply was nothing new to sync).</summary>
    Empty,

    /// <summary>WAF/anti-automation signal detected (challenge page, login redirect, 403/429...) — failure.</summary>
    Blocked,

    /// <summary>Unclassified/infra failure (malformed response, exception, 5xx) — failure.</summary>
    Error,

    /// <summary>The call did not complete within the client's time budget — failure.</summary>
    Timeout
}

/// <summary>Persistable snapshot of a <see cref="PollingCircuitBreaker"/>'s state. Mirrors the four
/// <c>SyncMetadata</c> keys (<c>BreakerState</c>/<c>BreakerConsecutiveFailures</c>/
/// <c>BreakerCooldownUntil</c>/<c>BreakerOpenCycleCount</c>) that <c>LastSyncTracker</c> persists.</summary>
public sealed class BreakerSnapshot
{
    public BreakerState State { get; init; } = BreakerState.Closed;
    public int ConsecutiveFailures { get; init; }
    public DateTime? CooldownUntil { get; init; }
    public int OpenCycleCount { get; init; }
    public bool LoadedFromDb { get; init; }
}

/// <summary>
/// Pure, deterministic circuit-breaker state machine for the OurVend polling cycle.
/// No I/O, no DB access — clock (<c>nowUtc</c> parameters) and RNG are injected by the caller so
/// the logic is fully unit-testable, mirroring the <see cref="PollScheduler"/> convention.
/// State persistence (surviving app restarts) is owned by the caller via <see cref="ToSnapshot"/>
/// and the <c>initialSnapshot</c> constructor parameter — see <c>LastSyncTracker</c>.
///
/// States: Closed → Degraded → Open → HalfOpen → Halted.
/// - Closed: any failure (Blocked/Error/Timeout) → Degraded. Any success (Ok/Empty) stays Closed.
/// - Degraded: consecutive failures accumulate (counting the failure that caused Closed→Degraded);
///   reaching <see cref="_consecutiveFailureThreshold"/> (default 3) trips to Open. Any success →
///   back to Closed, counters reset.
/// - Open: polling is skipped (<see cref="CanAttempt"/> is false) until <see cref="CooldownUntil"/>
///   elapses, at which point <see cref="CanAttempt"/> both returns true AND advances the state to
///   HalfOpen — this is the single resumption path, never a silent reset to Closed.
/// - HalfOpen: exactly one cautious cycle. Success → Closed (full reset). Failure → back to Open
///   with an escalated (default: doubled, capped) cooldown, and <see cref="OpenCycleCount"/> is
///   incremented.
/// - Halted: reached once <see cref="OpenCycleCount"/> hits <c>maxOpenCycles</c> (default 5) failed
///   Open→HalfOpen→Open round-trips. Terminal — <see cref="CanAttempt"/> is always false and
///   <see cref="Record"/> is a no-op until manually reset.
/// </summary>
public class PollingCircuitBreaker
{
    public static readonly TimeSpan DefaultBaseInterval = PollScheduler.DefaultInterval;
    public static readonly TimeSpan DefaultMaxDegradedBackoff = TimeSpan.FromHours(6);
    public static readonly TimeSpan DefaultBaseOpenCooldown = TimeSpan.FromHours(24);
    public static readonly TimeSpan DefaultMaxOpenCooldown = TimeSpan.FromHours(168);
    public const int DefaultConsecutiveFailureThreshold = 3;
    public const int DefaultMaxOpenCycles = 5;

    private readonly Random _rng;
    private readonly TimeSpan _baseInterval;
    private readonly TimeSpan _maxDegradedBackoff;
    private readonly int _consecutiveFailureThreshold;
    private readonly TimeSpan _baseOpenCooldown;
    private readonly TimeSpan _maxOpenCooldown;
    private readonly int _maxOpenCycles;

    public BreakerState State { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public DateTime? CooldownUntil { get; private set; }
    public int OpenCycleCount { get; private set; }

    /// <summary>Backoff duration the caller should apply to its scheduling cadence while Degraded
    /// (interval×2, jittered, capped). Null outside the Degraded state.</summary>
    public TimeSpan? DegradedBackoff { get; private set; }

    public PollingCircuitBreaker(
        Random rng,
        BreakerSnapshot? initialSnapshot = null,
        TimeSpan? baseInterval = null,
        TimeSpan? maxDegradedBackoff = null,
        int consecutiveFailureThreshold = DefaultConsecutiveFailureThreshold,
        TimeSpan? baseOpenCooldown = null,
        TimeSpan? maxOpenCooldown = null,
        int maxOpenCycles = DefaultMaxOpenCycles)
    {
        ArgumentNullException.ThrowIfNull(rng);
        _rng = rng;
        _baseInterval = baseInterval ?? DefaultBaseInterval;
        _maxDegradedBackoff = maxDegradedBackoff ?? DefaultMaxDegradedBackoff;
        _consecutiveFailureThreshold = consecutiveFailureThreshold;
        _baseOpenCooldown = baseOpenCooldown ?? DefaultBaseOpenCooldown;
        _maxOpenCooldown = maxOpenCooldown ?? DefaultMaxOpenCooldown;
        _maxOpenCycles = maxOpenCycles;

        ValidateConfiguration();

        var snapshot = initialSnapshot ?? new BreakerSnapshot();
        State = snapshot.State;
        ConsecutiveFailures = snapshot.ConsecutiveFailures;
        CooldownUntil = snapshot.CooldownUntil;
        OpenCycleCount = snapshot.OpenCycleCount;

        ValidateSnapshot();
    }

    /// <summary>
    /// Enforces consistency between <see cref="State"/>, <see cref="CooldownUntil"/>, and related
    /// fields after snapshot loading. Prevents impossible combinations such as Open/HalfOpen with a
    /// null cooldown (which would deadlock <see cref="CanAttempt"/>) or Closed/Degraded/Halted with
    /// a stale cooldown.
    /// </summary>
    private void ValidateSnapshot()
    {
        switch (State)
        {
            case BreakerState.Open:
            case BreakerState.HalfOpen:
                if (!CooldownUntil.HasValue)
                {
                    // Null cooldown in Open/HalfOpen would cause CanAttempt() to return false
                    // forever — auto-correct to UtcNow so the cooldown is immediately expired
                    // and the next CanAttempt() call can transition to HalfOpen.
                    CooldownUntil = DateTime.UtcNow;
                }
                break;

            case BreakerState.Closed:
            case BreakerState.Degraded:
            case BreakerState.Halted:
                // Cooldown is meaningless in these states — coerce to null for consistency.
                CooldownUntil = null;
                break;
        }
    }

    private void ValidateConfiguration()
    {
        if (_baseInterval < TimeSpan.Zero)
        {
            throw new ArgumentException($"baseInterval must be non-negative (got {_baseInterval}).");
        }
        if (_maxDegradedBackoff < TimeSpan.Zero)
        {
            throw new ArgumentException($"maxDegradedBackoff must be non-negative (got {_maxDegradedBackoff}).");
        }
        if (_baseOpenCooldown < TimeSpan.Zero)
        {
            throw new ArgumentException($"baseOpenCooldown must be non-negative (got {_baseOpenCooldown}).");
        }
        if (_maxOpenCooldown < _baseOpenCooldown)
        {
            throw new ArgumentException(
                $"maxOpenCooldown ({_maxOpenCooldown}) must be >= baseOpenCooldown ({_baseOpenCooldown}).");
        }
        if (_consecutiveFailureThreshold < 1)
        {
            throw new ArgumentException($"consecutiveFailureThreshold must be >= 1 (got {_consecutiveFailureThreshold}).");
        }
        if (_maxOpenCycles < 1)
        {
            throw new ArgumentException($"maxOpenCycles must be >= 1 (got {_maxOpenCycles}).");
        }
    }

    /// <summary>
    /// Whether a poll cycle is allowed to fire right now. Closed/Degraded/HalfOpen always allow it.
    /// Halted never allows it. Open allows it ONLY once <paramref name="nowUtc"/> reaches
    /// <see cref="CooldownUntil"/> — and doing so is exactly the transition into the HalfOpen
    /// single-probe window (a side effect of this call, mirroring how a real circuit breaker's
    /// "allow request" check doubles as the half-open trigger).
    /// </summary>
    public bool CanAttempt(DateTime nowUtc)
    {
        switch (State)
        {
            case BreakerState.Closed:
            case BreakerState.Degraded:
            case BreakerState.HalfOpen:
                return true;

            case BreakerState.Open:
                if (CooldownUntil.HasValue && nowUtc >= CooldownUntil.Value)
                {
                    State = BreakerState.HalfOpen;
                    return true;
                }
                return false;

            case BreakerState.Halted:
            default:
                return false;
        }
    }

    /// <summary>Records the outcome of a poll cycle and applies the corresponding state transition.
    /// Must only be called after a preceding <see cref="CanAttempt"/> returned true for the same
    /// cycle. A no-op while Halted (terminal).</summary>
    /// <exception cref="InvalidOperationException">Thrown when the current state is
    /// <see cref="BreakerState.Open"/> — the caller must transition to HalfOpen via
    /// <see cref="CanAttempt"/> before recording an outcome.</exception>
    public void Record(PollOutcome outcome, DateTime nowUtc)
    {
        switch (State)
        {
            case BreakerState.Halted:
                return;
            case BreakerState.Open:
                throw new InvalidOperationException(
                    "Record() cannot be called while the breaker is Open. " +
                    "Call CanAttempt() first to transition to HalfOpen before recording an outcome.");
        }

        var success = outcome is PollOutcome.Ok or PollOutcome.Empty;

        if (State == BreakerState.HalfOpen)
        {
            RecordHalfOpenProbe(success, nowUtc);
            return;
        }

        // Closed or Degraded.
        if (success)
        {
            ConsecutiveFailures = 0;
            CooldownUntil = null;
            OpenCycleCount = 0;
            DegradedBackoff = null;
            State = BreakerState.Closed;
            return;
        }

        ConsecutiveFailures++;
        if (ConsecutiveFailures >= _consecutiveFailureThreshold)
        {
            State = BreakerState.Open;
            DegradedBackoff = null;
            CooldownUntil = nowUtc.Add(_baseOpenCooldown);
        }
        else
        {
            State = BreakerState.Degraded;
            DegradedBackoff = ComputeDegradedBackoff();
        }
    }

    private void RecordHalfOpenProbe(bool success, DateTime nowUtc)
    {
        if (success)
        {
            State = BreakerState.Closed;
            ConsecutiveFailures = 0;
            CooldownUntil = null;
            OpenCycleCount = 0;
            DegradedBackoff = null;
            return;
        }

        OpenCycleCount++;
        if (OpenCycleCount >= _maxOpenCycles)
        {
            State = BreakerState.Halted;
            CooldownUntil = null;
            return;
        }

        State = BreakerState.Open;
        CooldownUntil = nowUtc.Add(ComputeEscalatedOpenCooldown());
    }

    private TimeSpan ComputeDegradedBackoff()
    {
        var backoff = TimeSpan.FromTicks(_baseInterval.Ticks * 2);
        if (backoff > _maxDegradedBackoff)
        {
            backoff = _maxDegradedBackoff;
        }
        return ApplyJitter(backoff);
    }

    private TimeSpan ComputeEscalatedOpenCooldown()
    {
        // OpenCycleCount was already incremented by the caller before this runs, so the first
        // escalation (OpenCycleCount == 1) doubles the base cooldown, the second quadruples it, etc.
        var multiplier = Math.Pow(2, OpenCycleCount);
        var span = TimeSpan.FromTicks((long)(_baseOpenCooldown.Ticks * multiplier));
        return span > _maxOpenCooldown ? _maxOpenCooldown : span;
    }

    /// <summary>±10% uniform jitter, deterministic via the injected RNG (mirrors <see cref="PollScheduler"/>'s
    /// jitter approach).</summary>
    private TimeSpan ApplyJitter(TimeSpan value)
    {
        var jitterFraction = (_rng.NextDouble() * 0.2) - 0.1; // uniform in [-0.1, +0.1)
        var jitteredTicks = value.Ticks + (long)(value.Ticks * jitterFraction);
        return TimeSpan.FromTicks(Math.Max(0, jitteredTicks));
    }

    /// <summary>Exports the current state for persistence (see <c>LastSyncTracker</c>).</summary>
    public BreakerSnapshot ToSnapshot() => new()
    {
        State = State,
        ConsecutiveFailures = ConsecutiveFailures,
        CooldownUntil = CooldownUntil,
        OpenCycleCount = OpenCycleCount
    };
}
