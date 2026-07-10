namespace VendingManager.Infrastructure.Services;

/// <summary>
/// Pure, deterministic computation of the next OurVend polling fire time.
/// No I/O, no DB access, no live portal calls — clock (<c>nowUtc</c>) and RNG are
/// injected by the caller so the logic is fully unit-testable.
/// </summary>
public class PollScheduler
{
    /// <summary>The IANA time zone the business window is defined in. DST-safe — resolved via
    /// <see cref="TimeZoneInfo"/>, never inferred from the container/local time zone.</summary>
    public static readonly TimeZoneInfo ChileTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");

    public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(2);
    public static readonly TimeSpan DefaultJitter = TimeSpan.FromMinutes(25);

    /// <summary>Worst-case wall-clock budget for one full poll cycle (browser launch, login,
    /// navigation, fetch, classification). Used ONLY for the closing-boundary check, never as a
    /// live measurement.</summary>
    public static readonly TimeSpan DefaultMaxCycleDuration = TimeSpan.FromMinutes(3);

    public static readonly TimeSpan DefaultWindowStart = new(8, 0, 0);
    public static readonly TimeSpan DefaultWindowEnd = new(21, 0, 0);

    /// <summary>Upper bound of the uniform random offset added to a deferred fire so it never
    /// lands at exactly the window opening instant.</summary>
    public static readonly TimeSpan DefaultDeferOffsetMax = TimeSpan.FromMinutes(30);

    private readonly TimeZoneInfo _timeZone;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _jitter;
    private readonly TimeSpan _maxCycleDuration;
    private readonly TimeSpan _windowStart;
    private readonly TimeSpan _windowEnd;
    private readonly TimeSpan _deferOffsetMax;

    public PollScheduler(
        TimeZoneInfo? timeZone = null,
        TimeSpan? interval = null,
        TimeSpan? jitter = null,
        TimeSpan? maxCycleDuration = null,
        TimeSpan? windowStart = null,
        TimeSpan? windowEnd = null,
        TimeSpan? deferOffsetMax = null)
    {
        _timeZone = timeZone ?? ChileTimeZone;
        _interval = interval ?? DefaultInterval;
        _jitter = jitter ?? DefaultJitter;
        _maxCycleDuration = maxCycleDuration ?? DefaultMaxCycleDuration;
        _windowStart = windowStart ?? DefaultWindowStart;
        _windowEnd = windowEnd ?? DefaultWindowEnd;
        _deferOffsetMax = deferOffsetMax ?? DefaultDeferOffsetMax;

        ValidateConfiguration();
    }

    /// <summary>
    /// Fail-fast validation of the resolved window/interval/jitter/backoff configuration.
    /// Runs for both the default constructor and the <see cref="PollSchedulerOptions"/>-bound
    /// overload, since the latter delegates into this one. Each check names the offending field
    /// so a misconfigured <c>appsettings.json</c> entry is easy to diagnose.
    /// </summary>
    private void ValidateConfiguration()
    {
        if (_windowStart >= _windowEnd)
        {
            throw new ArgumentException(
                $"WindowStart ({_windowStart}) must be earlier than WindowEnd ({_windowEnd}).");
        }
        if (_interval < TimeSpan.Zero)
        {
            throw new ArgumentException($"IntervalMinutes must be non-negative (got {_interval}).");
        }
        if (_jitter < TimeSpan.Zero)
        {
            throw new ArgumentException($"JitterMinutes must be non-negative (got {_jitter}).");
        }
        if (_maxCycleDuration < TimeSpan.Zero)
        {
            throw new ArgumentException($"MaxCycleMinutes must be non-negative (got {_maxCycleDuration}).");
        }
        if (_deferOffsetMax < TimeSpan.Zero)
        {
            throw new ArgumentException($"DeferOffsetMaxMinutes must be non-negative (got {_deferOffsetMax}).");
        }
        if (_windowEnd - _windowStart < _maxCycleDuration)
        {
            throw new ArgumentException(
                $"The window [{_windowStart}, {_windowEnd}) is too small to host a single poll cycle " +
                $"(MaxCycleMinutes={_maxCycleDuration.TotalMinutes}m). WindowEnd - WindowStart must be >= MaxCycleMinutes.");
        }
    }

