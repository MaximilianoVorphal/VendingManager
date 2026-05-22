using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using VendingManager.Shared.Enums;

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
    /// Estado del ciclo de vida del template.
    /// - Pendiente (0): Template en desarrollo, slots siendo configurados. No feed stock-critico.
    /// - Terminado (1): Template completado. Fuente para stock-critico (más reciente por máquina).
    /// Defaults to Pendiente for new instances.
    /// </summary>
    public EstadoTemplate Estado { get; set; } = EstadoTemplate.Pendiente;

    /// <summary>
    /// Concurrency token para optimistic concurrency en transiciones de estado.
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

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
    /// Single anchor date — the recharge timestamp for this machine.
    /// End date is derived: next period's FechaRecarga (or 2099-12-31 sentinel).
    /// Note: FechaFin is a SQL Server PERSISTED computed column, NOT a stored field.
    /// </summary>
    public DateTime FechaRecarga { get; set; }

    // NOTE: FechaFin is REMOVED from the C# entity — it is a SQL PERSISTED computed column.
    // EF Core will NOT map it (no property), SQL Server derives it on write.
    // Reads via raw SQL or helper methods for the computed value.

    /// <summary>
    /// Snapshot del inventario de slots al momento de la recarga
    /// </summary>
    public List<SnapshotSlot> SnapshotSlots { get; set; } = new();

    /// <summary>
    /// Foto guía — imagen de referencia de la estantería para esta máquina.
    /// Almacenada como varbinary(max), hasta 10 MB.
    /// </summary>
    public byte[]? FotoGuia { get; set; }

    /// <summary>
    /// Foto OCR — imagen capturada durante el proceso de recarga para esta máquina.
    /// Almacenada como varbinary(max), hasta 5 MB.
    /// </summary>
    public byte[]? FotoOcr { get; set; }
}