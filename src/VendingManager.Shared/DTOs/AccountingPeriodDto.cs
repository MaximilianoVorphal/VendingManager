using VendingManager.Shared.Enums;

namespace VendingManager.Shared.DTOs;

/// <summary>
/// DTO base para un período contable.
/// </summary>
public class AccountingPeriodDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    public AccountingPeriodEstado Estado { get; set; }
    public string? Trabajador { get; set; }
    public decimal TotalTransferido { get; set; }
    public decimal TotalCompras { get; set; }
    public decimal TotalGastos { get; set; }
    public decimal Diferencia => TotalTransferido - TotalCompras - TotalGastos;

    /// <summary>
    /// Sum of all Devolucion.Monto for this period. Set by the service mapper.
    /// </summary>
    public decimal Devuelto { get; set; }

    /// <summary>
    /// Outstanding balance to be returned. Derived from the single Diferencia source.
    /// SaldoADevolver = Diferencia − Devuelto.
    /// </summary>
    public decimal SaldoADevolver => Diferencia - Devuelto;
}

/// <summary>
/// DTO completo con todas las entidades vinculadas.
/// </summary>
public class AccountingPeriodFullDto : AccountingPeriodDto
{
    public List<TransferenciaDto> Transferencias { get; set; } = new();
    public List<MovimientoCajaDto> Gastos { get; set; } = new();
}

/// <summary>
/// Request para crear un nuevo período contable.
/// </summary>
public class CreatePeriodoRequest
{
    public string Name { get; set; } = string.Empty;
    public DateTime FechaInicio { get; set; } = DateTime.Today;
    public DateTime FechaFin { get; set; }
    public string? Trabajador { get; set; }
}

/// <summary>
/// Request para actualizar un período contable existente.
/// </summary>
public class UpdatePeriodoRequest
{
    public string? Name { get; set; }
    public DateTime? FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }
    public string? Trabajador { get; set; }
}
