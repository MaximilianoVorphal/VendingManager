using System.ComponentModel.DataAnnotations;

namespace VendingManager.Core.Entities;

/// <summary>
/// Snapshot del estado de un slot al momento de crear un template de recarga.
/// Captura el producto y cantidad inicial para cálculos precisos de agotamiento.
/// </summary>
public class SnapshotSlot
{
    [Key]
    public int Id { get; set; }

    public int PeriodoRecargaId { get; set; }
    public PeriodoRecarga PeriodoRecarga { get; set; } = null!;

    [Required]
    [MaxLength(10)]
    public string NumeroSlot { get; set; } = string.Empty;

    public int? ProductoId { get; set; }
    public Producto? Producto { get; set; }

    /// <summary>
    /// Cantidad de unidades al momento de la recarga
    /// </summary>
    public int CantidadInicial { get; set; }

    /// <summary>
    /// Capacidad máxima del slot
    /// </summary>
    public int CapacidadSlot { get; set; }
}
