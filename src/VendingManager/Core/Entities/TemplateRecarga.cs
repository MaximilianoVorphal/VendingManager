using System.ComponentModel.DataAnnotations;

namespace VendingManager.Core.Entities;

/// <summary>
/// Template de Recarga - representa un ciclo de reposición completo
/// donde cada máquina puede tener su propio horario independiente.
/// Ejemplo: "Recarga Semana 2 Enero" con Máquina 23 (08:00-18:00) y Máquina 22 (10:00-16:00)
/// </summary>
public class TemplateRecarga
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Nombre { get; set; } = string.Empty; // "Recarga Nuevo 3"

    [MaxLength(500)]
    public string? Descripcion { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.Now;

    /// <summary>
    /// Lista de períodos, uno por cada máquina incluida en esta recarga
    /// </summary>
    public List<PeriodoRecarga> Periodos { get; set; } = new();
}

/// <summary>
/// Período específico de una máquina dentro de un template de recarga.
/// Define el rango exacto de fecha/hora para análisis de stockout.
/// </summary>
public class PeriodoRecarga
{
    [Key]
    public int Id { get; set; }

    public int TemplateRecargaId { get; set; }
    public TemplateRecarga Template { get; set; } = null!;

    public int MaquinaId { get; set; }
    public Maquina Maquina { get; set; } = null!;

    /// <summary>
    /// Fecha y hora de inicio del período (cuando se cargó la máquina)
    /// </summary>
    public DateTime FechaInicio { get; set; }

    /// <summary>
    /// Fecha y hora de fin del período (fin del ciclo de análisis)
    /// </summary>
    public DateTime FechaFin { get; set; }

    /// <summary>
    /// Snapshot del inventario de slots al momento de la recarga
    /// </summary>
    public List<SnapshotSlot> SnapshotSlots { get; set; } = new();
}
