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

    public CajaBusinessService(
        ApplicationDbContext context,
        IVentaRepository ventaRepository,
        IMaquinaRepository maquinaRepository,
        IExcelExportService excelExportService,
        IOptions<VendingConfig> config)
    {
        _context = context;
        _ventaRepository = ventaRepository;
        _maquinaRepository = maquinaRepository;
        _excelExportService = excelExportService;
        _config = config;
    }

    /// <summary>
    /// Calcula el resumen financiero completo para un mes/año dado.
    /// </summary>
    public async Task<CajaResumenDto> GetResumenAsync(int month, int year)
    {
        DateTime startOfMonth = new DateTime(year, month, 1);
        DateTime endOfMonth = startOfMonth.AddMonths(1).AddSeconds(-1);

        // 1. SALDO ANTERIOR
        var prevIngresosVentas = await _ventaRepository.SumPrecioVentaPaidInRangeAsync(
            _config.Value.CajaStartDate, startOfMonth.AddSeconds(-1));

        var prevMovimientos = await _context.MovimientosCaja
            .Where(m => m.Fecha < startOfMonth && m.Fecha >= _config.Value.CajaStartDate)
            .SumAsync(m => m.Monto);

        decimal saldoAnterior = prevIngresosVentas + prevMovimientos;

        // 2. MOVIMIENTOS DEL MES
        var monthIngresosVentas = await _ventaRepository.SumPrecioVentaPaidInRangeAsync(startOfMonth, endOfMonth);

        var monthGastos = await _context.MovimientosCaja
            .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate && m.Monto < 0)
            .SumAsync(m => m.Monto);

        var monthAportes = await _context.MovimientosCaja
            .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate && m.Monto > 0)
            .SumAsync(m => m.Monto);

        // 3. UTILIDAD (PrecioVenta - CostoVenta)
        var monthPrecioSum = await _ventaRepository.SumPrecioVentaPaidInRangeAsync(startOfMonth, endOfMonth);
        var monthCostoSum = await _ventaRepository.SumCostoVentaPaidInRangeAsync(startOfMonth, endOfMonth);
        var monthUtilidad = monthPrecioSum - monthCostoSum;

        // 4. GASTOS MERCADERIA (Categoria "MERCADERIA")
        var monthGastosMercaderia = await _context.MovimientosCaja
            .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate && m.Monto < 0 && m.Categoria == "MERCADERIA")
            .SumAsync(m => m.Monto);

        // COSTO DE VENTA
        var monthCostoVenta = monthCostoSum;

        // MERMAS (Categoria "MERMA")
        var monthMermas = await _context.MovimientosCaja
            .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate && m.Monto < 0 && m.Categoria == "MERMA")
            .SumAsync(m => m.Monto);
        decimal mermasAbs = Math.Abs(monthMermas);

        decimal gastosMercaderiaAbs = Math.Abs(monthGastosMercaderia);

        // GASTOS VARIABLES (Logística)
        var categoriesVariables = new[] { "LOGISTICA", "PEAJES", "INSUMOS", "MANTENCION" };
        var monthGastosVariables = await _context.MovimientosCaja
             .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate && m.Monto < 0 && categoriesVariables.Contains(m.Categoria))
             .SumAsync(m => m.Monto);
        decimal gastosVariablesAbs = Math.Abs(monthGastosVariables);

        // GASTOS FIJOS (Estructurales)
        var categoriesFijos = new[] { "INFRA", "ARRIENDO_POS", "INTERNET", "COMISIONES", "SUELDOS", "GASTOS GENERALES", "OTROS" };
         var monthGastosFijos = await _context.MovimientosCaja
             .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate && m.Monto < 0 && categoriesFijos.Contains(m.Categoria))
             .SumAsync(m => m.Monto);
        decimal gastosFijosAbs = Math.Abs(monthGastosFijos);

        // UTILIDAD OPERACIONAL (EBITDA)
        decimal ventasNetas = monthIngresosVentas - mermasAbs;
        decimal margenBruto = (monthIngresosVentas - monthCostoVenta);
        decimal totalGastosOps = gastosVariablesAbs + gastosFijosAbs;
        decimal utilidadOperacional = margenBruto - mermasAbs - totalGastosOps;
        decimal utilidadNetaReal = utilidadOperacional;

        // TRANSBANK (Estimado)
        var excludedOrdenIds = new[] { "TB-EXTRA", "TB-SIN-VENTA" };
        var cantVentasTB = await _ventaRepository.CountPaidInRangeExcludingAsync(startOfMonth, endOfMonth, excludedOrdenIds);
        decimal costoTransbank = cantVentasTB * _config.Value.TransbankFee;

        // Verificación de IsLocked usando método estático de CajaService
        bool isLocked = IsMonthLockedStatic(month, year);

        return new CajaResumenDto
        {
            SaldoAnterior = saldoAnterior,
            IngresosVentas = monthIngresosVentas,
            GastosOperativos = totalGastosOps,
            AportesExtra = monthAportes,
            SaldoFinal = saldoAnterior + monthIngresosVentas + monthAportes + monthGastos,
            UtilidadTotal = margenBruto,
            GastosMercaderia = gastosMercaderiaAbs,
            TotalCostoVenta = monthCostoVenta,
            Mermas = mermasAbs,
            GastosVariables = gastosVariablesAbs,
            GastosFijos = gastosFijosAbs,
            UtilidadOperacional = utilidadOperacional,
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