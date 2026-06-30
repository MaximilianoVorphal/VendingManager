using FluentAssertions;
using VendingManager.Infrastructure.Services;
using Xunit;

namespace VendingManager.Tests.Services;

public class LastSyncTrackerTests
{
    [Fact]
    public void GetLastSync_ReturnsNull_WhenNotSet()
    {
        var tracker = new LastSyncTracker();

        var result = tracker.GetLastSync();

        result.Should().BeNull("no sync has been recorded yet");
    }

    [Fact]
    public void SetLastSync_ThenGetLastSync_ReturnsSameValue()
    {
        var tracker = new LastSyncTracker();
        var expected = new DateTime(2026, 6, 30, 14, 30, 0);

        tracker.SetLastSync(expected);
        var result = tracker.GetLastSync();

        result.Should().Be(expected);
    }

    [Fact]
    public void SetLastSync_OverwritesPreviousValue()
    {
        var tracker = new LastSyncTracker();
        var first = new DateTime(2026, 6, 30, 10, 0, 0);
        var second = new DateTime(2026, 6, 30, 14, 30, 0);

        tracker.SetLastSync(first);
        tracker.SetLastSync(second);
        var result = tracker.GetLastSync();

        result.Should().Be(second, "the second write should overwrite the first");
    }

    [Fact]
    public async Task GetLastSync_IsThreadSafe()
    {
        var tracker = new LastSyncTracker();
        var baseTime = new DateTime(2026, 6, 30, 12, 0, 0);
        var tasks = new List<Task>();

        // Write from multiple threads
        for (int i = 0; i < 100; i++)
        {
            var t = baseTime.AddMinutes(i);
            tasks.Add(Task.Run(() => tracker.SetLastSync(t)));
        }

        // Read from multiple threads simultaneously
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var val = tracker.GetLastSync();
                // Should not throw — just verifying no corruption
                val.Should().BeOneOf(
                    Enumerable.Range(0, 100)
                        .Select(j => baseTime.AddMinutes(j))
                        .ToArray());
            }));
        }

        await Task.WhenAll(tasks);

        // Final read — should be one of the written values
        var final = tracker.GetLastSync();
        final.Should().NotBeNull();
        final!.Value.Should().BeOnOrAfter(baseTime);
        final.Value.Should().BeOnOrBefore(baseTime.AddMinutes(99));
    }
}
