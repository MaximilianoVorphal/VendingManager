using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using VendingManager.Shared.Enums;

namespace VendingManager.Core.Entities;

/// <summary>
/// Entidad history para Rendicion. Refleja las columnas más los campos de auditoría.
/// </summary>
public class RendicionHistory
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

    // --- Rendicion base columns ---
    [Required]
    [MaxLength(200)]
    public string Trabajador { get; set; } = string.Empty;

    public DateTime FechaInicio { get; set; } = DateTime.Now;

    public DateTime? FechaFin { get; set; }

    public RendicionEstado Estado { get; set; } = RendicionEstado.Abierta;

    [MaxLength(1000)]
    public string? Observaciones { get; set; }
}