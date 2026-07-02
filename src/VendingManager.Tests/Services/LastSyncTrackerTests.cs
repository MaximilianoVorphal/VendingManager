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
}