    /// <summary>Builds a scheduler from a bindable <see cref="PollSchedulerOptions"/> instance
    /// (e.g. loaded from <c>appsettings.json</c> in a later work unit).</summary>
    public PollScheduler(PollSchedulerOptions options)
        : this(
            timeZone: TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId),
            interval: TimeSpan.FromMinutes(options.IntervalMinutes),
            jitter: TimeSpan.FromMinutes(options.JitterMinutes),
            maxCycleDuration: TimeSpan.FromMinutes(options.MaxCycleMinutes),
            windowStart: options.WindowStart,
            windowEnd: options.WindowEnd,
            deferOffsetMax: TimeSpan.FromMinutes(options.DeferOffsetMaxMinutes))
    {
    }

    /// <summary>
    /// Computes the next UTC fire instant given the current instant and an injected RNG.
    /// Elapsed-time arithmetic (interval, jitter, max-cycle) is always done in the UTC domain;
    /// only the window-boundary check and the deferred-fire construction touch local wall-clock
    /// time, and that conversion always goes through <see cref="TimeZoneInfo"/> so DST transitions
    /// (which in America/Santiago always land at local midnight, outside the business window) are
    /// resolved correctly for whichever calendar date is involved.
    /// </summary>
    public DateTime ComputeNextFire(DateTime nowUtc, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("nowUtc must be expressed in UTC (DateTimeKind.Utc).", nameof(nowUtc));
        }

        var jitterOffset = NextJitter(rng);
        var candidateUtc = nowUtc.Add(_interval).Add(jitterOffset);
        var candidateLocal = TimeZoneInfo.ConvertTimeFromUtc(candidateUtc, _timeZone);

        if (FiresWithinWindow(candidateLocal))
        {
            return candidateUtc;
        }

        var deferredLocal = DeferToNextOpening(candidateLocal, rng);
        return TimeZoneInfo.ConvertTimeToUtc(deferredLocal, _timeZone);
    }

    private bool FiresWithinWindow(DateTime candidateLocal)
    {
        var timeOfDay = candidateLocal.TimeOfDay;
        if (timeOfDay < _windowStart) return false;
        if (timeOfDay >= _windowEnd) return false;
        // The whole cycle (including its worst-case duration) must complete before the window closes.
        if (timeOfDay + _maxCycleDuration > _windowEnd) return false;
        return true;
    }

    private DateTime DeferToNextOpening(DateTime candidateLocal, Random rng)
    {
        // If today's window hasn't opened yet, defer to TODAY's opening rather than skipping a day.
        // Otherwise (window already closed or closing) defer to tomorrow's opening.
        var targetDate = candidateLocal.TimeOfDay < _windowStart
            ? candidateLocal.Date
            : candidateLocal.Date.AddDays(1);

        var offset = NextDeferOffset(rng);
        return targetDate.Add(_windowStart).Add(offset);
    }

    private TimeSpan NextJitter(Random rng)
    {
        var boundSeconds = _jitter.TotalSeconds;
        var raw = (rng.NextDouble() * 2 * boundSeconds) - boundSeconds; // uniform in [-bound, +bound)
        return TimeSpan.FromSeconds(raw);
    }

    private TimeSpan NextDeferOffset(Random rng)
    {
        // The deferred fire must honor the same "whole cycle completes before window close"
        // invariant as the organic-fire path (see FiresWithinWindow): windowStart + offset +
        // maxCycleDuration must never exceed windowEnd. ValidateConfiguration guarantees
        // headroom is never negative, but with a small custom window it can be smaller than the
        // configured DeferOffsetMax, so clamp to whichever is tighter.
        var headroom = _windowEnd - _windowStart - _maxCycleDuration;
        var effectiveMax = _deferOffsetMax < headroom ? _deferOffsetMax : headroom;
        var maxSeconds = effectiveMax.TotalSeconds;

        if (maxSeconds <= 1)
        {
            // Window too tight to also guarantee a strictly-positive (>= 1s) offset; clamp to
            // the largest offset that still keeps the fire inside the window rather than firing
            // outside it. This only happens for a pathologically tight window/max-cycle pairing.
            return TimeSpan.FromSeconds(Math.Max(0, maxSeconds));
        }

        // Guarantee a strictly-positive offset (>= 1s) so a deferred fire never lands at exactly
        // the window opening instant (e.g. 08:00:00.000).
        var seconds = 1 + (rng.NextDouble() * (maxSeconds - 1));
        return TimeSpan.FromSeconds(seconds);
    }
}
