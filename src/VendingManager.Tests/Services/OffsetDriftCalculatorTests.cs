using System;
using System.Collections.Generic;
using FluentAssertions;
using VendingManager.Infrastructure.Services;
using Xunit;

namespace VendingManager.Tests.Services;

/// <summary>
/// Pure unit tests for <see cref="OffsetDriftCalculator"/> — no DB, no I/O. Mirrors
/// <see cref="PollSchedulerTests"/>'s style for DST-transition coverage.
/// </summary>
public class OffsetDriftCalculatorTests
{
    private static readonly TimeZoneInfo Shanghai = OffsetDriftCalculator.ServerTimeZone;
    private static readonly TimeZoneInfo Santiago = OffsetDriftCalculator.ChileTimeZone;

    private static DateTime ChileLocalFromServerTime(DateTime serverTimeUnspecified)
    {
        var utc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(serverTimeUnspecified, DateTimeKind.Unspecified), Shanghai);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, Santiago);
    }

    private static DateTime MachineTimeForImpliedOffset(DateTime serverTimeUnspecified, int impliedOffsetHours)
        => ChileLocalFromServerTime(serverTimeUnspecified).AddHours(-impliedOffsetHours);

    /// <summary>Inverse of <see cref="ChileLocalFromServerTime"/>: returns the Shanghai ServerTime
    /// that round-trips to the given Chile-local instant.</summary>
    private static DateTime ServerTimeForChileLocal(DateTime chileLocalUnspecified)
    {
        var utc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(chileLocalUnspecified, DateTimeKind.Unspecified), Santiago);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, Shanghai);
    }

    private static OffsetDriftCalculator.Sample SampleForImpliedOffset(DateTime serverTime, int impliedOffsetHours)
        => new(MachineTimeForImpliedOffset(serverTime, impliedOffsetHours), serverTime);

    // ── DST straddle: per-row TimeZoneInfo conversion must not skew the result ──────

    [Fact]
    public void ComputeImpliedOffset_AcrossChileAutumnDstTransition_StaysStable()
    {
        var transitionDayLocal = FindDstTransitionDate(Santiago, offsetDecreases: true, year: 2026);
        AssertOffsetStableAcrossTransition(transitionDayLocal);
    }

    [Fact]
    public void ComputeImpliedOffset_AcrossChileSpringDstTransition_StaysStable()
    {
        var transitionDayLocal = FindDstTransitionDate(Santiago, offsetDecreases: false, year: 2026);
        AssertOffsetStableAcrossTransition(transitionDayLocal);
    }

    private static void AssertOffsetStableAcrossTransition(DateTime transitionDayLocal)
    {
        const int configuredOffset = -11;

        // One sample the day before the transition, one the day after — both real machine
        // clocks that agree exactly with the configured offset. Despite the China↔Chile UTC
        // delta shifting across the transition, the per-row TimeZoneInfo conversion must
        // resolve both to the SAME implied offset (no false drift from the DST event alone).
        // Chile transitions always land at local midnight, so noon on either side is unambiguous.
        var chileLocalBefore = transitionDayLocal.AddDays(-1).AddHours(12);
        var chileLocalAfter = transitionDayLocal.AddDays(1).AddHours(12);

        var serverTimeBefore = ServerTimeForChileLocal(chileLocalBefore);
        var serverTimeAfter = ServerTimeForChileLocal(chileLocalAfter);

        var sampleBefore = SampleForImpliedOffset(serverTimeBefore, configuredOffset);
        var sampleAfter = SampleForImpliedOffset(serverTimeAfter, configuredOffset);

        // Sanity: the two samples really do straddle a change in the Chile UTC offset.
        var offsetAtBefore = Santiago.GetUtcOffset(
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(serverTimeBefore, DateTimeKind.Unspecified), Shanghai));
        var offsetAtAfter = Santiago.GetUtcOffset(
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(serverTimeAfter, DateTimeKind.Unspecified), Shanghai));
        offsetAtBefore.Should().NotBe(offsetAtAfter, "the test must actually straddle a DST transition");

        var resultBefore = OffsetDriftCalculator.ComputeImpliedOffset(new[] { sampleBefore });
        var resultAfter = OffsetDriftCalculator.ComputeImpliedOffset(new[] { sampleAfter });

        resultBefore!.Value.ImpliedOffsetHours.Should().Be(configuredOffset);
        resultAfter!.Value.ImpliedOffsetHours.Should().Be(configuredOffset);

        // And combined into a single batch, the median must still land on the configured offset.
        var combined = OffsetDriftCalculator.ComputeImpliedOffset(new[] { sampleBefore, sampleAfter });
        combined!.Value.ImpliedOffsetHours.Should().Be(configuredOffset);
    }

    // ── Median aggregation ───────────────────────────────────────────────────

    [Fact]
    public void ComputeImpliedOffset_OddSampleCount_ReturnsMiddleValue()
    {
        var baseServerTime = new DateTime(2026, 7, 15, 12, 0, 0);
        var samples = new List<OffsetDriftCalculator.Sample>
        {
            SampleForImpliedOffset(baseServerTime.AddMinutes(0), -9),
            SampleForImpliedOffset(baseServerTime.AddMinutes(10), -9),
            SampleForImpliedOffset(baseServerTime.AddMinutes(20), -9),
        };

        var result = OffsetDriftCalculator.ComputeImpliedOffset(samples);

        result!.Value.ImpliedOffsetHours.Should().Be(-9);
        result.Value.SampleCount.Should().Be(3);
    }

    [Fact]
    public void ComputeImpliedOffset_WithSingleOutlier_MedianUnaffected()
    {
        var baseServerTime = new DateTime(2026, 7, 15, 12, 0, 0);
        var samples = new List<OffsetDriftCalculator.Sample>
        {
            SampleForImpliedOffset(baseServerTime.AddMinutes(0), -9),
            SampleForImpliedOffset(baseServerTime.AddMinutes(10), -9),
            SampleForImpliedOffset(baseServerTime.AddMinutes(20), -9),
            SampleForImpliedOffset(baseServerTime.AddMinutes(30), -9),
            SampleForImpliedOffset(baseServerTime.AddMinutes(40), 40), // wild outlier
        };

        var result = OffsetDriftCalculator.ComputeImpliedOffset(samples);

        result!.Value.ImpliedOffsetHours.Should().Be(-9);
        result.Value.SampleCount.Should().Be(5);
    }

    [Fact]
    public void ComputeImpliedOffset_EmptySamples_ReturnsNull()
    {
        var result = OffsetDriftCalculator.ComputeImpliedOffset(Array.Empty<OffsetDriftCalculator.Sample>());

        result.Should().BeNull();
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
                    return TimeZoneInfo.ConvertTimeFromUtc(current.AddHours(4), tz).Date;
                }
            }
            previousOffset = offset;
        }
        throw new InvalidOperationException($"No matching DST transition found for {year}");
    }
}
