using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VendingManager.Core.Entities;

/// <summary>
/// History entity for GastoRecurrente. Mirrors columns plus audit fields.
/// </summary>
public class GastoRecurrenteHistory
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

    // --- GastoRecurrente base columns ---
    [Required]
    [MaxLength(200)]
    public string Descripcion { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal MontoEstimado { get; set; }

    [Required]
    [MaxLength(50)]
    public string Categoria { get; set; } = "INTERNET";

    [Required]
    [MaxLength(20)]
    public string Tipo { get; set; } = "GASTO";

    public bool Activo { get; set; }
    public int? MaquinaId { get; set; }
    public DateTime FechaCreacion { get; set; }
}
