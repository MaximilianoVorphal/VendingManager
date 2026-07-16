using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using VendingManager.Core.Configuration;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared;
using VendingManager.Shared.Constants;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Helpers;

namespace VendingManager.Infrastructure.Services
{
public class SalesAnalyticsService : ISalesAnalyticsService
{
    private readonly ApplicationDbContext _context;
    private readonly IExcelExportService _excelExportService;
    private readonly IMemoryCache _cache;
    private readonly AnalyticsThresholds _thresholds;
    private readonly VendingConfig _config;

    public SalesAnalyticsService(
        ApplicationDbContext context,
        IExcelExportService excelExportService,
        IMemoryCache cache,
        IOptions<AnalyticsThresholds> thresholds,
        IOptions<VendingConfig> config)
    {
        _context = context;
        _excelExportService = excelExportService;
        _cache = cache;
        _thresholds = thresholds.Value;
        _config = config.Value;
    }

    /// <summary>
    /// Returns the computed end date for a period using the chain model.
    /// </summary>
    private async Task<DateTime> GetEndDateForPeriodoAsync(int maquinaId, DateTime fechaRecarga)
    {
        var nextRecarga = await _context.PeriodosRecarga
            .Where(p => p.MaquinaId == maquinaId && p.FechaRecarga > fechaRecarga)
            .OrderBy(p => p.FechaRecarga)
            .Select(p => (DateTime?)p.FechaRecarga)
            .FirstOrDefaultAsync();

        return nextRecarga ?? (fechaRecarga <= DateTime.Now
            ? (DateTime.Now < fechaRecarga.AddDays(90) ? DateTime.Now : fechaRecarga.AddDays(90))
            : fechaRecarga.AddDays(90));
    }

        public async Task<DashboardStats> GetDashboardStatsAsync(int maquinaId)
        {
            var key = $"SalesAnalyticsService:GetDashboardStatsAsync:M{maquinaId}";

            var stats = await _cache.GetOrCreateAsync(key, async (entry) =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);

                var result = new DashboardStats();
                var hoy = DateTime.Now.Date;
                var diff = (7 + (hoy.DayOfWeek - DayOfWeek.Monday)) % 7;
                var inicioSemana = hoy.AddDays(-1 * diff).Date;
                var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);

                var query = _context.Ventas.AsQueryable();
                if (maquinaId > 0)
                {
                    query = query.Where(v => v.MaquinaId == maquinaId);
                }

                result.Hoy = await GetPeriodoStats(query, hoy);
                result.Semana = await GetPeriodoStats(query, inicioSemana);
                result.Mes = await GetPeriodoStats(query, inicioMes);

                // Fetch Critical Stock Count: slots where StockActual <= per-slot StockMinimo
                var slotsQuery = _context.ConfiguracionSlots.Where(s => s.StockActual <= s.StockMinimo && s.ProductoId != 0);
                if (maquinaId > 0) slotsQuery = slotsQuery.Where(s => s.MaquinaId == maquinaId);
                result.CantidadStockCritico = await slotsQuery.CountAsync();

