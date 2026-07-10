using System;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using VendingManager.Infrastructure.Data;
using VendingManager.Infrastructure.Services;
using Xunit;

namespace VendingManager.Tests.Services;

public class PollingCircuitBreakerTests
{
    /// <summary>Deterministic RNG stub returning a fixed value in [0,1) for every draw.</summary>
    private sealed class FixedRandom : Random
    {
        private readonly double _value;
        public FixedRandom(double value) => _value = value;
        public override double NextDouble() => _value;
    }

    private static readonly DateTime BaseNowUtc = new(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc);

    private static PollingCircuitBreaker CreateBreaker(Random? rng = null, BreakerSnapshot? initial = null) =>
        new(rng ?? new FixedRandom(0.5), initialSnapshot: initial);

    [Fact]
    public void InitialState_IsClosed_WithZeroFailures()
    {
        var breaker = CreateBreaker();

        breaker.State.Should().Be(BreakerState.Closed);
        breaker.ConsecutiveFailures.Should().Be(0);
        breaker.CooldownUntil.Should().BeNull();
        breaker.OpenCycleCount.Should().Be(0);
    }

    [Theory]
    [InlineData(PollOutcome.Ok)]
    [InlineData(PollOutcome.Empty)]
    public void Record_SuccessFromClosed_StaysClosed(PollOutcome outcome)
    {
        var breaker = CreateBreaker();

        breaker.Record(outcome, BaseNowUtc);

        breaker.State.Should().Be(BreakerState.Closed);
        breaker.ConsecutiveFailures.Should().Be(0);
    }

    [Theory]
    [InlineData(PollOutcome.Blocked)]
    [InlineData(PollOutcome.Error)]
    [InlineData(PollOutcome.Timeout)]
    public void Record_FailureFromClosed_MovesToDegraded(PollOutcome outcome)
    {
        var breaker = CreateBreaker();

        breaker.Record(outcome, BaseNowUtc);

        breaker.State.Should().Be(BreakerState.Degraded);
        breaker.ConsecutiveFailures.Should().Be(1);
        breaker.DegradedBackoff.Should().NotBeNull();
    }

    [Fact]
    public void Record_SuccessFromDegraded_ReturnsToClosed_AndResetsCounter()
    {
        var breaker = CreateBreaker();
        breaker.Record(PollOutcome.Error, BaseNowUtc);
        breaker.State.Should().Be(BreakerState.Degraded);

        breaker.Record(PollOutcome.Ok, BaseNowUtc.AddMinutes(5));

        breaker.State.Should().Be(BreakerState.Closed);
        breaker.ConsecutiveFailures.Should().Be(0);
        breaker.DegradedBackoff.Should().BeNull();
    }

    [Fact]
    public void Record_ThreeConsecutiveFailures_MovesToOpen_WithCooldownSet()
    {
        var breaker = CreateBreaker();

        breaker.Record(PollOutcome.Error, BaseNowUtc);
        breaker.State.Should().Be(BreakerState.Degraded, "1st failure only degrades");

        breaker.Record(PollOutcome.Timeout, BaseNowUtc.AddMinutes(1));
        breaker.State.Should().Be(BreakerState.Degraded, "2nd failure still degraded, below threshold of 3");

        breaker.Record(PollOutcome.Blocked, BaseNowUtc.AddMinutes(2));

        breaker.State.Should().Be(BreakerState.Open, "3rd consecutive failure trips the breaker open");
        breaker.ConsecutiveFailures.Should().Be(3);
        breaker.CooldownUntil.Should().NotBeNull();
        breaker.CooldownUntil!.Value.Should().BeAfter(BaseNowUtc.AddMinutes(2));
    }

    [Fact]
    public void CanAttempt_WhenOpen_BeforeCooldownElapses_ReturnsFalse()
    {
        var breaker = OpenBreaker();

        breaker.CanAttempt(breaker.CooldownUntil!.Value.AddSeconds(-1)).Should().BeFalse();
    }

    [Fact]
    public void CanAttempt_WhenOpen_AfterCooldownElapses_ReturnsTrue_AndAdvancesToHalfOpen()
    {
        var breaker = OpenBreaker();
        var afterCooldown = breaker.CooldownUntil!.Value.AddSeconds(1);

        var canAttempt = breaker.CanAttempt(afterCooldown);

        canAttempt.Should().BeTrue();
        breaker.State.Should().Be(BreakerState.HalfOpen, "cooldown elapsed => single-probe window opens");
    }

