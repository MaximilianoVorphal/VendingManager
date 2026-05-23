using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using VendingManager.Shared.Enums;

namespace VendingManager.Core.Entities;

/// <summary>
/// Representa una rendición de gastos asociada a una transferencia.
/// Agrupa una transferencia con N compras y N gastos de caja.
/// </summary>
public class Rendicion
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Trabajador { get; set; } = string.Empty;

    public DateTime FechaInicio { get; set; } = DateTime.Now;

    public DateTime? FechaFin { get; set; }

    /// <summary>
    /// Estado del ciclo de vida: Abierta → Cerrada.
    /// </summary>
    public RendicionEstado Estado { get; set; } = RendicionEstado.Abierta;

    [MaxLength(1000)]
    public string? Observaciones { get; set; }

    /// <summary>
    /// Transferencias vinculadas a esta rendición.
    /// </summary>
    public List<Transferencia> Transferencias { get; set; } = new();

    /// <summary>
    /// Gastos (MovimientosCaja) vinculados a esta rendición.
    /// </summary>
    public List<MovimientoCaja> Gastos { get; set; } = new();

    /// <summary>
    /// Concurrency token para optimistic concurrency.
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }
}