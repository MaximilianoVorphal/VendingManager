namespace VendingManager.Core.Entities;

/// <summary>
/// Latest-state row (1 per Maquina) produced by the offset drift watchdog. Upserted on
/// every import batch that yields at least <see cref="Configuration.VendingConfig.OffsetDriftMinSamples"/>
/// usable (MachineTime, ServerTime) pairs. "Drifting" is never stored here — it is
/// computed at read time by comparing <see cref="ImpliedOffsetHours"/> against the
/// machine's current <see cref="Maquina.TimezoneOffsetHours"/>.
/// </summary>
public class OffsetDriftState
{
    public int MaquinaId { get; set; }

    /// <summary>Median of the raw (ServerTime - MachineTime) delta in hours. Diagnostic only.</summary>
    public double ObservedMedianDeltaHours { get; set; }

    /// <summary>Median per-row implied offset (Chile-local time minus machine time), rounded to the
    /// nearest hour — the value that would be proposed for <see cref="Maquina.TimezoneOffsetHours"/>.</summary>
    public int ImpliedOffsetHours { get; set; }

    /// <summary>Number of usable dual-timestamp rows behind this measurement.</summary>
    public int SampleCount { get; set; }

    public DateTime MeasuredAtUtc { get; set; }
}
