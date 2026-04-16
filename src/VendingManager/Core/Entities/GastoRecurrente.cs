using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VendingManager.Core.Entities;

/// <summary>
/// Representa un gasto fijo mensual que se repite cada mes.
/// Ejemplo: chip de máquina, bencina mensual estimada, arriendo POS.
/// El sistema alertará si no se ha registrado en el mes actual.
/// </summary>
public class GastoRecurrente
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Descripcion { get; set; } = string.Empty; // "Chip M-0023", "Bencina mensual"

    [Column(TypeName = "decimal(18,2)")]
    public decimal MontoEstimado { get; set; } // Monto fijo mensual estimado

    [Required]
    [MaxLength(50)]
    public string Categoria { get; set; } = "INTERNET"; // Mismas categorías de MovimientoCaja

    [Required]
    [MaxLength(20)]
    public string Tipo { get; set; } = "GASTO"; // GASTO (siempre será gasto)

    public bool Activo { get; set; } = true; // Para desactivar sin borrar

    public int? MaquinaId { get; set; } // Opcional: vincular a una máquina específica

    public DateTime FechaCreacion { get; set; } = DateTime.Now;
}
