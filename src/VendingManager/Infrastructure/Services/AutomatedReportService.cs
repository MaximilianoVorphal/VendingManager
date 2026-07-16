using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

namespace VendingManager.Infrastructure.Services;

public class AutomatedReportService : BackgroundService
{
    private readonly ILogger<AutomatedReportService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly LastSyncTracker _lastSyncTracker;

    // Polling API fields
    private readonly PollScheduler? _pollScheduler;
    private readonly PollingCircuitBreaker? _circuitBreaker;

    /// <summary>
    /// Whether the breaker state used to initialize <see cref="_circuitBreaker"/> was
    /// confirmed as loaded from the database. False when the DB was unavailable at
    /// construction time and a default-Closed fallback snapshot was used.
    /// </summary>
    private bool _breakerConfirmedFromDb;
    private readonly Random _rng = new();
    private readonly TimeSpan _windowStart;
    private readonly TimeSpan _windowEnd;

    public AutomatedReportService(
        ILogger<AutomatedReportService> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        LastSyncTracker lastSyncTracker)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _lastSyncTracker = lastSyncTracker;

        // Use indexer-based reads rather than GetValue<T> extension methods so null
        // IConfigurationSection from a dynamic mock (e.g. Mock.Of in tests) doesn't throw.
        var pollingSection = _configuration.GetSection("PollingApi");
        // pollingSection may be null under a dynamic mock (e.g. Mock.Of in tests).

