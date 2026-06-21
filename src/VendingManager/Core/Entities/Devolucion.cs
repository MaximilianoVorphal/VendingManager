using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VendingManager.Core.Entities;

/// <summary>
/// Represents a cash return (devolución) for a rendición or accounting period.
/// A Devolución posts an inverse (positive) MovimientoCaja to reflect money returned to the caja.
/// First slice: one Devolución per open period/rendición.
/// </summary>
public class Devolucion
{
    [Key]
    public int Id { get; set; }

    /// <summary>Amount returned. Always positive (money back into caja).</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Monto { get; set; }

    /// <summary>Date when the return was made.</summary>
    public DateTime Fecha { get; set; } = DateTime.Now;

    /// <summary>Worker who returned the money.</summary>
    [Required]
    [MaxLength(200)]
    public string Trabajador { get; set; } = string.Empty;

    /// <summary>
    /// FK to Rendicion (nullable). Used for the Rendicion API path.
    /// </summary>
    public int? RendicionId { get; set; }
    public Rendicion? Rendicion { get; set; }

    /// <summary>
    /// FK to AccountingPeriod (nullable). Primary linkage for the live /contabilidad UI.
    /// </summary>
    public int? PeriodoId { get; set; }
    public AccountingPeriod? AccountingPeriod { get; set; }

    /// <summary>
    /// FK to the inverse MovimientoCaja posted when this Devolucion was registered.
    /// Nullable for back-compat; linked inside the creation transaction for exactly-once semantics.
    /// </summary>
    public int? MovimientoCajaId { get; set; }
    public MovimientoCaja? MovimientoCaja { get; set; }

    /// <summary>Optional comprobante image path. Not required in first slice.</summary>
    public string? ComprobanteImagenPath { get; set; }

    /// <summary>Optional notes.</summary>
    [MaxLength(500)]
    public string? Observaciones { get; set; }
}
