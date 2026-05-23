using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using VendingManager.Shared.Enums;

namespace VendingManager.Core.Entities;

/// <summary>
/// Entidad history para Transferencia. Refleja las columnas más los campos de auditoría.
/// </summary>
public class TransferenciaHistory
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

    // --- Transferencia base columns ---
    public DateTime Fecha { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Monto { get; set; }

    [MaxLength(500)]
    public string? Descripcion { get; set; }

    [Required]
    [MaxLength(200)]
    public string Trabajador { get; set; } = string.Empty;

    public TransferenciaEstado Estado { get; set; } = TransferenciaEstado.Pendiente;

    public int? RendicionId { get; set; }
    public int? MovimientoCajaId { get; set; }
}