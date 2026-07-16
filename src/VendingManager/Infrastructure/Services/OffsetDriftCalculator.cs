using System;
using System.Collections.Generic;
using System.Linq;

namespace VendingManager.Infrastructure.Services;

/// <summary>
/// Pure, deterministic computation of a machine's implied timezone offset from a batch of
/// (MachineTime, ServerTime) sample pairs. No I/O, no DB access — fully unit-testable, mirroring
/// <see cref="PollScheduler"/>'s pure-computation style.
/// </summary>
public static class OffsetDriftCalculator
{
    /// <summary>The OurVend server's reporting time zone. Fixed +8 (no DST), so converting a raw
    /// server timestamp to UTC is always unambiguous.</summary>
    public static readonly TimeZoneInfo ServerTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

    /// <summary>The business time zone a machine's local timestamp should resolve to. DST-safe —
    /// resolved via <see cref="TimeZoneInfo"/>, never inferred from integer constants.</summary>
    public static readonly TimeZoneInfo ChileTimeZone = PollScheduler.ChileTimeZone;

    /// <summary>One usable dual-timestamp sample: the machine's own reported local time and the
    /// OurVend server's timestamp for the same event.</summary>
    public readonly record struct Sample(DateTime MachineTime, DateTime ServerTime);

    /// <summary>Aggregate drift measurement for one machine over a batch of <see cref="Sample"/>s.</summary>
    public readonly record struct Result(double ObservedMedianDeltaHours, int ImpliedOffsetHours, int SampleCount);

    /// <summary>
    /// Computes the implied offset for a batch of samples belonging to a single machine.
    /// Returns <c>null</c> for an empty batch (nothing to compute).
    /// </summary>
    public static Result? ComputeImpliedOffset(IReadOnlyList<Sample> samples)
    {
        if (samples is null || samples.Count == 0)
        {
            return null;
        }

        var rowOffsetHours = new List<double>(samples.Count);
        var rawDeltaHours = new List<double>(samples.Count);

        foreach (var sample in samples)
        {
            // China is fixed UTC+8 (no DST), so this conversion is always unambiguous regardless
            // of the calendar date.
            var serverInstantUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(sample.ServerTime, DateTimeKind.Unspecified), ServerTimeZone);

            // Chile's DST is resolved per-row here, so a batch straddling the transition instant
            // never corrupts the median (each row uses the delta valid at its own ServerTime).
            var chileLocal = TimeZoneInfo.ConvertTimeFromUtc(serverInstantUtc, ChileTimeZone);

            rowOffsetHours.Add((chileLocal - sample.MachineTime).TotalHours);
            rawDeltaHours.Add((sample.ServerTime - sample.MachineTime).TotalHours);
        }

        var impliedOffsetHours = (int)Math.Round(Median(rowOffsetHours), MidpointRounding.AwayFromZero);
        var observedMedianDeltaHours = Median(rawDeltaHours);

        return new Result(observedMedianDeltaHours, impliedOffsetHours, samples.Count);
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int count = sorted.Count;
        int mid = count / 2;
        return count % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
