using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VendingManager.Core.Entities;

/// <summary>
/// History entity for Compra. Mirrors Compra columns plus audit fields.
/// </summary>
public class CompraHistory
{
    [Key]
    public int Id { get; set; }

    public int EntityId { get; set; }
    public string Action { get; set; } = string.Empty;

    [Column(TypeName = "nvarchar(max)")]
    public string? BeforeJson { get; set; }

    [Column(TypeName = "nvarchar(max)")]
    public string? AfterJson { get; set; }

    public DateTime Timestamp { get; set; }
    public string Usuario { get; set; } = string.Empty;

    // --- Compra base columns (as simple scalars, no navigation properties) ---
    public DateTime FechaCompra { get; set; }

    [Required]
    public string Proveedor { get; set; } = string.Empty;

    public string? NumeroDocumento { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal MontoTotal { get; set; }

    public string Estado { get; set; } = "PAGADA";
    public string TipoFactura { get; set; } = "MERCADERIA";
    public bool PagadaCaja { get; set; } = true;
    public string? UsuarioRegistra { get; set; }
    public string? FacturaImagenPath { get; set; }
}
