using System.ComponentModel.DataAnnotations;
using VendingManager.Shared.Enums;

namespace VendingManager.Core.Entities;

/// <summary>
/// Representa un período contable que agrupa transferencias,
/// compras y gastos para un lapso de tiempo definido.
/// Reemplaza el enfoque de rendiciones por trabajador con un
/// enfoque de períodos de tiempo independientes del trabajador.
/// </summary>
public class AccountingPeriod
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Nombre descriptivo del período (ej: "Enero 2026", "Q1 2026").
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Fecha de inicio del período contable.
    /// </summary>
    public DateTime FechaInicio { get; set; }

    /// <summary>
    /// Fecha de fin del período contable.
    /// </summary>
    public DateTime FechaFin { get; set; }

    /// <summary>
    /// Estado del período: Abierto o Cerrado.
    /// </summary>
    public AccountingPeriodEstado Estado { get; set; } = AccountingPeriodEstado.Abierto;

    /// <summary>
    /// Trabajador asociado opcional (para filtrado/migración).
    /// </summary>
    [MaxLength(200)]
    public string? Trabajador { get; set; }

    /// <summary>
    /// Transferencias vinculadas a este período contable.
    /// </summary>
    public List<Transferencia> Transferencias { get; set; } = new();

    /// <summary>
    /// Devoluciones registradas para este período contable.
    /// </summary>
    public List<Devolucion> Devoluciones { get; set; } = new();
}
