using System.ComponentModel.DataAnnotations.Schema;

namespace VendingManager.Core.Entities;

/// <summary>
/// History entity for ConfiguracionSlot. Mirrors columns plus audit fields.
/// </summary>
public class ConfiguracionSlotHistory
{
    public int Id { get; set; }
    public int EntityId { get; set; }
    public string Action { get; set; } = string.Empty;

    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }

    public DateTime Timestamp { get; set; }
    public string Usuario { get; set; } = string.Empty;

    // --- ConfiguracionSlot base columns ---
    public int MaquinaId { get; set; }
    public string NumeroSlot { get; set; } = string.Empty;
    public int? ProductoId { get; set; }
    public int StockActual { get; set; }
    public int CapacidadMaxima { get; set; }
    public int StockMinimo { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PrecioVenta { get; set; }
}
