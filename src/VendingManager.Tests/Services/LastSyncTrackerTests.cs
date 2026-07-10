using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using VendingManager.Infrastructure.Data;
using VendingManager.Infrastructure.Services;
using Xunit;

namespace VendingManager.Tests.Services;

public class LastSyncTrackerTests
{
    private static LastSyncTracker CreateTracker()
    {
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var scopeMock = new Mock<IServiceScope>();
        var providerMock = new Mock<IServiceProvider>();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(options);
        providerMock.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns(db);
        scopeMock.Setup(s => s.ServiceProvider).Returns(providerMock.Object);
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
        return new LastSyncTracker(scopeFactoryMock.Object);
    }

    [Fact]
    public void GetLastSync_ReturnsNull_WhenNotSet()
    {
        var tracker = CreateTracker();

        var result = tracker.GetLastSync();

        result.Should().BeNull("no sync has been recorded yet");
    }

    [Fact]
    public void SetLastSync_ThenGetLastSync_ReturnsSameValue()
    {
        var tracker = CreateTracker();
        var expected = new DateTime(2026, 6, 30, 14, 30, 0);

        tracker.SetLastSync(expected);
        var result = tracker.GetLastSync();

        result.Should().Be(expected);
    }

    [Fact]
    public void SetLastSync_OverwritesPreviousValue()
    {
        var tracker = CreateTracker();
        var first = new DateTime(2026, 6, 30, 10, 0, 0);
        var second = new DateTime(2026, 6, 30, 14, 30, 0);

        tracker.SetLastSync(first);
        tracker.SetLastSync(second);
        var result = tracker.GetLastSync();

        result.Should().Be(second, "the second write should overwrite the first");
    }

    [Fact]
    public void GetLastSync_IsThreadSafe()
    {
        var tracker = CreateTracker();
        var baseTime = new DateTime(2026, 6, 30, 12, 0, 0);

        // Write sequentially to establish a known state
        for (int i = 0; i < 100; i++)
        {
            tracker.SetLastSync(baseTime.AddMinutes(i));
        }

        // Final value should be the last write
        var expectedFinal = baseTime.AddMinutes(99);
        tracker.GetLastSync().Should().Be(expectedFinal, "last write should win");

        // Concurrent reads should all return a valid value (no corruption)
        var readResults = new DateTime?[100];
        Parallel.For(0, 100, i =>
        {
            readResults[i] = tracker.GetLastSync();
        });

        readResults.Should().AllSatisfy(r =>
            r.Should().NotBeNull("concurrent reads should not return corrupted data"));
        readResults.Should().AllSatisfy(r =>
            r.Should().Be(expectedFinal, "all concurrent reads should see the last written value"));
    }

    [Fact]
    public void GetHealthStatus_Fresh_NoSyncEverRecorded_IsDegraded()
    {
        var tracker = CreateTracker();

        var status = tracker.GetHealthStatus();

        status.Should().Be(SyncHealthStatus.Degraded, "a fresh tracker has no successful sync baseline yet");
    }

    [Fact]
    public void GetHealthStatus_HealthyRecentSync_BreakerClosed_IsHealthy()
    {
        var tracker = CreateTracker();
        tracker.SetLastSync(DateTime.UtcNow.AddMinutes(-10));

        var status = tracker.GetHealthStatus();

        status.Should().Be(SyncHealthStatus.Healthy);
    }

    [Fact]
    public void GetHealthStatus_StaleSync_BreakerClosed_IsDegraded()
    {
        var tracker = CreateTracker();
        tracker.SetLastSync(DateTime.UtcNow.Subtract(LastSyncTracker.StalenessThreshold).AddMinutes(-1));

        var status = tracker.GetHealthStatus();

        status.Should().Be(SyncHealthStatus.Degraded, "last successful sync has aged past the staleness threshold");
    }

    [Fact]
    public void GetHealthStatus_RecentSync_BreakerOpen_IsDegraded()
    {
        var tracker = CreateTracker();
        tracker.SetLastSync(DateTime.UtcNow.AddMinutes(-1));
        tracker.SaveBreakerSnapshot(new BreakerSnapshot
        {
            State = BreakerState.Open,
            ConsecutiveFailures = 3,
            CooldownUntil = DateTime.UtcNow.AddHours(24),
            OpenCycleCount = 0
        });

        var status = tracker.GetHealthStatus();

        status.Should().Be(SyncHealthStatus.Degraded, "breaker not Closed overrides a recent successful sync");
    }

    [Fact]
    public void GetHealthStatus_DoesNotMutate_LastSuccessfulSyncTimestamp()
    {
        var tracker = CreateTracker();
        var expected = DateTime.UtcNow.AddMinutes(-5);
        tracker.SetLastSync(expected);
        tracker.SaveBreakerSnapshot(new BreakerSnapshot { State = BreakerState.Open });

        // Querying health (Degraded, due to Open breaker) must not touch the preserved timestamp.
        tracker.GetHealthStatus().Should().Be(SyncHealthStatus.Degraded);

        tracker.GetLastSync().Should().Be(expected, "GetHealthStatus must never overwrite LastSuccessfulSync");
    }

    [Fact]
    public void GetBreakerSnapshot_DefaultsToClosed_WhenNothingPersistedYet()
    {
        var tracker = CreateTracker();

        var snapshot = tracker.GetBreakerSnapshot();

        snapshot.State.Should().Be(BreakerState.Closed);
        snapshot.ConsecutiveFailures.Should().Be(0);
        snapshot.CooldownUntil.Should().BeNull();
        snapshot.OpenCycleCount.Should().Be(0);
    }

    [Fact]
    public void SaveBreakerSnapshot_ThenGetBreakerSnapshot_ReturnsSameValues()
    {
        var tracker = CreateTracker();
        var snapshot = new BreakerSnapshot
        {
            State = BreakerState.Degraded,
            ConsecutiveFailures = 2,
            CooldownUntil = null,
            OpenCycleCount = 0
        };

        tracker.SaveBreakerSnapshot(snapshot);
        var result = tracker.GetBreakerSnapshot();

        result.State.Should().Be(BreakerState.Degraded);
        result.ConsecutiveFailures.Should().Be(2);
        result.CooldownUntil.Should().BeNull();
        result.OpenCycleCount.Should().Be(0);
    }
}