    [Fact]
    public void HalfOpen_SuccessfulProbe_ReturnsToClosed_AndResetsAllCounters()
    {
        var breaker = OpenBreaker();
        var probeTime = breaker.CooldownUntil!.Value.AddSeconds(1);
        breaker.CanAttempt(probeTime).Should().BeTrue();
        breaker.State.Should().Be(BreakerState.HalfOpen);

        breaker.Record(PollOutcome.Ok, probeTime);

        breaker.State.Should().Be(BreakerState.Closed);
        breaker.ConsecutiveFailures.Should().Be(0);
        breaker.OpenCycleCount.Should().Be(0);
        breaker.CooldownUntil.Should().BeNull();
    }

    [Fact]
    public void HalfOpen_FailedProbe_ReturnsToOpen_WithEscalatedCooldown_AndIncrementsOpenCycleCount()
    {
        var breaker = OpenBreaker();
        var firstCooldown = breaker.CooldownUntil!.Value;
        var probeTime = firstCooldown.AddSeconds(1);
        breaker.CanAttempt(probeTime).Should().BeTrue();

        breaker.Record(PollOutcome.Blocked, probeTime);

        breaker.State.Should().Be(BreakerState.Open);
        breaker.OpenCycleCount.Should().Be(1);
        breaker.CooldownUntil.Should().NotBeNull();
        var secondSpan = breaker.CooldownUntil!.Value - probeTime;
        var firstSpan = firstCooldown - BaseNowUtc.AddMinutes(2);
        secondSpan.Should().BeGreaterThan(firstSpan, "cooldown must escalate (e.g. double) after a failed probe");
    }

    [Fact]
    public void RepeatedFailedProbes_EventuallyTransitionToHalted_TerminalState()
    {
        var breaker = OpenBreaker();
        var time = breaker.CooldownUntil!.Value.AddSeconds(1);

        // Cycle through Open -> HalfOpen(probe fails) -> Open repeatedly until Halted.
        // maxOpenCycles default = 5, so this should halt well within 10 iterations.
        for (var i = 0; i < 10 && breaker.State != BreakerState.Halted; i++)
        {
            breaker.CanAttempt(time).Should().BeTrue($"iteration {i} should still allow a probe before Halted");
            breaker.Record(PollOutcome.Error, time);
            if (breaker.State == BreakerState.Halted) break;
            time = breaker.CooldownUntil!.Value.AddSeconds(1);
        }

        breaker.State.Should().Be(BreakerState.Halted);
    }

    [Fact]
    public void Halted_NeverAllowsAttempts_RegardlessOfTime()
    {
        var breaker = HaltedBreaker();

        breaker.CanAttempt(DateTime.UtcNow.AddYears(1)).Should().BeFalse();
        breaker.State.Should().Be(BreakerState.Halted);
    }

    [Fact]
    public void Halted_IgnoresRecordCalls_StaysHalted()
    {
        var breaker = HaltedBreaker();

        breaker.Record(PollOutcome.Ok, DateTime.UtcNow);

        breaker.State.Should().Be(BreakerState.Halted, "Halted is terminal until manual reset");
    }

    [Fact]
    public void DegradedBackoff_IsCappedAtMaximum()
    {
        var breaker = new PollingCircuitBreaker(
            new FixedRandom(0.99),
            baseInterval: TimeSpan.FromHours(5),
            maxDegradedBackoff: TimeSpan.FromHours(6));

        breaker.Record(PollOutcome.Error, BaseNowUtc);

        // base interval x2 = 10h, capped to 6h, +jitter must still stay within a sane bound near the cap.
        breaker.DegradedBackoff!.Value.Should().BeLessOrEqualTo(TimeSpan.FromHours(6.6));
    }

    [Fact]
    public void Deterministic_SameSeed_ProducesSameCooldown()
    {
        var breakerA = CreateBreaker(new FixedRandom(0.42));
        var breakerB = CreateBreaker(new FixedRandom(0.42));

        breakerA.Record(PollOutcome.Error, BaseNowUtc);
        breakerB.Record(PollOutcome.Error, BaseNowUtc);

        breakerA.DegradedBackoff.Should().Be(breakerB.DegradedBackoff);
    }

