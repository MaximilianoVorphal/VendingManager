namespace VendingManager.Core.Configuration;

public class VendingConfig
{
    public DateTime CajaStartDate { get; set; } = new DateTime(2026, 1, 1);

    public int TransbankFee { get; set; } = 80;

    public int RotacionStockMinimoDias { get; set; } = 30;

    /// <summary>
    /// Cuando es true, stock-critico consulta SnapshotSlots del template Terminado más reciente
    /// en lugar de ConfiguracionSlots. Permite transición gradual al nuevo flujo.
    /// </summary>
    public bool UseTemplateInventoryForStockCritico { get; set; } = false;

    /// <summary>
    /// When true, enables in-memory caching for the period list endpoint (GET /periodos).
    /// Kill-switch: set false to bypass cache without redeploy.
    /// </summary>
    public bool UsePeriodCache { get; set; } = true;

    /// <summary>
    /// Sliding expiration duration in minutes for the period list cache.
    /// Default: 5 minutes.
    /// </summary>
    public int PeriodCacheDurationMinutes { get; set; } = 5;

    /// <summary>
    /// Default timezone offset in hours used when a Maquina does not have
    /// its own <see cref="Entities.Maquina.TimezoneOffsetHours"/> configured.
    /// Default -11 corresponds to Chilean CLT (UTC-4 standard time, UTC-3 during DST;
    /// the machine reports UTC+7 offset, so the net adjustment is -11).
    /// </summary>
    public int DefaultTimezoneOffsetHours { get; set; } = -11;

    /// <summary>
    /// Minimum absolute difference (in hours) between a machine's configured offset and its
    /// watchdog-implied offset before the machine is surfaced as drifting.
    /// </summary>
    public int OffsetDriftThresholdHours { get; set; } = 1;

    /// <summary>
    /// Minimum number of usable (MachineTime, ServerTime) sample pairs required in a single
    /// import batch before the offset drift watchdog evaluates/persists a machine's drift state.
    /// </summary>
    public int OffsetDriftMinSamples { get; set; } = 5;
}