                return result;
            });

            return stats!;
        }

        private async Task<PeriodoStats> GetPeriodoStats(IQueryable<Venta> baseQuery, DateTime fechaDesde)
        {
            var q = baseQuery.Where(v => v.FechaLocal >= fechaDesde);

            var data = await q.GroupBy(x => 1).Select(g => new
            {
                Total = g.Sum(x => x.PrecioVenta),
                Pagado = g.Where(x => x.Pagado).Sum(x => x.PrecioVenta),
                Pendiente = g.Where(x => !x.Pagado).Sum(x => x.PrecioVenta),
                Count = g.Count()
            }).FirstOrDefaultAsync();

            if (data == null) return new PeriodoStats();

            return new PeriodoStats
            {
                VentaTotal = data.Total,
                PagadoTB = data.Pagado,
                Pendiente = data.Pendiente,
                CantidadVentas = data.Count
            };
        }

        public async Task<ReporteDto> GetReporteRangoAsync(DateTime inicio, DateTime fin, int maquinaId, bool includePhantom = false, int? templateId = null)
        {
            // Template-based reports should NOT be cached — data changes frequently during editing
            // and users expect to see changes immediately after SyncSlotProducto.
            if (templateId.HasValue && templateId.Value > 0)
            {
                return await BuildReporteAsync(inicio, fin, maquinaId, includePhantom, templateId.Value);
            }

            // No cache — analytics deben reflejar datos frescos siempre.
            // Si la query es lenta, se optimiza la query, no se esconde con cache.
            return await BuildReporteAsync(inicio, fin, maquinaId, includePhantom, null);
        }

        private async Task<ReporteDto> BuildReporteAsync(DateTime inicio, DateTime fin, int maquinaId, bool includePhantom, int? templateId)
        {
            var query = _context.Ventas
                .Include(v => v.Maquina)
                .Include(v => v.Producto)
                .AsQueryable();

            if (templateId.HasValue && templateId.Value > 0)
            {
                var periodos = await _context.PeriodosRecarga
                    .Where(p => p.TemplateRecargaId == templateId)
                    .ToListAsync();

                if (periodos.Any())
                {
                    var parameter = Expression.Parameter(typeof(Venta), "v");
                    Expression? body = null;

                    foreach (var p in periodos)
                    {
                        var maquinaIdP = p.MaquinaId;
                        var fechaRecarga = p.FechaRecarga;
                        var fechaFin = await GetEndDateForPeriodoAsync(maquinaIdP, fechaRecarga);
                        var maquinaEq = Expression.Equal(Expression.Property(parameter, nameof(Venta.MaquinaId)), Expression.Constant(maquinaIdP));
                        var fechaGte = Expression.GreaterThanOrEqual(Expression.Property(parameter, nameof(Venta.FechaLocal)), Expression.Constant(fechaRecarga));
                        var fechaLte = Expression.LessThanOrEqual(Expression.Property(parameter, nameof(Venta.FechaLocal)), Expression.Constant(fechaFin));

                        var range = Expression.AndAlso(maquinaEq, Expression.AndAlso(fechaGte, fechaLte));
                        body = body == null ? range : Expression.OrElse(body, range);
                    }

                    if (body != null)
                    {
                        var lambda = Expression.Lambda<Func<Venta, bool>>(body, parameter);
                        query = query.Where(lambda);
                    }
                }
                else
                {
                    query = query.Where(v => false);
                }
            }
            else
            {
                DateTime finAjustado = fin.Date.AddDays(1).AddTicks(-1);
                DateTime inicioAjustado = inicio.Date;
                query = query.Where(v => v.FechaLocal >= inicioAjustado && v.FechaLocal <= finAjustado);
            }

            if (maquinaId > 0)
            {
                query = query.Where(v => v.MaquinaId == maquinaId);
            }

            var listaVentas = await query
                .OrderByDescending(v => v.FechaLocal)
                .ToListAsync();

            var reporte = new ReporteDto
            {
                TotalVentas = listaVentas.Count,
                MontoTotal = listaVentas.Sum(v => v.PrecioVenta),
                MontoPagado = listaVentas.Where(v => v.Pagado).Sum(v => v.PrecioVenta),
                MontoPendiente = listaVentas.Where(v => !v.Pagado).Sum(v => v.PrecioVenta),
                MontoPhantom = listaVentas.Where(v => v.IdOrdenMaquina == VentaConstants.TbExtra || v.IdOrdenMaquina == VentaConstants.TbSinVenta).Sum(v => v.PrecioVenta),
                Detalle = new List<DetalleVentaDto>(),
                Fantasmas = new List<DetalleVentaDto>()
            };

            reporte.TotalVentas -= listaVentas.Count(v => v.IdOrdenMaquina == VentaConstants.TbExtra || v.IdOrdenMaquina == VentaConstants.TbSinVenta);
            reporte.MontoTotal -= reporte.MontoPhantom;
            reporte.MontoPagado -= reporte.MontoPhantom;

            foreach (var v in listaVentas)
            {
                bool esFantasma = (v.IdOrdenMaquina == VentaConstants.TbExtra || v.IdOrdenMaquina == VentaConstants.TbSinVenta);

                decimal costo = v.CostoVenta;
                if (costo == 0 && v.Producto != null) costo = v.Producto.CostoPromedio;

                decimal ganancia = v.Pagado ? v.PrecioVenta - costo : 0;

                var detalleDto = new DetalleVentaDto
                {
                    FechaRaw = v.FechaLocal,
                    Maquina = v.Maquina?.Nombre ?? "Desconocida",
                    IdInternoMaquina = v.Maquina?.IdInternoMaquina ?? "",
                    Slot = v.NumeroSlot,
                    Producto = v.Producto?.Nombre ?? "---",
                    Monto = v.PrecioVenta,
                    CostoUnitario = costo,
                    Ganancia = ganancia,
                    Estado = v.Pagado ? "Pagado" : "Pendiente"
                };

                if (esFantasma)
                {
                    reporte.Fantasmas.Add(detalleDto);
                }

                if (!esFantasma || includePhantom)
                {
                    reporte.Detalle.Add(detalleDto);
                }
            }

            reporte.GananciaTotal = reporte.Detalle.Sum(d => d.Ganancia);

            return reporte;
        }

        public async Task<InformeFinancieroDto> GetInformeFinancieroAsync(DateTime inicio, DateTime fin, int maquinaId)
        {
            DateTime finAjustado = fin.Date.AddDays(1).AddTicks(-1);
            DateTime inicioAjustado = inicio.Date;

            var queryVentas = _context.Ventas
                .Include(v => v.Producto)
                .Where(v => v.Pagado && v.FechaLocal >= inicioAjustado && v.FechaLocal <= finAjustado);

            if (maquinaId > 0) queryVentas = queryVentas.Where(v => v.MaquinaId == maquinaId);

            var listaVentas = await queryVentas.ToListAsync();

            // Phantoms (TB-EXTRA, TB-SIN-VENTA) no son ventas reales — excluir del cálculo financiero
            // (mismo criterio que BuildReporteAsync, que las pone aparte en reporte.Fantasmas)
            var ventasReales = listaVentas
                .Where(v => v.IdOrdenMaquina != VentaConstants.TbExtra && v.IdOrdenMaquina != VentaConstants.TbSinVenta)
                .ToList();

            decimal ingresosVentas = 0;
            decimal costoVentas = 0;

            foreach (var v in ventasReales)
            {
                ingresosVentas += v.PrecioVenta;
                decimal costo = v.CostoVenta;
                if (costo == 0 && v.Producto != null) costo = v.Producto.CostoPromedio;
                costoVentas += costo;
            }

            decimal gastosOperativos = 0;
            decimal mermasAbs = 0;
            if (maquinaId == 0)
            {
                // Categorías operacionales via CategoriasGasto.Operacionales —
                // single source of truth, shared with CajaBusinessService.
                gastosOperativos = await _context.MovimientosCaja
                    .Where(m => m.Fecha >= _config.CajaStartDate
                             && m.Fecha >= inicioAjustado
                             && m.Fecha <= finAjustado
                             && m.Monto < 0
                             && CategoriasGasto.Operacionales.Contains(m.Categoria))
                    .SumAsync(m => m.Monto);

                gastosOperativos = Math.Abs(gastosOperativos);

                // Mermas — same as CajaBusinessService:86
                var monthMermas = await _context.MovimientosCaja
                    .Where(m => m.Fecha >= _config.CajaStartDate
                             && m.Fecha >= inicioAjustado
                             && m.Fecha <= finAjustado
                             && m.Monto < 0
                             && m.Categoria == "MERMA")
                    .SumAsync(m => m.Monto);
                mermasAbs = Math.Abs(monthMermas);
            }

            return new InformeFinancieroDto
            {
                VentasTotales = ingresosVentas,
                CostoVentas = costoVentas,
                MargenBruto = ingresosVentas - costoVentas,
                GastosOperativos = gastosOperativos,
                UtilidadNeta = CategoriasGasto.CalcularUtilidadOperacional(
                    ingresosVentas - costoVentas, mermasAbs, gastosOperativos),
                MargenPorcentaje = ingresosVentas > 0 ? ((ingresosVentas - costoVentas) / ingresosVentas) * 100 : 0
            };
        }

        public async Task<(byte[] content, string fileName)> ExportarReporteAsync(DateTime inicio, DateTime fin, int maquinaId, bool includePhantom = false, int? templateId = null)
        {
            var reporte = await GetReporteRangoAsync(inicio, fin, maquinaId, includePhantom, templateId);

            if (reporte == null || reporte.Detalle.Count == 0)
                throw new InvalidOperationException("No hay datos para exportar en el rango seleccionado.");

            var bytes = await _excelExportService.ExportSalesReportAsync(reporte.Detalle, inicio, fin);
            string name = $"Reporte_{inicio:ddMMyy}_{fin:ddMMyy}.xlsx";
            return (bytes, name);
        }

        public async Task<List<AnalisisProductoDto>> GetAnalisisProductosAsync(DateTime inicio, DateTime fin, int maquinaId, bool includePendientes = false)
        {
            // No cache — analytics deben reflejar datos frescos siempre.
            DateTime finAjustado = fin.Date.AddDays(1).AddTicks(-1);
            DateTime inicioAjustado = inicio.Date;

            var allProducts = await _context.Productos.ToListAsync();

            // T8: Batch fetch ProductoCosto for the period to avoid N+1
            var costoRecords = await _context.ProductoCostos
                .Where(pc => pc.FechaDesde <= finAjustado && (pc.FechaHasta == null || pc.FechaHasta > inicioAjustado))
                .AsNoTracking()
                .ToListAsync();

            // Build lookup: productoId -> sorted dictionary (FechaDesde DESC) -> costo
            var costoLookup = costoRecords
                .GroupBy(pc => pc.ProductoId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(pc => pc.FechaDesde)
                          .DistinctBy(pc => pc.FechaDesde.Date)
                          .ToDictionary(pc => pc.FechaDesde.Date, pc => pc.Costo)
                );

            var query = _context.Ventas.Where(v => v.FechaLocal >= inicioAjustado && v.FechaLocal <= finAjustado);
            if (maquinaId > 0)
            {
                query = query.Where(v => v.MaquinaId == maquinaId);
            }

            // T8: Include FechaLocal for historical cost lookup
            var sales = await query.Select(v => new { v.ProductoId, v.PrecioVenta, v.CostoVenta, v.Producto, v.FechaLocal }).ToListAsync();

            // T8: Pre-calculate cost per sale using historical batch lookup
            var saleCosts = new List<(int productoId, decimal precio, decimal costo)>();
            foreach (var s in sales)
            {
                if (!s.ProductoId.HasValue) continue;
                decimal costo = s.CostoVenta;
                if (costo <= 0)
                {
                    if (costoLookup.TryGetValue(s.ProductoId.Value, out var productCostos))
                    {
                        var fechaVenta = s.FechaLocal.Date;
                        // Find most recent cost whose FechaDesde <= sale date
                        var match = productCostos
                            .Where(kv => kv.Key <= fechaVenta)
                            .OrderByDescending(kv => kv.Key)
                            .FirstOrDefault();
                        if (match.Key != default)
                            costo = match.Value;
                    }
                }
                if (costo <= 0 && s.Producto != null)
                    costo = s.Producto.CostoPromedio;
                saleCosts.Add((s.ProductoId.Value, s.PrecioVenta, costo));
            }

            // Group using pre-calculated costs
            var salesGrouped = saleCosts
                .GroupBy(s => s.productoId)
                .ToDictionary(g => g.Key, g => new
                {
                    Count = g.Count(),
                    TotalVentas = g.Sum(x => x.precio),
                    TotalCosto = g.Sum(x => x.costo)
                });

            // Pendientes / Desconocidos: ventas sin producto asignado
            int pendientesCount = 0;
            decimal pendientesVentas = 0;
            decimal pendientesCostos = 0;
            if (includePendientes)
            {
                var pendientes = sales.Where(s => !s.ProductoId.HasValue).ToList();
                pendientesCount = pendientes.Count;
                pendientesVentas = pendientes.Sum(x => x.PrecioVenta);
                pendientesCostos = pendientes.Sum(x => x.CostoVenta);
            }

            var resultInner = new List<AnalisisProductoDto>();

            double daysInPeriod = (finAjustado - inicioAjustado).TotalDays;
            if (daysInPeriod < 1) daysInPeriod = 1;

            foreach (var p in allProducts)
            {
                var dto = new AnalisisProductoDto
                {
                    ProductoId = p.Id,
                    Nombre = p.Nombre,
                    Codigo = !string.IsNullOrEmpty(p.SKU) ? p.SKU : p.CodigoBarras ?? "",
                    Categoria = p.Categoria ?? "General"
                };

                if (salesGrouped.TryGetValue(p.Id, out var stats))
                {
                    dto.CantidadVendida = stats.Count;
                    dto.TotalVentas = stats.TotalVentas;
                    var totalCosto = stats.TotalCosto;
                    dto.TotalGanancia = dto.TotalVentas - totalCosto;
                }
                else
                {
                    dto.CantidadVendida = 0;
                    dto.TotalVentas = 0;
                    dto.TotalGanancia = 0;
                }

                dto.RotacionDiaria = (decimal)(dto.CantidadVendida / daysInPeriod);

                decimal margenPct = dto.TotalVentas > 0 ? (dto.TotalGanancia / dto.TotalVentas) * 100 : 0;

                if (dto.RotacionDiaria >= _thresholds.RotacionAlta)
                {
                    dto.Clasificacion = "Estrella";
                }
                else if (dto.RotacionDiaria < _thresholds.RotacionMedia)
                {
                    if (margenPct >= _thresholds.MargenAlto)
                        dto.Clasificacion = "Joya";
                    else
                        dto.Clasificacion = "Cacho";
                }
                else
                {
                    dto.Clasificacion = "Normal";
                }

                resultInner.Add(dto);
            }

            // Agregar entrada de Pendientes / Desconocidos al final si hay
            if (includePendientes && pendientesCount > 0)
            {
                resultInner.Add(new AnalisisProductoDto
                {
                    ProductoId = 0,
                    Nombre = "Pendientes / Desconocidos",
                    Codigo = "",
                    Categoria = "Pendiente",
                    CantidadVendida = pendientesCount,
                    TotalVentas = pendientesVentas,
                    TotalGanancia = pendientesVentas - pendientesCostos,
                    RotacionDiaria = (decimal)(pendientesCount / daysInPeriod),
                    Clasificacion = "Pendiente"
                });
            }

            // T9: Clasificación ABC — post-aggregation in-memory
            var abcProducts = resultInner.Where(p => p.ProductoId > 0).OrderByDescending(p => p.TotalVentas).ToList();
            decimal totalVentas = abcProducts.Sum(p => p.TotalVentas);
            decimal cumulative = 0;
            foreach (var p in abcProducts)
            {
                cumulative += p.TotalVentas;
                p.PorcentajeAcumulado = totalVentas > 0
                    ? Math.Round((cumulative / totalVentas) * 100, 1)
                    : 0;
                p.ClasificacionABC = p.PorcentajeAcumulado switch
                {
                    <= 80m => "A",
                    <= 95m => "B",
                    _ => "C"
                };
            }

            // T10: Tendencias MoM/WoW — query previous period
            var duration = finAjustado - inicioAjustado;
            var prevInicio = inicioAjustado.AddDays(-duration.TotalDays);
            var prevFin = inicioAjustado.AddTicks(-1);

            var prevSales = await _context.Ventas
                .Where(v => v.FechaLocal >= prevInicio && v.FechaLocal <= prevFin)
                .Where(v => maquinaId == 0 || v.MaquinaId == maquinaId)
                .Where(v => v.ProductoId != null)
                .GroupBy(v => v.ProductoId!.Value)
                .Select(g => new { ProductoId = g.Key, TotalVentas = g.Sum(x => x.PrecioVenta) })
                .ToDictionaryAsync(x => x.ProductoId, x => x.TotalVentas);

            foreach (var dto in resultInner.Where(p => p.ProductoId > 0))
            {
                if (prevSales.TryGetValue(dto.ProductoId, out var prev) && prev > 0)
                {
                    dto.CambioPorcentual = Math.Round((dto.TotalVentas - prev) / prev * 100, 1);
                    dto.Tendencia = dto.CambioPorcentual > 0 ? "▲" : dto.CambioPorcentual < 0 ? "▼" : "→";
                }
                else
                {
                    dto.Tendencia = "—";
                }
            }

            return resultInner.OrderByDescending(x => x.CantidadVendida).ToList();
        }

        public async Task<List<StockoutAnalysisDto>> GetStockoutAnalysisAsync(
            DateTime inicio, DateTime fin, int maquinaId, double umbralHorasSilencio = 14)
        {
            DateTime finAjustado = fin.Date.AddDays(1).AddTicks(-1);
            DateTime inicioAjustado = inicio.Date;

            var query = _context.Ventas
                .Include(v => v.Maquina)
                .Include(v => v.Producto)
                .Where(v => v.FechaLocal >= inicioAjustado && v.FechaLocal <= finAjustado)
                .Where(v => v.IdOrdenMaquina != VentaConstants.TbExtra && v.IdOrdenMaquina != VentaConstants.TbSinVenta);

            if (maquinaId > 0)
            {
                query = query.Where(v => v.MaquinaId == maquinaId);
            }

            var ventas = await query.ToListAsync();

            if (!ventas.Any())
            {
                return new List<StockoutAnalysisDto>();
            }

            var ultimaActividadPorMaquina = ventas
                .GroupBy(v => v.MaquinaId)
                .ToDictionary(g => g.Key, g => g.Max(v => v.FechaLocal));

            var grupos = ventas
                .Where(v => v.ProductoId.HasValue && v.ProductoId > 0)
                .GroupBy(v => new { v.MaquinaId, v.ProductoId })
                .ToList();

            // T12: Detectar Dead Slots — slots configurados sin ventas en el período
            var slotsConfigurados = await _context.ConfiguracionSlots
                .Where(s => maquinaId == 0 || s.MaquinaId == maquinaId)
                .Where(s => s.ProductoId != null && s.ProductoId > 0)
                .Select(s => new { s.MaquinaId, s.NumeroSlot, s.ProductoId, s.StockActual, s.StockMinimo })
                .ToListAsync();

            var slotsConVentas = ventas
                .Where(v => v.NumeroSlot != null)
                .Select(v => v.NumeroSlot!)
                .ToHashSet();

            // Diccionario NumeroSlot → StockActual para Fill% y predicción (REQ-5/REQ-6)
            var slotStockLookup = slotsConfigurados
                .GroupBy(s => s.NumeroSlot)
                .ToDictionary(g => g.Key, g => g.First().StockActual);

            // T13: Para Fill% necesitamos el StockInicial — batch-fetch del snapshot de slot más reciente
            // SnapshotSlot → PeriodoRecarga → MaquinaId (navegación de relación)
            var snapshotSlots = await _context.SnapshotSlots
                .Include(ss => ss.PeriodoRecarga)
                .Where(ss => maquinaId == 0 || ss.PeriodoRecarga.MaquinaId == maquinaId)
                .Where(ss => ss.PeriodoRecarga.FechaRecarga <= finAjustado)
                .ToListAsync();

            var slotSnapshotLookup = snapshotSlots
                .GroupBy(ss => new { ss.PeriodoRecarga.MaquinaId, ss.NumeroSlot })
                .Select(g => g.OrderByDescending(ss => ss.PeriodoRecarga.FechaRecarga).First())
                .ToDictionary(
                    ss => (ss.PeriodoRecarga.MaquinaId, ss.NumeroSlot),
                    ss => ss.CantidadInicial
                );

            var result = new List<StockoutAnalysisDto>();

            foreach (var grupo in grupos)
            {
                var maquinaId_ = grupo.Key.MaquinaId;
                var productoId = grupo.Key.ProductoId!.Value;
                var ventasGrupo = grupo.ToList();

                var primeraVenta = ventasGrupo.Min(v => v.FechaLocal);
                var ultimaVenta = ventasGrupo.Max(v => v.FechaLocal);
                var ultimaActividadMaquina = ultimaActividadPorMaquina[maquinaId_];
                var cantidad = ventasGrupo.Count;

                var precioPromedio = ventasGrupo.Average(v => v.PrecioVenta);
                var costoPromedio = ventasGrupo.Average(v =>
                    v.CostoVenta > 0 ? v.CostoVenta : (v.Producto?.CostoPromedio ?? 0));
                var gananciaPromedio = precioPromedio - costoPromedio;

                var horasDiferencia = HorarioOperativoHelper.HorasEnRangoOperativo(ultimaVenta, ultimaActividadMaquina);
                var posibleQuiebre = horasDiferencia > umbralHorasSilencio;

                var fechaReferencia = posibleQuiebre ? ultimaActividadMaquina : finAjustado;
                var horasSinStock = HorarioOperativoHelper.HorasEnRangoOperativo(ultimaVenta, fechaReferencia);
                if (horasSinStock < 0) horasSinStock = 0;

                var horasActivas = HorarioOperativoHelper.HorasEnRangoOperativo(inicio, ultimaVenta);
                if (horasActivas < 1) horasActivas = 1;

                var velocidadPorHora = cantidad / (decimal)horasActivas;

                decimal dineroPerdido = 0;
                decimal gananciaPerdida = 0;

                if (posibleQuiebre && horasSinStock > 0)
                {
                    dineroPerdido = velocidadPorHora * (decimal)horasSinStock * precioPromedio;
                    gananciaPerdida = velocidadPorHora * (decimal)horasSinStock * gananciaPromedio;
                }

                var maquina = ventasGrupo.First().Maquina;
                var producto = ventasGrupo.First().Producto;
                var slots = ventasGrupo.Select(v => v.NumeroSlot).Distinct().ToList();

                // T13: Determinar StockInicial desde snapshot
                var stockInicial = 0;
                if (slots.Count == 1)
                {
                    var key = (maquinaId_, slots[0]!);
                    if (slotSnapshotLookup.TryGetValue(key, out var snapStock))
                        stockInicial = snapStock;
                }

                var dto = new StockoutAnalysisDto
                {
                    MaquinaId = maquinaId_,
                    MaquinaNombre = maquina?.Nombre ?? "Desconocida",
                    ProductoId = productoId,
                    ProductoNombre = producto?.Nombre ?? "Desconocido",
                    NumeroSlot = string.Join(", ", slots),

                    PrimeraVenta = primeraVenta,
                    UltimaVenta = ultimaVenta,
                    UltimaActividadMaquina = ultimaActividadMaquina,
                    FinReporte = finAjustado,

                    PosibleQuiebre = posibleQuiebre,
                    HorasSinStock = horasSinStock,

                    CantidadVendida = cantidad,
                    StockInicial = stockInicial,
                    StockActual = slotStockLookup.GetValueOrDefault(slots.FirstOrDefault() ?? "", 0),
                    HorasActivas = horasActivas,
                    VelocidadPorHora = Math.Round(velocidadPorHora, 4),

                    PrecioPromedioVenta = Math.Round(precioPromedio, 0),
                    GananciaPromedio = Math.Round(gananciaPromedio, 0),
                    DineroPerdidoEstimado = Math.Round(dineroPerdido, 0),
                    GananciaPerdidaEstimada = Math.Round(gananciaPerdida, 0)
                };

                // T13: Fill % y Predicción Stockout
                dto.FillPct = dto.StockInicial > 0
                    ? (int)Math.Round((double)dto.StockActual / dto.StockInicial * 100)
                    : -1;

                if (dto.StockInicial > 0 && dto.VelocidadDiaria > 0)
                {
                    dto.DiasHastaStockout = Math.Round((decimal)dto.StockActual / (decimal)dto.VelocidadDiaria, 1);
                }

                result.Add(dto);
            }

            // T12: Agregar Dead Slots — slots configurados que no tuvieron ventas en el período
            var productoIdsConVentas = grupos.Select(g => g.Key.ProductoId!.Value).ToHashSet();
            var deadSlots = slotsConfigurados.Where(s => !slotsConVentas.Contains(s.NumeroSlot)).ToList();

            // Batch-load product names for dead slots to avoid N+1 queries (was per-slot FindAsync)
            var deadSlotProductIds = deadSlots
                .Where(s => s.ProductoId > 0)
                .Select(s => s.ProductoId!.Value)
                .Distinct()
                .ToList();

            var productNameLookup = deadSlotProductIds.Count > 0
                ? await _context.Productos
                    .Where(p => deadSlotProductIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.Nombre)
                : new Dictionary<int, string>();

            foreach (var slot in deadSlots)
            {
                string productoNombre = "Desconocido";
                if (slot.ProductoId > 0)
                {
                    productoNombre = productNameLookup.GetValueOrDefault(slot.ProductoId!.Value, "Desconocido");
                }

                result.Add(new StockoutAnalysisDto
                {
                    MaquinaId = maquinaId > 0 ? maquinaId : slot.MaquinaId,
                    MaquinaNombre = "",
                    ProductoId = slot.ProductoId,
                    ProductoNombre = productoNombre,
                    NumeroSlot = slot.NumeroSlot,

                    EsDeadSlot = true,
                    StockActual = slot.StockActual,
                    PosibleQuiebre = slot.StockActual <= slot.StockMinimo,
                    FinReporte = finAjustado,
                    UltimaActividadMaquina = finAjustado
                });
            }

            return result
                .OrderByDescending(x => x.DineroPerdidoEstimado)
                .ThenByDescending(x => x.PosibleQuiebre)
                .ThenByDescending(x => x.HorasSinStock)
                .ToList();
        }

        // T11: Agregación por categoría
        public async Task<List<CategoriaAnalisisDto>> GetCategoriaAnalisisAsync(DateTime inicio, DateTime fin, int maquinaId)
        {
            DateTime finAjustado = fin.Date.AddDays(1).AddTicks(-1);
            DateTime inicioAjustado = inicio.Date;

            // Batch fetch ProductoCosto for accurate profit (same pattern as REQ-1)
            var costoRecords = await _context.ProductoCostos
                .Where(pc => pc.FechaDesde <= finAjustado && (pc.FechaHasta == null || pc.FechaHasta > inicioAjustado))
                .AsNoTracking()
                .ToListAsync();

            var costoLookup = costoRecords
                .GroupBy(pc => pc.ProductoId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(pc => pc.FechaDesde)
                          .DistinctBy(pc => pc.FechaDesde.Date)
                          .ToDictionary(pc => pc.FechaDesde.Date, pc => pc.Costo)
                );

            var query = _context.Ventas
                .Include(v => v.Producto)
                .Where(v => v.FechaLocal >= inicioAjustado && v.FechaLocal <= finAjustado)
                .Where(v => v.ProductoId != null && v.ProductoId > 0);

            if (maquinaId > 0)
                query = query.Where(v => v.MaquinaId == maquinaId);

            var sales = await query.ToListAsync();

            var categoryData = new Dictionary<string, (decimal totalVentas, decimal totalCosto, int cantidad)>();

            foreach (var v in sales)
            {
                var categoria = v.Producto?.Categoria ?? "Sin Categoría";
                var precio = v.PrecioVenta;

                decimal costo = v.CostoVenta;
                if (costo <= 0)
                {
                    if (costoLookup.TryGetValue(v.ProductoId ?? 0, out var productCostos))
                    {
                        var fechaVenta = v.FechaLocal.Date;
                        var match = productCostos
                            .Where(kv => kv.Key <= fechaVenta)
                            .OrderByDescending(kv => kv.Key)
                            .FirstOrDefault();
                        if (match.Key != default)
                            costo = match.Value;
                    }
                }
                if (costo <= 0 && v.Producto != null)
                    costo = v.Producto.CostoPromedio;

                if (categoryData.TryGetValue(categoria, out var existing))
                    categoryData[categoria] = (existing.totalVentas + precio, existing.totalCosto + costo, existing.cantidad + 1);
                else
                    categoryData[categoria] = (precio, costo, 1);
            }

            decimal totalGeneral = categoryData.Values.Sum(v => v.totalVentas);

            var result = categoryData.Select(kv => new CategoriaAnalisisDto
            {
                Nombre = kv.Key,
                TotalVentas = kv.Value.totalVentas,
                TotalGanancia = kv.Value.totalVentas - kv.Value.totalCosto,
                CantidadVendida = kv.Value.cantidad,
                PorcentajeDelTotal = totalGeneral > 0
                    ? Math.Round((kv.Value.totalVentas / totalGeneral) * 100, 1)
                    : 0
            }).OrderByDescending(c => c.TotalVentas).ToList();

            return result;
        }

        public async Task<List<VentaDiariaDto>> GetVentasDiariasAsync(int productoId, int maquinaId, DateTime inicio, DateTime fin)
        {
            DateTime finAjustado;
            if (fin.TimeOfDay.TotalSeconds == 0)
                finAjustado = fin.Date.AddDays(1).AddTicks(-1);
            else
                finAjustado = fin;

            DateTime inicioAjustado = inicio;

            var ventas = await _context.Ventas
                .Where(v => v.ProductoId == productoId)
                .Where(v => v.MaquinaId == maquinaId)
                .Where(v => v.FechaLocal >= inicioAjustado && v.FechaLocal <= finAjustado)
                .Where(v => v.IdOrdenMaquina != VentaConstants.TbExtra && v.IdOrdenMaquina != VentaConstants.TbSinVenta)
                .GroupBy(v => v.FechaLocal.Date)
                .Select(g => new VentaDiariaDto
                {
                    Fecha = g.Key,
                    Cantidad = g.Count()
                })
                .OrderBy(v => v.Fecha)
                .ToListAsync();

            return ventas;
        }
    }
}