    [Fact]
    public void Constructor_NullRng_Throws()
    {
        Action act = () => new PollingCircuitBreaker(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToSnapshot_ReflectsCurrentState()
    {
        var breaker = OpenBreaker();

        var snapshot = breaker.ToSnapshot();

        snapshot.State.Should().Be(BreakerState.Open);
        snapshot.ConsecutiveFailures.Should().Be(3);
        snapshot.CooldownUntil.Should().Be(breaker.CooldownUntil);
        snapshot.OpenCycleCount.Should().Be(0);
    }

    [Fact]
    public void RehydratingFromSnapshot_RestoresExactState()
    {
        var original = OpenBreaker();
        var snapshot = original.ToSnapshot();

        var rehydrated = new PollingCircuitBreaker(new FixedRandom(0.5), initialSnapshot: snapshot);

        rehydrated.State.Should().Be(original.State);
        rehydrated.ConsecutiveFailures.Should().Be(original.ConsecutiveFailures);
        rehydrated.CooldownUntil.Should().Be(original.CooldownUntil);
        rehydrated.OpenCycleCount.Should().Be(original.OpenCycleCount);
    }

    // --- Snapshot invariant validation ---

    [Fact]
    public void Constructor_OpenWithNullCooldown_AutoCorrectsToUtcNow()
    {
        var snapshot = new BreakerSnapshot
        {
            State = BreakerState.Open,
            ConsecutiveFailures = 3,
            CooldownUntil = null, // invalid: Open requires a cooldown
            OpenCycleCount = 0
        };

        var breaker = new PollingCircuitBreaker(new FixedRandom(0.5), initialSnapshot: snapshot);

        // Snapshot validation auto-corrects null cooldown to UtcNow
        breaker.State.Should().Be(BreakerState.Open);
        breaker.CooldownUntil.Should().NotBeNull("null cooldown must be auto-corrected to avoid deadlock");
        // Since UtcNow was just set, CanAttempt should return true (cooldown is already elapsed)
        breaker.CanAttempt(DateTime.UtcNow.AddSeconds(1)).Should().BeTrue(
            "auto-corrected cooldown allows immediate transition to HalfOpen");
        breaker.State.Should().Be(BreakerState.HalfOpen);
    }

    [Fact]
    public void Constructor_HalfOpenWithNullCooldown_AutoCorrectsToUtcNow()
    {
        var snapshot = new BreakerSnapshot
        {
            State = BreakerState.HalfOpen,
            ConsecutiveFailures = 2,
            CooldownUntil = null, // invalid: HalfOpen should have come from an Open state
            OpenCycleCount = 0
        };

        var breaker = new PollingCircuitBreaker(new FixedRandom(0.5), initialSnapshot: snapshot);

        breaker.CooldownUntil.Should().NotBeNull("null cooldown in HalfOpen must be auto-corrected");
    }

    [Fact]
    public void Constructor_HaltedWithNonNullCooldown_CoercesToNull()
    {
        var snapshot = new BreakerSnapshot
        {
            State = BreakerState.Halted,
            ConsecutiveFailures = 5,
            CooldownUntil = DateTime.UtcNow.AddHours(48), // stale cooldown, meaningless in Halted
            OpenCycleCount = 5
        };

        var breaker = new PollingCircuitBreaker(new FixedRandom(0.5), initialSnapshot: snapshot);

        breaker.State.Should().Be(BreakerState.Halted);
        breaker.CooldownUntil.Should().BeNull("cooldown is meaningless in Halted state");
    }

    [Fact]
    public void Constructor_ClosedWithStaleCooldown_CoercesToNull()
    {
        var snapshot = new BreakerSnapshot
        {
            State = BreakerState.Closed,
            ConsecutiveFailures = 0,
            CooldownUntil = DateTime.UtcNow.AddHours(24), // stale from a previous cycle
            OpenCycleCount = 0
        };

        var breaker = new PollingCircuitBreaker(new FixedRandom(0.5), initialSnapshot: snapshot);

        breaker.State.Should().Be(BreakerState.Closed);
        breaker.CooldownUntil.Should().BeNull("cooldown must be coerced to null in Closed state");
    }

    // --- Record() contract enforcement ---

    [Fact]
    public void Record_WhenOpen_ThrowsInvalidOperationException()
    {
        var breaker = OpenBreaker();

        Action act = () => breaker.Record(PollOutcome.Ok, BaseNowUtc);

        act.Should().Throw<InvalidOperationException>(
            "Record() must not be called while Open — CanAttempt() must be called first");
    }

    [Fact]
    public void Record_WhenOpen_WithFailure_ThrowsInvalidOperationException()
    {
        var breaker = OpenBreaker();

        Action act = () => breaker.Record(PollOutcome.Error, BaseNowUtc);

        act.Should().Throw<InvalidOperationException>(
            "Record() violations apply to failures too, not just success outcomes");
    }

    // --- Full reset on Closed transition ---

    [Fact]
    public void Record_SuccessFromDegraded_ResetsCooldownAndOpenCycleCount()
    {
        // Simulate a breaker that went through Open → HalfOpen (failed) → Open → HalfOpen (success)
        // and verify the Closed reset clears everything.
        var breaker = OpenBreaker();
        var time = breaker.CooldownUntil!.Value.AddSeconds(1);

        // Fail one HalfOpen probe to increment OpenCycleCount
        breaker.CanAttempt(time).Should().BeTrue();
        breaker.Record(PollOutcome.Error, time);
        breaker.OpenCycleCount.Should().Be(1);

        // Now re-enter Open, wait, and succeed to get back to HalfOpen
        time = breaker.CooldownUntil!.Value.AddSeconds(1);
        breaker.CanAttempt(time).Should().BeTrue();
        breaker.State.Should().Be(BreakerState.HalfOpen);

        // Success should reset everything including OpenCycleCount
        breaker.Record(PollOutcome.Ok, time);

        breaker.State.Should().Be(BreakerState.Closed);
        breaker.ConsecutiveFailures.Should().Be(0);
        breaker.CooldownUntil.Should().BeNull();
        breaker.OpenCycleCount.Should().Be(0, "OpenCycleCount must reset on transition to Closed");
        breaker.DegradedBackoff.Should().BeNull();
    }

    // --- Persistence round-trip / restart-survival, via LastSyncTracker (the owner of SyncMetadata I/O) ---

    private static LastSyncTracker CreateTrackerOverSharedDb(string dbName)
    {
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var scopeMock = new Mock<IServiceScope>();
        var providerMock = new Mock<IServiceProvider>();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var db = new ApplicationDbContext(options);
        providerMock.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns(db);
        scopeMock.Setup(s => s.ServiceProvider).Returns(providerMock.Object);
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
        return new LastSyncTracker(scopeFactoryMock.Object);
    }

    [Fact]
    public void BreakerState_SurvivesSimulatedAppRestart_ViaSyncMetadata()
    {
        var dbName = Guid.NewGuid().ToString();

        // "App instance #1": breaker trips to Open and persists its state.
        var trackerBeforeRestart = CreateTrackerOverSharedDb(dbName);
        var breaker = OpenBreaker();
        trackerBeforeRestart.SaveBreakerSnapshot(breaker.ToSnapshot());

        // "App restart": brand-new LastSyncTracker instance, same underlying DB.
        var trackerAfterRestart = CreateTrackerOverSharedDb(dbName);
        var reloaded = trackerAfterRestart.GetBreakerSnapshot();

        reloaded.State.Should().Be(BreakerState.Open, "an Open cooldown must not silently reset to Closed on restart");
        reloaded.ConsecutiveFailures.Should().Be(breaker.ConsecutiveFailures);
        reloaded.CooldownUntil.Should().Be(breaker.CooldownUntil);
        reloaded.OpenCycleCount.Should().Be(breaker.OpenCycleCount);

        // The rehydrated breaker must behave identically to the pre-restart one.
        var rehydratedBreaker = new PollingCircuitBreaker(new FixedRandom(0.5), initialSnapshot: reloaded);
        rehydratedBreaker.CanAttempt(reloaded.CooldownUntil!.Value.AddSeconds(-1)).Should().BeFalse(
            "cooldown must still be honored after restart, not reset");
    }

    [Fact]
    public void BreakerSnapshot_DefaultsToClosed_WhenNothingPersistedYet()
    {
        var tracker = CreateTrackerOverSharedDb(Guid.NewGuid().ToString());

        var snapshot = tracker.GetBreakerSnapshot();

        snapshot.State.Should().Be(BreakerState.Closed);
        snapshot.ConsecutiveFailures.Should().Be(0);
        snapshot.CooldownUntil.Should().BeNull();
        snapshot.OpenCycleCount.Should().Be(0);
    }

    // --- helpers ---

    private static PollingCircuitBreaker OpenBreaker()
    {
        var breaker = CreateBreaker();
        breaker.Record(PollOutcome.Error, BaseNowUtc);
        breaker.Record(PollOutcome.Timeout, BaseNowUtc.AddMinutes(1));
        breaker.Record(PollOutcome.Blocked, BaseNowUtc.AddMinutes(2));
        breaker.State.Should().Be(BreakerState.Open);
        return breaker;
    }

    private static PollingCircuitBreaker HaltedBreaker()
    {
        var breaker = OpenBreaker();
        var time = breaker.CooldownUntil!.Value.AddSeconds(1);
        for (var i = 0; i < 10 && breaker.State != BreakerState.Halted; i++)
        {
            breaker.CanAttempt(time);
            breaker.Record(PollOutcome.Error, time);
            if (breaker.State == BreakerState.Halted) break;
            time = breaker.CooldownUntil!.Value.AddSeconds(1);
        }
        breaker.State.Should().Be(BreakerState.Halted);
        return breaker;
    }
}
