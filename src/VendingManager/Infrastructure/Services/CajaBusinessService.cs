using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VendingManager.Core.Configuration;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

namespace VendingManager.Infrastructure.Services;

/// <summary>
/// Servicio de lógica de negocio para operaciones de caja.
/// CajaService delega cálculos financieros y recopilación de datos de reportes a esta clase.
/// Registrado como tipo concreto en DI — sin interfaz pública.
/// </summary>
public class CajaBusinessService
{
    private readonly ApplicationDbContext _context;
    private readonly IVentaRepository _ventaRepository;
    private readonly IMaquinaRepository _maquinaRepository;
    private readonly IExcelExportService _excelExportService;
    private readonly IOptions<VendingConfig> _config;
    private readonly SalesAnalyticsService _salesAnalytics;

    public CajaBusinessService(
        ApplicationDbContext context,
        IVentaRepository ventaRepository,
        IMaquinaRepository maquinaRepository,
        IExcelExportService excelExportService,
        IOptions<VendingConfig> config,
        SalesAnalyticsService salesAnalytics)
    {
        _context = context;
        _ventaRepository = ventaRepository;
        _maquinaRepository = maquinaRepository;
        _excelExportService = excelExportService;
        _config = config;
        _salesAnalytics = salesAnalytics;
    }

    /// <summary>
    /// Calcula el resumen financiero completo para un mes/año dado.
    /// Cuando maquinaId es null, devuelve el resumen consolidado de flota (comportamiento actual).
    /// Cuando maquinaId > 0, devuelve el P&L específico de esa máquina (ingresos, costo, OPEX por máquina, depreciación).
    /// </summary>
    public async Task<CajaResumenDto> GetResumenAsync(int month, int year, int? maquinaId = null)
    {
        DateTime startOfMonth = new DateTime(year, month, 1);
        DateTime endOfMonth = startOfMonth.AddMonths(1).AddSeconds(-1);

        // 1. SALDO ANTERIOR — always fleet-level (bank balance)
        var prevIngresosVentas = await _ventaRepository.SumPrecioVentaPaidInRangeAsync(
            _config.Value.CajaStartDate, startOfMonth.AddSeconds(-1));

        var prevMovimientos = await _context.MovimientosCaja
            .Where(m => m.Fecha < startOfMonth && m.Fecha >= _config.Value.CajaStartDate)
            .SumAsync(m => m.Monto);

        decimal saldoAnterior = prevIngresosVentas + prevMovimientos;

        // 2. MONTH-LEVEL — fleet vs per-machine branch
        decimal monthIngresosVentas, monthCostoVenta, mermasAbs, gastosMercaderiaAbs;
        decimal gastosVariablesAbs, gastosFijosAbs, totalGastosOps;
        decimal monthGastos, monthAportes, utilidadOperacional, utilidadNetaReal;
        decimal depreciacionPeriodo = 0;
        bool isLocked = IsMonthLockedStatic(month, year);

        if (maquinaId > 0)
        {
            // ── PER-MACHINE BRANCH ──────────────────────────────────────────
            int mid = maquinaId.Value;

            // Ingresos por venta (filtered by MaquinaId)
            monthIngresosVentas = await _context.Ventas
                .Where(v => v.Pagado && v.FechaHora >= startOfMonth && v.FechaHora <= endOfMonth && v.MaquinaId == mid)
                .SumAsync(v => v.PrecioVenta);

            // Costo de venta (filtered by MaquinaId)
            monthCostoVenta = await _context.Ventas
                .Where(v => v.Pagado && v.FechaHora >= startOfMonth && v.FechaHora <= endOfMonth && v.MaquinaId == mid)
                .SumAsync(v => v.CostoVenta);

            // Mermas (filtered by MaquinaId)
            var monthMermas = await _context.MovimientosCaja
                .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate
                         && m.Monto < 0 && m.Categoria == "MERMA" && m.MaquinaId == mid)
                .SumAsync(m => m.Monto);
            mermasAbs = Math.Abs(monthMermas);

            // Gastos Mercaderia (filtered by MaquinaId)
            var monthGastosMercaderia = await _context.MovimientosCaja
                .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate
                         && m.Monto < 0 && m.Categoria == "MERCADERIA" && m.MaquinaId == mid)
                .SumAsync(m => m.Monto);
            gastosMercaderiaAbs = Math.Abs(monthGastosMercaderia);

