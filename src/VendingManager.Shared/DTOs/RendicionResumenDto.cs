namespace VendingManager.Shared.DTOs;

/// <summary>
/// Resumen de conciliación de una rendición.
/// </summary>
public class RendicionResumenDto
{
    public int RendicionId { get; set; }

    /// <summary>Sum of all linked transferencia amounts.</summary>
    public decimal Transferido { get; set; }

    /// <summary>Sum of all linked compra monto totals.</summary>
    public decimal TotalCompras { get; set; }

    /// <summary>Sum of all linked gasto (MovimientoCaja) amounts.</summary>
    public decimal TotalGastos { get; set; }

    /// <summary>Transferido - (TotalCompras + TotalGastos).</summary>
    public decimal Diferencia { get; set; }
}