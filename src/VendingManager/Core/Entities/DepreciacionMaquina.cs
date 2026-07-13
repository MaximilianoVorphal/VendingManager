using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VendingManager.Core.Entities;

/// <summary>
/// Tracks CAPEX depreciation parameters per machine.
/// Linear (straight-line) depreciation, prorated by operational days.
/// </summary>
public class DepreciacionMaquina
{
    [Key]
    public int Id { get; set; }

    /// <summary>FK to Maquina — no navigation property (keep it lean).</summary>
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

    public bool Activo { get; set; } = true;

    public DateTime FechaCreacion { get; set; } = DateTime.Now;
}
