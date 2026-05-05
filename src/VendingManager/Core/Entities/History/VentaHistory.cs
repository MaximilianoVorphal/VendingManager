using System.ComponentModel.DataAnnotations.Schema;

namespace VendingManager.Core.Entities;

/// <summary>
/// History entity for Venta. Mirrors Venta columns plus audit fields.
/// </summary>
public class VentaHistory
{
    public int Id { get; set; }
    public int EntityId { get; set; }
    public string Action { get; set; } = string.Empty;

    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }

    public DateTime Timestamp { get; set; }
    public string Usuario { get; set; } = string.Empty;

    // --- Venta base columns (simple scalars only) ---
    public DateTime FechaHora { get; set; }
    public DateTime FechaLocal { get; set; }
    public int MaquinaId { get; set; }
    public int? ProductoId { get; set; }
    public string NumeroSlot { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal PrecioVenta { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CostoVenta { get; set; }

    public string IdOrdenMaquina { get; set; } = string.Empty;
    public string? IdTransaccionPago { get; set; }
    public bool Pagado { get; set; }
}
