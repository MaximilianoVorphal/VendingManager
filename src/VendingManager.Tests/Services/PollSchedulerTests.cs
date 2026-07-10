using System;
using FluentAssertions;
using VendingManager.Infrastructure.Services;
using Xunit;

namespace VendingManager.Tests.Services;

public class PollSchedulerTests
{
    private static readonly TimeZoneInfo Santiago = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");

    private static DateTime LocalToUtc(int year, int month, int day, int hour, int minute, int second = 0)
    {
        var local = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, Santiago);
    }

    private static DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Santiago);

    /// <summary>Deterministic RNG stub returning a fixed value in [0,1) for every draw.</summary>
    private sealed class FixedRandom : Random
    {
        private readonly double _value;
        public FixedRandom(double value) => _value = value;
        public override double NextDouble() => _value;
    }

    [Fact]
    public void ComputeNextFire_FiresWithinWindow_WhenIntervalElapsedMidDay()
    {
        // now = 2026-03-10 12:00 local (well inside window, non-DST-adjacent day)
        var nowUtc = LocalToUtc(2026, 3, 10, 12, 0);
        var scheduler = new PollScheduler();
        var rng = new FixedRandom(0.5); // midpoint draw -> zero jitter

        var nextFireUtc = scheduler.ComputeNextFire(nowUtc, rng);
        var nextFireLocal = ToLocal(nextFireUtc);

        nextFireLocal.Date.Should().Be(new DateTime(2026, 3, 10));
        nextFireLocal.TimeOfDay.Should().Be(new TimeSpan(14, 0, 0), "interval is 2h and jitter is zero at rng=0.5");
    }

    [Fact]
    public void ComputeNextFire_NearClosingBoundary_DefersInsteadOfFiringLate()
    {
        // now local 18:58 -> +2h interval = 20:58; +3min max-cycle = 21:01 > 21:00 -> must defer
        var nowUtc = LocalToUtc(2026, 3, 10, 18, 58);
        var scheduler = new PollScheduler();
        var rng = new FixedRandom(0.5); // zero jitter isolates the boundary logic

        var nextFireUtc = scheduler.ComputeNextFire(nowUtc, rng);
        var nextFireLocal = ToLocal(nextFireUtc);

        nextFireLocal.Date.Should().Be(new DateTime(2026, 3, 11),
            "the 20:58 candidate plus the 3min max-cycle budget would spill past 21:00, so it must defer");
        nextFireLocal.TimeOfDay.Should().BeGreaterThanOrEqualTo(new TimeSpan(8, 0, 0));
        nextFireLocal.TimeOfDay.Should().BeLessThan(new TimeSpan(8, 30, 0));
        nextFireLocal.TimeOfDay.Should().NotBe(new TimeSpan(8, 0, 0), "deferred fires must never land at exactly 08:00:00");
    }

    [Fact]
    public void ComputeNextFire_WhenNowIsAfterWindow_DefersToNextBusinessDay()
    {
        var nowUtc = LocalToUtc(2026, 3, 10, 22, 0); // after 21:00 close
        var scheduler = new PollScheduler();
        var rng = new FixedRandom(0.3);

        var nextFireUtc = scheduler.ComputeNextFire(nowUtc, rng);
        var nextFireLocal = ToLocal(nextFireUtc);

        nextFireLocal.Date.Should().Be(new DateTime(2026, 3, 11));
        nextFireLocal.TimeOfDay.Should().BeGreaterThanOrEqualTo(new TimeSpan(8, 0, 0));
        nextFireLocal.TimeOfDay.Should().BeLessThan(new TimeSpan(8, 30, 0));
        nextFireLocal.TimeOfDay.Should().NotBe(new TimeSpan(8, 0, 0));
    }

    [Fact]
    public void ComputeNextFire_WhenNowIsBeforeWindow_DefersToSameDayOpening()
    {
        // now 03:00 local; interval+zero-jitter lands at 05:00, still pre-window -> must open TODAY, not skip a day
        var nowUtc = LocalToUtc(2026, 3, 10, 3, 0);
        var scheduler = new PollScheduler();
        var rng = new FixedRandom(0.5);

        var nextFireUtc = scheduler.ComputeNextFire(nowUtc, rng);
        var nextFireLocal = ToLocal(nextFireUtc);

        nextFireLocal.Date.Should().Be(new DateTime(2026, 3, 10), "the window has not opened yet today");
        nextFireLocal.TimeOfDay.Should().BeGreaterThanOrEqualTo(new TimeSpan(8, 0, 0));
        nextFireLocal.TimeOfDay.Should().BeLessThan(new TimeSpan(8, 30, 0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.999999)]
    public void ComputeNextFire_ExtremeJitter_NeverPushesFireOutsideWindow(double extreme)
    {
        // now near the closing boundary so extreme jitter would normally spill past 21:00
        var nowUtc = LocalToUtc(2026, 3, 10, 18, 40);
        var scheduler = new PollScheduler();
        var rng = new FixedRandom(extreme);

        var nextFireUtc = scheduler.ComputeNextFire(nowUtc, rng);
        var nextFireLocal = ToLocal(nextFireUtc);

        nextFireLocal.TimeOfDay.Should().BeGreaterThanOrEqualTo(new TimeSpan(8, 0, 0));
        nextFireLocal.TimeOfDay.Should().BeLessThan(new TimeSpan(21, 0, 0));
        (nextFireLocal.TimeOfDay + TimeSpan.FromMinutes(3)).Should().BeLessThanOrEqualTo(new TimeSpan(21, 0, 0),
            "even with the max-cycle budget added, the fire must still complete before 21:00");
    }

    [Fact]
    public void ComputeNextFire_SameClockAndSeed_ProducesSameResult()
    {
        var nowUtc = LocalToUtc(2026, 3, 10, 12, 0);
        var scheduler = new PollScheduler();

        var first = scheduler.ComputeNextFire(nowUtc, new Random(42));
        var second = scheduler.ComputeNextFire(nowUtc, new Random(42));

        first.Should().Be(second);
    }

    [Fact]
    public void ComputeNextFire_AcrossAutumnDstTransition_UsesCorrectOffsetForNextDay()
    {
        // Discover the real DST-end (autumn, offset decreases) transition date from the runtime's own
        // tzdata instead of hardcoding one, so the test survives tzdata/law updates.
        var transitionDayLocal = FindDstTransitionDate(Santiago, offsetDecreases: true, year: 2026);
        var dayBefore = transitionDayLocal.AddDays(-1);

        var nowUtc = LocalToUtc(dayBefore.Year, dayBefore.Month, dayBefore.Day, 18, 58);
        var scheduler = new PollScheduler();
        var rng = new FixedRandom(0.5);

        Action act = () => scheduler.ComputeNextFire(nowUtc, rng);
        act.Should().NotThrow("DST transitions must not crash the scheduler");

        var nextFireUtc = scheduler.ComputeNextFire(nowUtc, rng);
        var nextFireLocal = ToLocal(nextFireUtc);

        nextFireLocal.Date.Should().Be(transitionDayLocal, "the deferred fire lands on the day after the DST transition");
        nextFireLocal.TimeOfDay.Should().BeGreaterThanOrEqualTo(new TimeSpan(8, 0, 0));
        nextFireLocal.TimeOfDay.Should().BeLessThan(new TimeSpan(8, 30, 0));

        // Sanity: the offset actually differs across the transition, proving this test exercises DST
        var offsetBefore = Santiago.GetUtcOffset(LocalToUtc(dayBefore.Year, dayBefore.Month, dayBefore.Day, 12, 0));
        var offsetAfter = Santiago.GetUtcOffset(nextFireUtc);
        offsetBefore.Should().NotBe(offsetAfter);
    }

    [Fact]
    public void ComputeNextFire_AcrossSpringDstTransition_DoesNotCrashAndStaysInWindow()
    {
        var transitionDayLocal = FindDstTransitionDate(Santiago, offsetDecreases: false, year: 2026);
        var dayBefore = transitionDayLocal.AddDays(-1);

        var nowUtc = LocalToUtc(dayBefore.Year, dayBefore.Month, dayBefore.Day, 20, 30);
        var scheduler = new PollScheduler();
        var rng = new FixedRandom(0.5);

        Action act = () => scheduler.ComputeNextFire(nowUtc, rng);
        act.Should().NotThrow("DST transitions must not crash the scheduler");

        var nextFireUtc = scheduler.ComputeNextFire(nowUtc, rng);
        var nextFireLocal = ToLocal(nextFireUtc);

        nextFireLocal.TimeOfDay.Should().BeGreaterThanOrEqualTo(new TimeSpan(8, 0, 0));
        nextFireLocal.TimeOfDay.Should().BeLessThan(new TimeSpan(21, 0, 0));
    }

    [Fact]
    public void ComputeNextFire_FromOptions_MatchesDefaultConstructorBehavior()
    {
        // The options-bound constructor (for future appsettings wiring, Unit 3) must produce the
        // same result as the default constructor when given the spec-default values.
        var nowUtc = LocalToUtc(2026, 3, 10, 12, 0);
        var defaultScheduler = new PollScheduler();
        var optionsScheduler = new PollScheduler(new PollSchedulerOptions());

        var expected = defaultScheduler.ComputeNextFire(nowUtc, new Random(7));
        var actual = optionsScheduler.ComputeNextFire(nowUtc, new Random(7));

        actual.Should().Be(expected);
    }

    // --- R3-001/R3-002: deferred fire must stay within a NON-default window, gated the same
    // way the organic-fire path is via FiresWithinWindow (windowEnd - maxCycleDuration). ---

    [Fact]
    public void ComputeNextFire_DeferredFire_NeverExceedsWindowEnd_WithSmallCustomWindow()
    {
        // Small window: 20:00-20:10, default 30-min defer offset, default 3-min max cycle.
        // Reproduces R3-001: an un-clamped defer offset (up to 30 min) would land the fire around
        // 20:15-20:30, well past WindowEnd (20:10) -- violating the "never fire outside window"
        // contract that the organic-fire path already enforces via FiresWithinWindow.
        var options = new PollSchedulerOptions
        {
            WindowStart = new TimeSpan(20, 0, 0),
            WindowEnd = new TimeSpan(20, 10, 0),
        };
        var scheduler = new PollScheduler(options);
        var rng = new FixedRandom(0.999999); // near-max draw -> largest possible offset

        // now well after the window closes -> defers to tomorrow's opening
        var nowUtc = LocalToUtc(2026, 3, 10, 22, 0);
        var nextFireUtc = scheduler.ComputeNextFire(nowUtc, rng);
        var nextFireLocal = ToLocal(nextFireUtc);

        nextFireLocal.Date.Should().Be(new DateTime(2026, 3, 11));
        nextFireLocal.TimeOfDay.Should().BeGreaterThanOrEqualTo(options.WindowStart);
        nextFireLocal.TimeOfDay.Should().BeLessThan(options.WindowEnd);
        (nextFireLocal.TimeOfDay + TimeSpan.FromMinutes(options.MaxCycleMinutes))
            .Should().BeLessThanOrEqualTo(options.WindowEnd,
                "even a deferred fire must complete its worst-case cycle before WindowEnd");
    }

    [Fact]
    public void ComputeNextFire_DeferredFire_WithSmallWindow_StillAvoidsExactWindowOpen()
    {
        // Same small window, but a near-zero RNG draw. The "never lands at exactly window-open"
        // property from the default-window tests must survive the R3-001 clamp.
        var options = new PollSchedulerOptions
        {
            WindowStart = new TimeSpan(20, 0, 0),
            WindowEnd = new TimeSpan(20, 10, 0),
        };
        var scheduler = new PollScheduler(options);
        var rng = new FixedRandom(0.0);

        var nowUtc = LocalToUtc(2026, 3, 10, 22, 0);
        var nextFireUtc = scheduler.ComputeNextFire(nowUtc, rng);
        var nextFireLocal = ToLocal(nextFireUtc);

        nextFireLocal.TimeOfDay.Should().NotBe(options.WindowStart,
            "deferred fires must never land at exactly window-open, even with a small window");
        (nextFireLocal.TimeOfDay + TimeSpan.FromMinutes(options.MaxCycleMinutes))
            .Should().BeLessThanOrEqualTo(options.WindowEnd);
    }

    [Fact]
    public void ComputeNextFire_DeferredFire_WithCustomDeferOffsetLargerThanWindowHeadroom_ClampsToWindow()
    {
        // DeferOffsetMaxMinutes (45) is larger than the window's headroom for a valid fire
        // (WindowEnd - WindowStart - MaxCycleMinutes = 15 - 5 = 10 minutes), so the effective
        // offset must be clamped down to the headroom, not the configured max.
        var options = new PollSchedulerOptions
        {
            WindowStart = new TimeSpan(9, 0, 0),
            WindowEnd = new TimeSpan(9, 15, 0),
            MaxCycleMinutes = 5,
            DeferOffsetMaxMinutes = 45,
        };
        var scheduler = new PollScheduler(options);
        var rng = new FixedRandom(0.999999);

        var nowUtc = LocalToUtc(2026, 3, 10, 12, 0); // after window closes -> defers to next day
        var nextFireUtc = scheduler.ComputeNextFire(nowUtc, rng);
        var nextFireLocal = ToLocal(nextFireUtc);

        nextFireLocal.TimeOfDay.Should().BeGreaterThanOrEqualTo(options.WindowStart);
        (nextFireLocal.TimeOfDay + TimeSpan.FromMinutes(options.MaxCycleMinutes))
            .Should().BeLessThanOrEqualTo(options.WindowEnd,
                "the configured DeferOffsetMaxMinutes exceeds the window's headroom, so it must be clamped");
    }

    // --- R3-003: fail-fast validation on misconfigured PollSchedulerOptions ---

    [Fact]
    public void Constructor_WindowStartNotBeforeWindowEnd_ThrowsArgumentException()
    {
        var options = new PollSchedulerOptions
        {
            WindowStart = new TimeSpan(21, 0, 0),
            WindowEnd = new TimeSpan(8, 0, 0),
        };

        Action act = () => new PollScheduler(options);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*WindowStart*WindowEnd*");
    }

    [Fact]
    public void Constructor_WindowTooSmallToHostOneValidFire_ThrowsArgumentException()
    {
        var options = new PollSchedulerOptions
        {
            WindowStart = new TimeSpan(20, 0, 0),
            WindowEnd = new TimeSpan(20, 2, 0), // 2 minutes, less than the 3-minute default max cycle
        };

        Action act = () => new PollScheduler(options);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*window*");
    }

    [Theory]
    [InlineData(-5, 25, 3, 30)]
    [InlineData(120, -1, 3, 30)]
    [InlineData(120, 25, -1, 30)]
    [InlineData(120, 25, 3, -1)]
    public void Constructor_NegativeNumericOption_ThrowsArgumentException(
        int intervalMinutes, int jitterMinutes, int maxCycleMinutes, int deferOffsetMaxMinutes)
    {
        var options = new PollSchedulerOptions
        {
            IntervalMinutes = intervalMinutes,
            JitterMinutes = jitterMinutes,
            MaxCycleMinutes = maxCycleMinutes,
            DeferOffsetMaxMinutes = deferOffsetMaxMinutes,
        };

        Action act = () => new PollScheduler(options);

        act.Should().Throw<ArgumentException>();
    }

    private static DateTime FindDstTransitionDate(TimeZoneInfo tz, bool offsetDecreases, int year)
    {
        var current = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(year, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var previousOffset = tz.GetUtcOffset(current);
        while (current < end)
        {
            current = current.AddHours(1);
            var offset = tz.GetUtcOffset(current);
            if (offset != previousOffset)
            {
                var decreased = offset < previousOffset;
                if (decreased == offsetDecreases)
                {
                    // The transition instant itself lands inside the repeated/skipped local hour
                    // (23:00-24:00 for a fall-back, 00:00-01:00 for a spring-forward). Add a
                    // buffer well past that ambiguity before flooring to a date, so we return the
                    // first calendar day that is fully governed by the new offset end-to-end.
                    return TimeZoneInfo.ConvertTimeFromUtc(current.AddHours(4), tz).Date;
                }
            }
            previousOffset = offset;
        }
        throw new InvalidOperationException($"No matching DST transition found for {year}");
    }
}
