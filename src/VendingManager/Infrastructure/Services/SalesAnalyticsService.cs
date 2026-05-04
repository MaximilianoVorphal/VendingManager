using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.DTOs;

namespace VendingManager.Infrastructure.Services
{
    public class SalesAnalyticsService : ISalesAnalyticsService
    {
        private readonly ApplicationDbContext _context;

        public SalesAnalyticsService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<DashboardStats> GetDashboardStatsAsync(int maquinaId)
        {
            var stats = new DashboardStats();
            var hoy = DateTime.Now.Date;
            var diff = (7 + (hoy.DayOfWeek - DayOfWeek.Monday)) % 7;
            var inicioSemana = hoy.AddDays(-1 * diff).Date;
            var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);

            var query = _context.Ventas.AsQueryable();
            if (maquinaId > 0)
            {
                query = query.Where(v => v.MaquinaId == maquinaId);
            }

            stats.Hoy = await GetPeriodoStats(query, hoy);
            stats.Semana = await GetPeriodoStats(query, inicioSemana);
            stats.Mes = await GetPeriodoStats(query, inicioMes);

            // Fetch Critical Stock Count: slots where StockActual <= per-slot StockMinimo
            var slotsQuery = _context.ConfiguracionSlots.Where(s => s.StockActual <= s.StockMinimo && s.ProductoId != 0);
            if (maquinaId > 0) slotsQuery = slotsQuery.Where(s => s.MaquinaId == maquinaId);
            stats.CantidadStockCritico = await slotsQuery.CountAsync();

            return stats;
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
                        var maquinaEq = Expression.Equal(Expression.Property(parameter, nameof(Venta.MaquinaId)), Expression.Constant(p.MaquinaId));
                        var fechaGte = Expression.GreaterThanOrEqual(Expression.Property(parameter, nameof(Venta.FechaLocal)), Expression.Constant(p.FechaInicio));
                        var fechaLte = Expression.LessThanOrEqual(Expression.Property(parameter, nameof(Venta.FechaLocal)), Expression.Constant(p.FechaFin));

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
                MontoPhantom = listaVentas.Where(v => v.IdOrdenMaquina == "TB-EXTRA" || v.IdOrdenMaquina == "TB-SIN-VENTA").Sum(v => v.PrecioVenta),
                Detalle = new List<DetalleVentaDto>(),
                Fantasmas = new List<DetalleVentaDto>()
            };

            reporte.TotalVentas -= listaVentas.Count(v => v.IdOrdenMaquina == "TB-EXTRA" || v.IdOrdenMaquina == "TB-SIN-VENTA");
            reporte.MontoTotal -= reporte.MontoPhantom;
            reporte.MontoPagado -= reporte.MontoPhantom;

            foreach (var v in listaVentas)
            {
                bool esFantasma = (v.IdOrdenMaquina == "TB-EXTRA" || v.IdOrdenMaquina == "TB-SIN-VENTA");

                decimal costo = v.CostoVenta;
                if (costo == 0 && v.Producto != null) costo = v.Producto.CostoPromedio;

                decimal ganancia = v.PrecioVenta - costo;

                var detalleDto = new DetalleVentaDto
                {
                    FechaRaw = v.FechaLocal,
                    Maquina = v.Maquina?.Nombre ?? "Desconocida",
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

            decimal ingresosVentas = 0;
            decimal costoVentas = 0;

            foreach (var v in listaVentas)
            {
                ingresosVentas += v.PrecioVenta;
                decimal costo = v.CostoVenta;
                if (costo == 0 && v.Producto != null) costo = v.Producto.CostoPromedio;
                costoVentas += costo;
            }

            decimal gastosOperativos = 0;
            if (maquinaId == 0)
            {
                gastosOperativos = await _context.MovimientosCaja
                    .Where(m => m.Fecha >= inicioAjustado && m.Fecha <= finAjustado && m.Monto < 0)
                    .SumAsync(m => m.Monto);

                gastosOperativos = Math.Abs(gastosOperativos);
            }

            return new InformeFinancieroDto
            {
                VentasTotales = ingresosVentas,
                CostoVentas = costoVentas,
                MargenBruto = ingresosVentas - costoVentas,
                GastosOperativos = gastosOperativos,
                UtilidadNeta = (ingresosVentas - costoVentas) - gastosOperativos,
                MargenPorcentaje = ingresosVentas > 0 ? ((ingresosVentas - costoVentas) / ingresosVentas) * 100 : 0
            };
        }

        public async Task<(byte[] content, string fileName)> ExportarReporteAsync(DateTime inicio, DateTime fin, int maquinaId, bool includePhantom = false, int? templateId = null)
        {
            var reporte = await GetReporteRangoAsync(inicio, fin, maquinaId, includePhantom, templateId);

            if (reporte == null || reporte.Detalle.Count == 0)
                throw new InvalidOperationException("No hay datos para exportar en el rango seleccionado.");

            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Ventas");

                worksheet.Cell(1, 1).Value = "FECHA LOCAL";
                worksheet.Cell(1, 2).Value = "MAQUINA";
                worksheet.Cell(1, 3).Value = "SLOT";
                worksheet.Cell(1, 4).Value = "PRODUCTO";
                worksheet.Cell(1, 5).Value = "COSTO";
                worksheet.Cell(1, 6).Value = "VENTA";
                worksheet.Cell(1, 7).Value = "GANANCIA";
                worksheet.Cell(1, 8).Value = "ESTADO";

                var rangoHeader = worksheet.Range("A1:H1");
                rangoHeader.Style.Font.Bold = true;
                rangoHeader.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;

                int row = 2;
                foreach (var v in reporte.Detalle)
                {
                    worksheet.Cell(row, 1).Value = v.FechaRaw;
                    worksheet.Cell(row, 2).Value = v.Maquina;
                    worksheet.Cell(row, 3).Value = v.Slot;
                    worksheet.Cell(row, 4).Value = v.Producto;
                    worksheet.Cell(row, 5).Value = v.CostoUnitario;
                    worksheet.Cell(row, 6).Value = v.Monto;
                    worksheet.Cell(row, 7).Value = v.Ganancia;
                    worksheet.Cell(row, 8).Value = v.Estado;

                    worksheet.Cell(row, 5).Style.NumberFormat.Format = "$ #,##0";
                    worksheet.Cell(row, 6).Style.NumberFormat.Format = "$ #,##0";
                    worksheet.Cell(row, 7).Style.NumberFormat.Format = "$ #,##0";

                    if (v.Ganancia > 0) worksheet.Cell(row, 7).Style.Font.FontColor = ClosedXML.Excel.XLColor.Green;

                    row++;
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    string name = $"Reporte_{inicio:ddMMyy}_{fin:ddMMyy}.xlsx";
                    return (stream.ToArray(), name);
                }
            }
        }

