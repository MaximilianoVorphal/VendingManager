namespace VendingManager.Core.Configuration;

public class VendingConfig
{
    public DateTime CajaStartDate { get; set; } = new DateTime(2026, 1, 1);

    public int TransbankFee { get; set; } = 80;

    public int RotacionStockMinimoDias { get; set; } = 30;

    public int RotacionUmbralCritico { get; set; } = 7;

    /// <summary>
    /// Ruta absoluta donde se guardan las imágenes de facturas.
    /// Si es null o vacío, se usa WebRootPath/wwwroot como fallback.
    /// Ejemplo en Docker: "/var/uploads/vendingmanager"
    /// </summary>
    public string? FacturaUploadPath { get; set; }

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
}