        if (pollingSection != null)
        {
            int IntVal(string key, int fallback) =>
                int.TryParse(pollingSection[key], out var v) ? v : fallback;

            var intervalMinutes = IntVal("IntervalMinutes", 120);
            var jitterMaxMinutes = IntVal("JitterMaxMinutes", 30);
            var maxCycleMinutes = IntVal("MaxCycleMinutes", 3);

            _windowStart = TimeSpan.TryParse(pollingSection["WindowStart"], out var ws) ? ws : new(8, 0, 0);
            _windowEnd = TimeSpan.TryParse(pollingSection["WindowEnd"], out var we) ? we : new(21, 0, 0);

            _pollScheduler = new PollScheduler(
                interval: TimeSpan.FromMinutes(intervalMinutes),
                jitter: TimeSpan.FromMinutes(jitterMaxMinutes),
                maxCycleDuration: TimeSpan.FromMinutes(maxCycleMinutes),
                windowStart: _windowStart,
                windowEnd: _windowEnd);

            // Circuit-breaker config from appsettings
            var bfBackoff = TimeSpan.FromMinutes(IntVal("BaseIntervalForBackoffMinutes", 7));
            var degradedCap = TimeSpan.FromMinutes(IntVal("DegradedBackoffCapMinutes", 360));
            var failureThreshold = IntVal("ConsecutiveFailureThreshold", 3);
            var baseCooldown = TimeSpan.FromHours(IntVal("BaseOpenCooldownHours", 24));
                var maxCooldown = TimeSpan.FromHours(IntVal("MaxOpenCooldownHours", (int)PollingCircuitBreaker.DefaultMaxOpenCooldown.TotalHours));
            var maxCycles = IntVal("MaxOpenCycles", 5);

            // Load persisted breaker snapshot so state survives restart/redeploy
            var snapshot = _lastSyncTracker.GetBreakerSnapshot();
            _circuitBreaker = new PollingCircuitBreaker(
                _rng, snapshot,
                baseInterval: bfBackoff,
                maxDegradedBackoff: degradedCap,
                consecutiveFailureThreshold: failureThreshold,
                baseOpenCooldown: baseCooldown,
                maxOpenCooldown: maxCooldown,
                maxOpenCycles: maxCycles);

            _breakerConfirmedFromDb = snapshot.LoadedFromDb;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AutomatedReportService: Polling API mode — " +
            "starting jittered ~2h polling cycle (window {WindowStart:hh\\:mm}-{WindowEnd:hh\\:mm} CLT).",
            _windowStart, _windowEnd);
        await RunPollingLoopAsync(stoppingToken);
    }

    private async Task RunPollingLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;

            // If the breaker is Degraded and has computed a backoff duration, use it as
            // the interval override so the poll loop backs off instead of running at
            // full cadence. The degraded backoff already includes its own jitter from
            // PollingCircuitBreaker; the scheduler still applies window-boundary logic.
            TimeSpan? intervalOverride = null;
            if (_circuitBreaker is not null
                && _circuitBreaker.State == BreakerState.Degraded
                && _circuitBreaker.DegradedBackoff.HasValue)
            {
                intervalOverride = _circuitBreaker.DegradedBackoff.Value;
            }

            var nextFire = intervalOverride.HasValue
                ? _pollScheduler!.ComputeNextFire(now, _rng, intervalOverride.Value)
                : _pollScheduler!.ComputeNextFire(now, _rng);
            var delay = nextFire - now;

            if (delay > TimeSpan.Zero)
            {
                _logger.LogInformation(
                    "Next poll cycle at {NextFire:O} (in {DelayMinutes:F1} min)",
                    nextFire, delay.TotalMinutes);
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            await RunOnePollCycleAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Internal constructor for testability — injects scheduler, breaker, and window bounds
    /// directly so tests can control them without relying on config binding.
    /// </summary>
    internal AutomatedReportService(
        ILogger<AutomatedReportService> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        LastSyncTracker lastSyncTracker,
        PollScheduler pollScheduler,
        PollingCircuitBreaker circuitBreaker,
        TimeSpan windowStart,
        TimeSpan windowEnd)
        : this(logger, httpClientFactory, configuration, serviceProvider, lastSyncTracker)
    {
        _pollScheduler = pollScheduler;
        _circuitBreaker = circuitBreaker;
        _windowStart = windowStart;
        _windowEnd = windowEnd;
    }

    /// <summary>
    /// Runs a single poll cycle: window check → breaker check → sync → record.
    /// Internal for testability.
    /// </summary>
    internal async Task RunOnePollCycleAsync(CancellationToken stoppingToken = default)
    {
        stoppingToken.ThrowIfCancellationRequested();

        var now = DateTime.UtcNow;

        // 1. Business-hours window check (redundant if ComputeNextFire just ran,
        //    but the delay may have been zero and time has passed).
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, PollScheduler.ChileTimeZone);
        if (localNow.TimeOfDay < _windowStart || localNow.TimeOfDay >= _windowEnd)
        {
            _logger.LogInformation(
                "Outside business hours ({LocalTime:HH:mm} CLT) — skipping poll cycle", localNow);
            return;
        }

        // 2. Breaker check — includes Open→HalfOpen side-effect if cooldown has expired.
        if (!_circuitBreaker!.CanAttempt(now))
        {
            _logger.LogWarning(
                "Breaker is {State} — skipping poll cycle (cooldown until {CooldownUntil:O})",
                _circuitBreaker.State, _circuitBreaker.CooldownUntil);
            return;
        }

        _logger.LogInformation(
            "Poll cycle starting (breaker state={State}, failures={Failures})",
            _circuitBreaker.State, _circuitBreaker.ConsecutiveFailures);

        // 3. Execute sync via scoped ISyncOrchestratorService
        PollOutcome outcome;
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<ISyncOrchestratorService>();

            var desde = _lastSyncTracker.GetLastSync() ?? now.AddDays(-2);
            var hasta = now;

            var result = await syncService.SincronizarDesdePortalApi(desde, hasta, stoppingToken);

            outcome = result.Outcome switch
            {
                SyncOutcome.Ok => PollOutcome.Ok,
                SyncOutcome.Empty => PollOutcome.Empty,
                SyncOutcome.Blocked => PollOutcome.Blocked,
                SyncOutcome.Error => PollOutcome.Error,
                SyncOutcome.Timeout => PollOutcome.Timeout,
                _ => PollOutcome.Error
            };

            _logger.LogInformation(
                "Sync result: {SyncOutcome} — {Details}",
                result.Outcome, result.Details ?? result.Stats ?? "no details");
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown — do NOT record as an error. Let the exception
            // propagate so the hosting framework stops the background service.
            throw;
        }
        catch (Exception ex)
        {
            outcome = PollOutcome.Error;
            _logger.LogError(ex, "Unhandled exception during poll cycle");
        }

        // 4. Record outcome in breaker and persist state immediately so a restart
        //    during an active cooldown does not silently reset polling to Closed.
        _circuitBreaker.Record(outcome, DateTime.UtcNow);

        // If the breaker was initialised from a fallback snapshot (DB was down at startup),
        // retry the DB load now. If the DB is still unreachable we skip the persist so we
        // do NOT overwrite the real persisted state with a fallback-derived one.
        if (!_breakerConfirmedFromDb)
        {
            var recheck = _lastSyncTracker.GetBreakerSnapshot();

            if (recheck.LoadedFromDb)
            {
                // DB became available — confirm and persist the current breaker state.
                _breakerConfirmedFromDb = true;
                _lastSyncTracker.SaveBreakerSnapshot(_circuitBreaker.ToSnapshot());
            }
            else
            {
                // DB still unreachable — skip persist to avoid overwriting
                // with a potentially stale Closed state.
                _logger.LogWarning(
                    "Skipping SaveBreakerSnapshot — DB breaker state cannot be confirmed");
            }
        }
        else
        {
            _lastSyncTracker.SaveBreakerSnapshot(_circuitBreaker.ToSnapshot());
        }

        // 5. Advance last-sync timestamp only on success (ok/empty).
        //    Per LastSyncTracker.SetLastSync XML-doc: callers MUST only invoke for
        //    successful outcomes, so "time since last success" stays accurate through a degrade.
        if (outcome is PollOutcome.Ok or PollOutcome.Empty)
        {
            _lastSyncTracker.SetLastSync(DateTime.UtcNow);
            _logger.LogInformation(
                "Poll cycle completed: {Outcome} (breaker now {State}, failures={Failures})",
                outcome, _circuitBreaker.State, _circuitBreaker.ConsecutiveFailures);
        }
        else
        {
            _logger.LogWarning(
                "Poll cycle failed: {Outcome} (breaker now {State}, failures={Failures}, degradedBackoff={Backoff})",
                outcome, _circuitBreaker.State, _circuitBreaker.ConsecutiveFailures,
                _circuitBreaker.DegradedBackoff);
        }
    }
}

