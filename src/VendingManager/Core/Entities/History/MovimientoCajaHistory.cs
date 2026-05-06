using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VendingManager.Core.Entities;

/// <summary>
/// Entidad history para MovimientoCaja. Refleja las columnas más los campos de auditoría.
/// </summary>
public class MovimientoCajaHistory
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

    // --- MovimientoCaja base columns ---
    public DateTime Fecha { get; set; }

    [Required]
    public string Descripcion { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Monto { get; set; }

    public string Tipo { get; set; } = "GASTO";
    public string Categoria { get; set; } = "GENERAL";
    public string? ImagenPath { get; set; }
    public int? ProductoId { get; set; }
    public int Cantidad { get; set; }
    public int? OrdenCargaId { get; set; }
    public int? CompraId { get; set; }
    public int? GastoRecurrenteId { get; set; }
}
