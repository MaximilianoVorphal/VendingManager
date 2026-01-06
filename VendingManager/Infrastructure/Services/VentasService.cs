using Microsoft.EntityFrameworkCore;
using System.IO;

namespace VendingManager.Infrastructure.Services
{
    public class VentasService : IVentasService
    {
        private readonly ApplicationDbContext _context;
        private readonly IExcelService _excelService;

        public VentasService(ApplicationDbContext context, IExcelService excelService)
        {
            _context = context;
            _excelService = excelService;
        }

        public async Task<List<MaquinaSimpleDto>> GetMaquinasAsync()
        {
            return await _context.Maquinas
                .Select(m => new MaquinaSimpleDto { Id = m.Id, Nombre = m.Nombre })
                .ToListAsync();
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

            // Fetch Critical Stock Count
            var slotsQuery = _context.ConfiguracionSlots.Where(s => s.StockActual <= 2 && s.ProductoId != 0);
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

        public async Task<ReporteDto> GetReporteRangoAsync(DateTime inicio, DateTime fin, int maquinaId, bool includePhantom = false)
        {
            DateTime finAjustado = fin.Date.AddDays(1).AddTicks(-1);
            DateTime inicioAjustado = inicio.Date;

            var query = _context.Ventas
                .Include(v => v.Maquina)
                .Include(v => v.Producto)
                .Where(v => v.FechaLocal >= inicioAjustado && v.FechaLocal <= finAjustado);

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

                // Calculamos los cobros fantasma (TB-EXTRA)
                MontoPhantom = listaVentas.Where(v => v.IdOrdenMaquina == "TB-EXTRA" || v.IdOrdenMaquina == "TB-SIN-VENTA").Sum(v => v.PrecioVenta),

                Detalle = new List<DetalleVentaDto>()
            };

            // AJUSTE: Restamos los Fantasmas de los totales generales para que las Kpis (Verde/Azul)
            // reflejen solo la operación "Limpia". La plata extra queda aislada en la tarjeta Rosa.
            reporte.TotalVentas -= listaVentas.Count(v => v.IdOrdenMaquina == "TB-EXTRA" || v.IdOrdenMaquina == "TB-SIN-VENTA");
            reporte.MontoTotal -= reporte.MontoPhantom;
            reporte.MontoPagado -= reporte.MontoPhantom; // Asumiendo que los fantasmas siempre están pagados


            foreach (var v in listaVentas)
            {
                // DETECTAR SI ES FANTASMA
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

                // 1. SIEMPRE AGREGAR A LA LISTA DE FANTASMAS SI CORRESPONDE (Para el Modal)
                if (esFantasma)
                {
                    reporte.Fantasmas.Add(detalleDto);
                }

                // 2. AGREGAR A LA LISTA PRINCIPAL SOLO SI CUMPLE EL FILTRO
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

            // FIX: Usar FechaLocal para consistencia con Dashboard y Filtros
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

                // FIX: Lógica de Fallback de Costos (Igual que en Dashboard)
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

        public async Task<(byte[] content, string fileName)> ExportarReporteAsync(DateTime inicio, DateTime fin, int maquinaId, bool includePhantom = false)
        {
            var reporte = await GetReporteRangoAsync(inicio, fin, maquinaId, includePhantom);

            if (reporte == null || reporte.Detalle.Count == 0)
                throw new InvalidOperationException("No hay datos para exportar en el rango seleccionado.");

            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Ventas");

                // Headers
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

        public async Task FixDatesAsync()
        {
            var ventas = await _context.Ventas.Where(v => v.FechaLocal < new DateTime(2000, 1, 1)).ToListAsync();
            foreach (var v in ventas)
            {
                v.FechaLocal = v.FechaHora.AddHours(-11);
            }
            await _context.SaveChangesAsync();
        }

        public async Task ImportarVentasMaquinaAsync(Stream stream, string fileName)
        {
            await _excelService.ImportarVentasMaquina(stream, fileName);
        }

        public async Task ImportarTransbankAsync(Stream stream, string fileName)
        {
            await _excelService.ImportarTransbank(stream, fileName);
        }
        public async Task<List<StockCriticoDto>> GetStockCriticoAsync(int maquinaId)
        {
            var query = _context.ConfiguracionSlots
                .Include(s => s.Maquina)
                .Include(s => s.Producto)
                .Where(s => s.StockActual <= 2 && s.ProductoId != 0); // Threshold matches InventarioMaquina.razor

            if (maquinaId > 0)
            {
                query = query.Where(s => s.MaquinaId == maquinaId);
            }

            return await query
                .Select(s => new StockCriticoDto
                {
                    SlotId = s.Id,
                    Maquina = s.Maquina.Nombre,
                    NumeroSlot = s.NumeroSlot,
                    Producto = s.Producto.Nombre,
                    StockActual = s.StockActual,
                    CapacidadMaxima = s.CapacidadMaxima
                })
                .OrderBy(s => s.Maquina)
                .ThenBy(s => s.NumeroSlot)
                .ToListAsync();
        }

        public async Task RecalcularCostosHistoricosAsync()
        {
            // 1. Cargar Configuración de Slots para fallback (optimizado en memoria)
            var slots = await _context.ConfiguracionSlots
                .Include(s => s.Producto)
                .Where(s => s.ProductoId != 0 && s.Producto != null)
                .ToListAsync();

            // Usamos una clave compuesta (MaquinaId + Slot)
            var slotMap = slots
                .GroupBy(s => new { s.MaquinaId, s.NumeroSlot })
                .ToDictionary(g => g.Key, g => g.First());

            // 2. Cargar todas las ventas
            var ventas = await _context.Ventas
                .Include(v => v.Producto)
                .ToListAsync();

            int updated = 0;
            foreach (var v in ventas)
            {
                Producto? p = v.Producto;

                // Si la venta no tiene producto linkeado (ej. carga antigua), 
                // intentamos buscar qué producto está AHORA en ese slot.
                if (p == null)
                {
                    if (slotMap.TryGetValue(new { v.MaquinaId, v.NumeroSlot }, out var config))
                    {
                        p = config.Producto;
                        v.ProductoId = config.ProductoId; // Linkeamos para el futuro
                    }
                }

                // Si encontramos un producto válido, actualizamos el "Snapshot" del costo
                if (p != null)
                {
                    // Sobrescribimos el CostoVenta con el CostoPromedio actual del producto
                    // Esto es lo que el usuario quiere para arreglar su carga inicial errónea
                    if (v.CostoVenta != p.CostoPromedio)
                    {
                        v.CostoVenta = p.CostoPromedio;
                        updated++;
                    }
                }
            }

            if (updated > 0)
            {
                await _context.SaveChangesAsync();
            }
        }
        public async Task<List<AnalisisProductoDto>> GetAnalisisProductosAsync(DateTime inicio, DateTime fin, int maquinaId)
        {
            DateTime finAjustado = fin.Date.AddDays(1).AddTicks(-1);
            DateTime inicioAjustado = inicio.Date;

            // 1. Get ALL products (to show 0 sales items)
            var allProducts = await _context.Productos.ToListAsync();

            // 2. Get Sales in Range
            var query = _context.Ventas.Where(v => v.Pagado && v.FechaLocal >= inicioAjustado && v.FechaLocal <= finAjustado);
            if (maquinaId > 0)
            {
                query = query.Where(v => v.MaquinaId == maquinaId);
            }

            var sales = await query.Select(v => new { v.ProductoId, v.PrecioVenta, v.CostoVenta, v.Producto }).ToListAsync();

            // 3. Group Sales by Product
            var salesGrouped = sales.Where(s => s.ProductoId.HasValue)
                                    .GroupBy(s => s.ProductoId.Value)
                                    .ToDictionary(g => g.Key, g => new
                                    {
                                        Count = g.Count(),
                                        TotalVentas = g.Sum(x => x.PrecioVenta),
                                        TotalCosto = g.Sum(x => x.CostoVenta > 0 ? x.CostoVenta : (x.Producto != null ? x.Producto.CostoPromedio : 0))
                                    });

            // 4. Merge & Calculate Advanced Metrics
            var result = new List<AnalisisProductoDto>();
            
            // Auxiliar variables for categorization
            // We need DaysInPeriod to calculate velocity
            // If inicio == fin, it's 1 day.
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
                    // 0 Sales
                    dto.CantidadVendida = 0;
                    dto.TotalVentas = 0;
                    dto.TotalGanancia = 0;
                }

                // --- NEW CALCULATIONS ---
                
                // 1. Velocity (Rotacion Diaria)
                dto.RotacionDiaria = (decimal)(dto.CantidadVendida / daysInPeriod);

                // 2. Classification Logic (Heuristics based on User Request)
                // Estrellas: High Vol, Good Margin (Good Margin is subjective, let's say > 30% or just High Vol)
                // Joyas: Low Vol, High Margin (> 50%?)
                // Cachos: Low Vol, Low Margin
                
                // Thresholds (Adjustable)
                decimal MarginThreshold = 40.0m; // 40% margin is "Good"
                decimal VolumeHighThreshold = (decimal)(2.0 / 30.0); // > 2 unit per month is "High"?? No, let's look at the data.
                                                                     // User example: 4.6/day is HIGH. 0.5/day is LOW.
                                                                     // Let's set High Velocity > 1.0 (1 per day) is Super High.
                                                                     // Maybe > 0.5 (1 every 2 days) is decent.
                                                                     // Let's use 0.3 (approx 10/month) as the cut-off for "Low Vol".
                
                // Note: User said "Maní: 0.5 bags/day" is Low Volume but High Margin ("Joya").
                // User said "Coca Cola: 4.6 cans/day" is High Volume ("Vaca Lechera" / "Estrella").
                
                // Refinamos los Thresholds
                decimal TH_Rotacion_Alta = 1.0m; // > 1 per day
                decimal TH_Rotacion_Media = 0.2m; // > 0.2 per day (6 per month)

                decimal TH_Margen_Alto = 50.0m; // > 50%
                decimal TH_Margen_Bajo = 30.0m; // < 30%

                // Calculate Margin % locally
                decimal margenPct = dto.TotalVentas > 0 ? (dto.TotalGanancia / dto.TotalVentas) * 100 : 0;

                if (dto.RotacionDiaria >= TH_Rotacion_Alta)
                {
                    dto.Clasificacion = "Estrella"; // Vende mucho
                }
                else if (dto.RotacionDiaria < TH_Rotacion_Media) // Vende poco
                {
                    if (margenPct >= TH_Margen_Alto)
                        dto.Clasificacion = "Joya"; // Poco volumen, mucho margen
                    else
                        dto.Clasificacion = "Cacho"; // Poco volumen, poco (o normal) margen
                }
                else
                {
                    dto.Clasificacion = "Normal"; // Ni fu ni fa
                }

                result.Add(dto);
            }

            return result.OrderByDescending(x => x.CantidadVendida).ToList();
        }
    }
}