        public async Task<List<AnalisisProductoDto>> GetAnalisisProductosAsync(DateTime inicio, DateTime fin, int maquinaId)
        {
            DateTime finAjustado = fin.Date.AddDays(1).AddTicks(-1);
            DateTime inicioAjustado = inicio.Date;

            var allProducts = await _context.Productos.ToListAsync();

            var query = _context.Ventas.Where(v => v.FechaLocal >= inicioAjustado && v.FechaLocal <= finAjustado);
            if (maquinaId > 0)
            {
                query = query.Where(v => v.MaquinaId == maquinaId);
            }

            var sales = await query.Select(v => new { v.ProductoId, v.PrecioVenta, v.CostoVenta, v.Producto }).ToListAsync();

            var salesGrouped = sales.Where(s => s.ProductoId.HasValue)
                                    .GroupBy(s => s.ProductoId.Value)
                                    .ToDictionary(g => g.Key, g => new
                                    {
                                        Count = g.Count(),
                                        TotalVentas = g.Sum(x => x.PrecioVenta),
                                        TotalCosto = g.Sum(x => x.CostoVenta > 0 ? x.CostoVenta : (x.Producto != null ? x.Producto.CostoPromedio : 0))
                                    });

            var result = new List<AnalisisProductoDto>();

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

                decimal TH_Rotacion_Alta = 1.0m;
                decimal TH_Rotacion_Media = 0.2m;
                decimal TH_Margen_Alto = 50.0m;

                decimal margenPct = dto.TotalVentas > 0 ? (dto.TotalGanancia / dto.TotalVentas) * 100 : 0;

                if (dto.RotacionDiaria >= TH_Rotacion_Alta)
                {
                    dto.Clasificacion = "Estrella";
                }
                else if (dto.RotacionDiaria < TH_Rotacion_Media)
                {
                    if (margenPct >= TH_Margen_Alto)
                        dto.Clasificacion = "Joya";
                    else
                        dto.Clasificacion = "Cacho";
                }
                else
                {
                    dto.Clasificacion = "Normal";
                }

                result.Add(dto);
            }

            return result.OrderByDescending(x => x.CantidadVendida).ToList();
        }

        public async Task<List<StockoutAnalysisDto>> GetStockoutAnalysisAsync(
            DateTime inicio, DateTime fin, int maquinaId, double umbralHorasSilencio = 24)
        {
            DateTime finAjustado = fin.Date.AddDays(1).AddTicks(-1);
            DateTime inicioAjustado = inicio.Date;

            var query = _context.Ventas
                .Include(v => v.Maquina)
                .Include(v => v.Producto)
                .Where(v => v.FechaLocal >= inicioAjustado && v.FechaLocal <= finAjustado)
                .Where(v => v.IdOrdenMaquina != "TB-EXTRA" && v.IdOrdenMaquina != "TB-SIN-VENTA");

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

                var horasDiferencia = (ultimaActividadMaquina - ultimaVenta).TotalHours;
                var posibleQuiebre = horasDiferencia > umbralHorasSilencio;

                var fechaReferencia = posibleQuiebre ? ultimaActividadMaquina : finAjustado;
                var horasSinStock = (fechaReferencia - ultimaVenta).TotalHours;
                if (horasSinStock < 0) horasSinStock = 0;

                var horasActivas = (ultimaVenta - inicio).TotalHours;
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

                result.Add(new StockoutAnalysisDto
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
                    HorasActivas = horasActivas,
                    VelocidadPorHora = Math.Round(velocidadPorHora, 4),

                    PrecioPromedioVenta = Math.Round(precioPromedio, 0),
                    GananciaPromedio = Math.Round(gananciaPromedio, 0),
                    DineroPerdidoEstimado = Math.Round(dineroPerdido, 0),
                    GananciaPerdidaEstimada = Math.Round(gananciaPerdida, 0)
                });
            }

            return result
                .OrderByDescending(x => x.DineroPerdidoEstimado)
                .ThenByDescending(x => x.PosibleQuiebre)
                .ThenByDescending(x => x.HorasSinStock)
                .ToList();
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
                .Where(v => v.IdOrdenMaquina != "TB-EXTRA" && v.IdOrdenMaquina != "TB-SIN-VENTA")
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
