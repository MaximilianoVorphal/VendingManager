namespace VendingManager.Infrastructure.Services;

/// <summary>
/// Configuration shape for <see cref="PollScheduler"/>, extracted so the window/jitter/backoff
/// values can be bound from <c>appsettings.json</c> in a later work unit (Unit 3 — integration
/// wiring). Values here default to the ones defined in the spec/design and mirror
/// <see cref="PollScheduler"/>'s own defaults; this class introduces no DI/appsettings wiring by
/// itself.
/// </summary>
public class PollSchedulerOptions
{
    /// <summary>Polling interval in minutes. Default: 120 (~2h).</summary>
    public int IntervalMinutes { get; set; } = (int)PollScheduler.DefaultInterval.TotalMinutes;

    /// <summary>Jitter bound in minutes, applied as ±JitterMinutes. Default: 25.</summary>
    public int JitterMinutes { get; set; } = (int)PollScheduler.DefaultJitter.TotalMinutes;

    /// <summary>Worst-case wall-clock budget for one poll cycle, in minutes. Default: 3.</summary>
    public int MaxCycleMinutes { get; set; } = (int)PollScheduler.DefaultMaxCycleDuration.TotalMinutes;

    /// <summary>Business window opening time (local, America/Santiago). Default: 08:00.</summary>
    public TimeSpan WindowStart { get; set; } = PollScheduler.DefaultWindowStart;

    /// <summary>Business window closing time (local, America/Santiago). Default: 21:00.</summary>
    public TimeSpan WindowEnd { get; set; } = PollScheduler.DefaultWindowEnd;

    /// <summary>Upper bound, in minutes, of the uniform random offset added to a deferred fire so
    /// it never lands at exactly the window opening instant. Default: 30.</summary>
    public int DeferOffsetMaxMinutes { get; set; } = (int)PollScheduler.DefaultDeferOffsetMax.TotalMinutes;

    /// <summary>IANA time zone id for the business window. Default: America/Santiago.</summary>
    public string TimeZoneId { get; set; } = "America/Santiago";
}
