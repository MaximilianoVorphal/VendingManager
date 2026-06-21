using VendingManager.Shared.DTOs;

namespace VendingManager.Web.Pages.Contabilidad.State;

/// <summary>
/// State container for the ContabilidadPage (period-based accounting view).
/// Replaces the old 5-step wizard state with a period-selection model.
/// </summary>
public class ContabilidadPageState
{
    /// <summary>Lista de períodos disponibles (filtrada por fecha si aplica).</summary>
    public List<AccountingPeriodDto> Periodos { get; set; } = new();

    /// <summary>Período actualmente seleccionado (DTO base).</summary>
    public AccountingPeriodDto? PeriodoActivo { get; set; }

    /// <summary>Datos completos del período activo (con transferencias, compras, gastos).</summary>
    public AccountingPeriodFullDto? PeriodoActivoFull { get; set; }

    /// <summary>Filtro de fecha desde para la lista de períodos.</summary>
    public DateTime? FiltroDesde { get; set; }

    /// <summary>Filtro de fecha hasta para la lista de períodos.</summary>
    public DateTime? FiltroHasta { get; set; }

    // Convenience properties for computed values
    public decimal TotalTransferido => PeriodoActivoFull?.TotalTransferido ?? PeriodoActivo?.TotalTransferido ?? 0;
    public decimal TotalCompras => PeriodoActivoFull?.TotalCompras ?? PeriodoActivo?.TotalCompras ?? 0;
    public decimal TotalGastos => PeriodoActivoFull?.TotalGastos ?? PeriodoActivo?.TotalGastos ?? 0;
    public decimal Diferencia => PeriodoActivoFull?.Diferencia ?? PeriodoActivo?.Diferencia ?? 0;

    /// <summary>Sum of all registered Devoluciones for the active period.</summary>
    public decimal Devuelto => PeriodoActivoFull?.Devuelto ?? PeriodoActivo?.Devuelto ?? 0;

    /// <summary>Outstanding balance to return. Derived: Diferencia − Devuelto.</summary>
    public decimal SaldoADevolver => PeriodoActivoFull?.SaldoADevolver ?? PeriodoActivo?.SaldoADevolver ?? 0;

    /// <summary>
    /// True when all comprobantes are verified AND SaldoADevolver == 0.
    /// Gates the cuadrar/close action on the UI.
    /// </summary>
    public bool CanCuadrar
    {
        get
        {
            if (PeriodoActivoFull == null) return false;
            if (SaldoADevolver != 0) return false;

            // All transferencias must be verified
            if (PeriodoActivoFull.Transferencias.Any(t => !t.Verificada))
                return false;

            // All compras (across all transferencias) must be verified
            if (PeriodoActivoFull.Transferencias
                    .SelectMany(t => t.Compras)
                    .Any(c => !c.Verificada))
                return false;

            return true;
        }
    }

    public List<TransferenciaDto> Transferencias => PeriodoActivoFull?.Transferencias ?? new();
    public List<MovimientoCajaDto> Gastos => PeriodoActivoFull?.Gastos ?? new();

    public List<CompraDto> Compras
    {
        get
        {
            if (PeriodoActivoFull == null) return new();
            return PeriodoActivoFull.Transferencias
                .SelectMany(t => t.Compras)
                .ToList();
        }
    }

    public void Limpiar()
    {
        Periodos.Clear();
        PeriodoActivo = null;
        PeriodoActivoFull = null;
        FiltroDesde = null;
        FiltroHasta = null;
    }

    public void Dispose() { }
}
