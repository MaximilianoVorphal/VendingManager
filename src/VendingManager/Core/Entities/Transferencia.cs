using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using VendingManager.Shared.Enums;

namespace VendingManager.Core.Entities;

/// <summary>
/// Representa una transferencia de dinero a un trabajador para rendir cuentas.
/// </summary>
public class Transferencia
{
    [Key]
    public int Id { get; set; }

    public DateTime Fecha { get; set; } = DateTime.Now;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Monto { get; set; }

    [MaxLength(500)]
    public string? Descripcion { get; set; }

    [Required]
    [MaxLength(200)]
    public string Trabajador { get; set; } = string.Empty;

    /// <summary>
    /// Estado del ciclo de vida: Pendiente → EnUso → Conciliado.
    /// </summary>
    public TransferenciaEstado Estado { get; set; } = TransferenciaEstado.Pendiente;

    /// <summary>
    /// FK opcional a Rendicion cuando esta transferencia es rendida.
    /// </summary>
    public int? RendicionId { get; set; }
    public Rendicion? Rendicion { get; set; }

    /// <summary>
    /// FK opcional a AccountingPeriod cuando esta transferencia pertenece a un período contable.
    /// </summary>
    public int? PeriodoId { get; set; }
    public AccountingPeriod? AccountingPeriod { get; set; }

    /// <summary>
    /// FK opcional a MovimientoCaja cuando esta transferencia sale de caja.
    /// </summary>
    public int? MovimientoCajaId { get; set; }
    public MovimientoCaja? MovimientoCaja { get; set; }

    /// <summary>
    /// Compras vinculadas a esta transferencia.
    /// </summary>
    public List<Compra> Compras { get; set; } = new();

    /// <summary>
    /// Binary content of the transfer comprobante (varbinary(max)).
    /// </summary>
    public byte[]? ComprobanteImagen { get; set; }

    /// <summary>
    /// MIME type of the comprobante (e.g. image/jpeg, application/pdf).
    /// </summary>
    [MaxLength(100)]
    public string? ComprobanteImagenContentType { get; set; }

    /// <summary>
    /// Original file name of the uploaded comprobante (for display).
    /// </summary>
    [MaxLength(255)]
    public string? ComprobanteImagenFileName { get; set; }

    /// <summary>
    /// Indicates whether the transfer comprobante has been verified by the owner.
    /// Defaults to false; historic rows are explicitly unverified after migration.
    /// </summary>
    public bool Verificada { get; set; } = false;

    /// <summary>
    /// Concurrency token para optimistic concurrency.
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }
}