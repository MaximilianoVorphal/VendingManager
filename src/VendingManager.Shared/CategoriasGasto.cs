namespace VendingManager.Shared;

/// <summary>
/// Single source of truth for gasto classification.
///
/// ESTABLECIDO POR: SDD consolidacion-financiera, Unit 2 (clasificacion-gastos) + Unit 3 (P&amp;L).
///
/// Estructurales — movimientos de capital que NO son gastos operativos reales.
///   RETIRO_CAPITAL: pre-migration compat (migration RemoveRetiroCapitalFromCaja).
///   DEVOLUCION_RENDICION: devolución al trabajador de saldo no usado.
///
/// Variables — gastos asociados a la operación logística diaria.
/// Fijos — gastos estructurales recurrentes (infraestructura, personal, comisiones).
/// Operacionales = Variables ∪ Fijos — el conjunto completo de gastos operativos.
///
/// CalcularUtilidadOperacional() — fórmula unificada para CajaBusinessService y
///   SalesAnalyticsService: margenBruto - mermasAbs - totalGastosOps.
///
/// EsGastoOperativoReal() — extensión in-memory para objetos materializados.
///   Para LINQ-to-Entities usar el HashSet directamente (ICollection<T>.Contains()
///   es traducible a SQL IN):
///   .Where(m => !CategoriasGasto.Estructurales.Contains(m.Categoria))
/// </summary>
public static class CategoriasGasto
{
    /// <summary>
    /// Categorías estructurales de capital — no son gastos reales del fondo.
    /// Excluidas de totales de gastos y listados de rendición.
    /// OrdinalIgnoreCase para consistencia con los datos históricos.
    /// </summary>
    public static readonly HashSet<string> Estructurales =
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

    // ── P&amp;L buckets (Unit 3) ───────────────────────────────────────────

    /// <summary>Gastos variables: logística operativa diaria.</summary>
    public static readonly HashSet<string> Variables =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LOGISTICA", "PEAJES", "INSUMOS", "MANTENCION"
        };

    /// <summary>Gastos fijos: infraestructura, personal, comisiones.</summary>
    public static readonly HashSet<string> Fijos =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "INFRA", "ARRIENDO_POS", "INTERNET", "COMISIONES",
            "SUELDOS", "GASTOS GENERALES", "OTROS", "SERVICIOS"
        };

    /// <summary>Variables ∪ Fijos — el conjunto completo de categorías operacionales.</summary>
    public static readonly HashSet<string> Operacionales =
        new HashSet<string>(Variables.Concat(Fijos), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fórmula unificada de utilidad operacional para CajaBusinessService y
    /// SalesAnalyticsService. Las mermas se restan del margen bruto, consistente
    /// con CajaBusinessService:105.
    /// </summary>
    public static decimal CalcularUtilidadOperacional(
        decimal margenBruto,
        decimal mermasAbs,
        decimal totalGastosOps) =>
        margenBruto - mermasAbs - totalGastosOps;
}
