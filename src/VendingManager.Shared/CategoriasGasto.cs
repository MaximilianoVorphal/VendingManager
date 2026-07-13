namespace VendingManager.Shared;

/// <summary>
/// Single source of truth for gasto classification.
///
/// ESTABLECIDO POR: SDD consolidacion-financiera, Unit 2 (clasificacion-gastos).
///
/// Estructurales — movimientos de capital que NO son gastos operativos reales.
///   RETIRO_CAPITAL: pre-migration compat (migration RemoveRetiroCapitalFromCaja).
///   DEVOLUCION_RENDICION: devolución al trabajador de saldo no usado.
///
/// EsGastoOperativoReal() — extensión in-memory (NO traducible a SQL) para
///   objetos materializados. Para LINQ-to-Entities usar el set directamente:
///   .Where(m => !CategoriasGasto.Estructurales.Contains(m.Categoria))
/// </summary>
public static class CategoriasGasto
{
    /// <summary>
    /// Categorías estructurales de capital — no son gastos reales del fondo.
    /// Excluidas de totales de gastos y listados de rendición.
    /// OrdinalIgnoreCase para consistencia con los datos históricos.
    /// </summary>
    public static readonly IReadOnlySet<string> Estructurales =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RETIRO_CAPITAL",
            "DEVOLUCION_RENDICION"
        };

    /// <summary>
    /// True si el movimiento NO es un movimiento estructural de capital
    /// (es decir, sí es un gasto operativo real).
    /// SOLO para uso in-memory con objetos ya materializados.
    /// En LINQ-to-Entities usar !CategoriasGasto.Estructurales.Contains(categoria).
    /// </summary>
    public static bool EsGastoOperativoReal(string? categoria) =>
        !Estructurales.Contains(categoria ?? string.Empty);
}
