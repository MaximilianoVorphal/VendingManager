using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VendingManager.Core.Entities;

/// <summary>
/// Entidad history para DepreciacionMaquina. Refleja las columnas más los campos de auditoría.
/// </summary>
public class DepreciacionMaquinaHistory
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

    // --- DepreciacionMaquina base columns ---
    public int MaquinaId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Descripcion { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal ValorAdquisicion { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ValorResidual { get; set; }

    public int VidaUtilMeses { get; set; }

    public DateTime FechaAdquisicion { get; set; }

    [Required]
    [MaxLength(50)]
    public string MetodoDepreciacion { get; set; } = "LINEAL";

    public bool Activo { get; set; }

    public DateTime FechaCreacion { get; set; }
}