            // OPEX per-machine (uses fleet proration for shared costs)
            (gastosFijosAbs, gastosVariablesAbs) = await _salesAnalytics.CalcularOpexPorMaquinaAsync(mid, startOfMonth, endOfMonth);
            totalGastosOps = gastosVariablesAbs + gastosFijosAbs;

            // Depreciation
            depreciacionPeriodo = await _salesAnalytics.CalcularDepreciacionAsync(mid, startOfMonth, endOfMonth);

            // All expenses/income (filtered by MaquinaId)
            monthGastos = await _context.MovimientosCaja
                .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate
                         && m.Monto < 0 && m.MaquinaId == mid)
                .SumAsync(m => m.Monto);

            monthAportes = await _context.MovimientosCaja
                .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate
                         && m.Monto > 0 && m.MaquinaId == mid)
                .SumAsync(m => m.Monto);

            // EBITDA
            decimal margenBruto = monthIngresosVentas - monthCostoVenta;
            utilidadOperacional = margenBruto - mermasAbs - totalGastosOps - depreciacionPeriodo;
            utilidadNetaReal = utilidadOperacional;
        }
        else
        {
            // ── FLEET-LEVEL BRANCH (unchanged) ─────────────────────────────
            monthIngresosVentas = await _ventaRepository.SumPrecioVentaPaidInRangeAsync(startOfMonth, endOfMonth);

            monthGastos = await _context.MovimientosCaja
                .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate && m.Monto < 0)
                .SumAsync(m => m.Monto);

            monthAportes = await _context.MovimientosCaja
                .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate && m.Monto > 0)
                .SumAsync(m => m.Monto);

            var monthCostoSum = await _ventaRepository.SumCostoVentaPaidInRangeAsync(startOfMonth, endOfMonth);
            monthCostoVenta = monthCostoSum;

            var monthGastosMercaderia = await _context.MovimientosCaja
                .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate && m.Monto < 0 && m.Categoria == "MERCADERIA")
                .SumAsync(m => m.Monto);

            var monthMermas = await _context.MovimientosCaja
                .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate && m.Monto < 0 && m.Categoria == "MERMA")
                .SumAsync(m => m.Monto);
            mermasAbs = Math.Abs(monthMermas);
            gastosMercaderiaAbs = Math.Abs(monthGastosMercaderia);

            var categoriesVariables = new[] { "LOGISTICA", "PEAJES", "INSUMOS", "MANTENCION" };
            var monthGastosVariables = await _context.MovimientosCaja
                 .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate && m.Monto < 0 && categoriesVariables.Contains(m.Categoria))
                 .SumAsync(m => m.Monto);
            gastosVariablesAbs = Math.Abs(monthGastosVariables);

            var categoriesFijos = new[] { "INFRA", "ARRIENDO_POS", "INTERNET", "COMISIONES", "SUELDOS", "GASTOS GENERALES", "OTROS", "SERVICIOS" };
            var monthGastosFijos = await _context.MovimientosCaja
                 .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate && m.Monto < 0 && categoriesFijos.Contains(m.Categoria))
                 .SumAsync(m => m.Monto);
            gastosFijosAbs = Math.Abs(monthGastosFijos);

            totalGastosOps = gastosVariablesAbs + gastosFijosAbs;

            decimal margenBruto = monthIngresosVentas - monthCostoVenta;
            utilidadOperacional = margenBruto - mermasAbs - totalGastosOps;
            utilidadNetaReal = utilidadOperacional;
        }

        // 3. TRANSBANK — fleet-level (not per-machine in this iteration)
        var excludedOrdenIds = new[] { "TB-EXTRA", "TB-SIN-VENTA" };
        var cantVentasTB = await _ventaRepository.CountPaidInRangeExcludingAsync(startOfMonth, endOfMonth, excludedOrdenIds);
        decimal costoTransbank = cantVentasTB * _config.Value.TransbankFee;

        decimal margenBrutoFinal = monthIngresosVentas - monthCostoVenta;

        return new CajaResumenDto
        {
            SaldoAnterior = saldoAnterior,
            IngresosVentas = monthIngresosVentas,
            GastosOperativos = totalGastosOps,
            AportesExtra = monthAportes,
            SaldoFinal = saldoAnterior + monthIngresosVentas + monthAportes + monthGastos,
            UtilidadTotal = margenBrutoFinal,
            GastosMercaderia = gastosMercaderiaAbs,
            TotalCostoVenta = monthCostoVenta,
            Mermas = mermasAbs,
            GastosVariables = gastosVariablesAbs,
            GastosFijos = gastosFijosAbs,
            UtilidadOperacional = utilidadOperacional,
            DepreciacionPeriodo = depreciacionPeriodo,
            UtilidadNeta = utilidadNetaReal,
            CantidadVentasTransbank = cantVentasTB,
            CostoTransbank = costoTransbank,
            IsLocked = isLocked
        };
    }

    /// <summary>
    /// Obtiene todos los movimientos de caja para un mes/año dado.
    /// </summary>
    public async Task<List<MovimientoCaja>> GetMovimientosAsync(int month, int year)
    {
        return await _context.MovimientosCaja
            .Where(m => m.Fecha.Month == month && m.Fecha.Year == year && m.Fecha >= _config.Value.CajaStartDate)
            .OrderByDescending(m => m.Fecha)
            .ToListAsync();
    }

    /// <summary>
    /// Obtiene todos los datos necesarios para el reporte de caja: resumen, movimientos y ventas pagadas en el rango.
    /// </summary>
    public async Task<(CajaResumenDto resumen, List<MovimientoCaja> movimientos, IReadOnlyList<Venta> ventas)> GetCajaReportDataAsync(int month, int year)
    {
        DateTime startOfMonth = new DateTime(year, month, 1);
        DateTime endOfMonth = startOfMonth.AddMonths(1).AddSeconds(-1);

        var resumen = await GetResumenAsync(month, year);
        var movimientos = await _context.MovimientosCaja
            .Where(m => m.Fecha.Month == month && m.Fecha.Year == year && m.Fecha >= _config.Value.CajaStartDate)
            .OrderBy(m => m.Fecha)
            .ToListAsync();
        var ventas = await _ventaRepository.GetPaidInRangeAsync(startOfMonth, endOfMonth);

        return (resumen, movimientos, ventas);
    }

    /// <summary>
    /// Calcula la valorización de stock (bodega + máquinas).
    /// </summary>
    public async Task<ValorizacionStockDto> GetValorizacionStockAsync()
    {
        // 1. Bodega: Sum(StockBodega * CostoPromedio)
        var valorBodega = await _context.Productos
            .SumAsync(p => p.StockBodega * p.CostoPromedio);

        // 2. Maquinas: Sum(StockActual * CostoPromedio)
        var valorMaquinas = await _context.ConfiguracionSlots
            .Include(s => s.Producto)
            .Where(s => s.ProductoId != null && s.Producto != null)
            .SumAsync(s => s.StockActual * s.Producto!.CostoPromedio);

        return new ValorizacionStockDto
        {
            ValorBodega = valorBodega,
            ValorMaquinas = valorMaquinas
        };
    }

    /// <summary>
    /// Helper estático — misma lógica de bloqueo que CajaService.IsMonthLocked.
    /// </summary>
    private static bool IsMonthLockedStatic(int month, int year)
    {
        DateTime now = DateTime.Now;
        DateTime targetDateEnd = new DateTime(year, month, 1).AddMonths(1).AddSeconds(-1);
        if (targetDateEnd >= now) return false;
        DateTime lockDate = targetDateEnd.AddDays(5);
        return false; // Actualmente deshabilitado en el original
    }
}