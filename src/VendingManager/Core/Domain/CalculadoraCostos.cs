namespace VendingManager.Core.Domain;

/// <summary>
/// Pure-math CPP (Costo Promedio Ponderado) operations.
/// No side effects, no DI, no ProductoCosto history-row logic — callers own the lifecycle.
///
/// Implements ADR-012: apply-purchase (weighted-avg add) and revert-purchase
/// (subtract with Math.Max(0) guard + reset-to-0 when stock &lt;= 0).
/// </summary>
public static class CalculadoraCostos
{
    /// <summary>
    /// Weighted-average addition: (currentPool + newValue) / newStock.
    /// Returns the new CPP; outputs the new stock via <paramref name="nuevoStock"/>.
    /// First-unit pricing: when old stock is 0, new CPP = cost of new units.
    /// </summary>
    public static decimal ApplyPurchase(
        int stockActual,
        decimal cppActual,
        int cantidad,
        decimal costoUnitario,
        out int nuevoStock)
    {
        decimal valorInventarioActual = stockActual * cppActual;
        decimal valorNuevaTransaccion = cantidad * costoUnitario;

        nuevoStock = stockActual + cantidad;

        if (nuevoStock > 0)
            return (valorInventarioActual + valorNuevaTransaccion) / nuevoStock;

        return cppActual;
    }

    /// <summary>
    /// Reverse a prior purchase: (currentPool - removedValue) / newStock.
    /// Returns the new CPP (capped at 0 via Math.Max); outputs new stock.
    /// Stock &lt;= 0 → both stock and CPP reset to 0 per ADR-012.
    /// </summary>
    public static decimal RevertPurchase(
        int stockActual,
        decimal cppActual,
        int cantidadARevertir,
        decimal costoUnitarioARevertir,
        out int nuevoStock)
    {
        decimal valorTotalActual = stockActual * cppActual;
        decimal valorARestar = cantidadARevertir * costoUnitarioARevertir;

        nuevoStock = stockActual - cantidadARevertir;

        if (nuevoStock > 0)
            return Math.Max(0, (valorTotalActual - valorARestar) / nuevoStock);

        nuevoStock = 0;
        return 0;
    }
}
