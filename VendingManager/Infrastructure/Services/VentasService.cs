using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Linq.Expressions;
using VendingManager.Core.Entities;

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

        public async Task<List<ProductoSimpleDto>> GetProductosAsync()
        {
            return await _context.Productos
                .OrderBy(p => p.Nombre)
                .Select(p => new ProductoSimpleDto { Id = p.Id, Nombre = p.Nombre })
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
                    // Template vacio, no traer nada
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

        public async Task<(byte[] content, string fileName)> ExportarReporteAsync(DateTime inicio, DateTime fin, int maquinaId, bool includePhantom = false, int? templateId = null)
        {
            var reporte = await GetReporteRangoAsync(inicio, fin, maquinaId, includePhantom, templateId);

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

        public async Task<string> ImportarVentasMaquinaAsync(Stream stream, string fileName, DateTime? fechaLimite = null)
        {
            return await _excelService.ImportarVentasMaquina(stream, fileName, fechaLimite);
        }

        public async Task ImportarTransbankAsync(Stream stream, string fileName, DateTime? fechaLimite = null)
        {
            await _excelService.ImportarTransbank(stream, fileName, fechaLimite);
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
                    ProductoId = s.ProductoId ?? 0,
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
            var query = _context.Ventas.Where(v => v.FechaLocal >= inicioAjustado && v.FechaLocal <= finAjustado);
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

        /// <summary>
        /// Análisis de Quiebres de Stock y Costo de Oportunidad.
        /// Detecta productos que dejaron de venderse mientras la máquina seguía activa,
        /// calcula velocidad real de venta y estima el dinero perdido.
        /// </summary>
        public async Task<List<StockoutAnalysisDto>> GetStockoutAnalysisAsync(
            DateTime inicio, DateTime fin, int maquinaId, double umbralHorasSilencio = 24)
        {
            DateTime finAjustado = fin.Date.AddDays(1).AddTicks(-1);
            DateTime inicioAjustado = inicio.Date;

            // 1. Obtener todas las ventas del periodo (excluyendo fantasmas)
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

            // 2. Calcular última actividad por máquina (cualquier venta)
            var ultimaActividadPorMaquina = ventas
                .GroupBy(v => v.MaquinaId)
                .ToDictionary(g => g.Key, g => g.Max(v => v.FechaLocal));

            // 3. Agrupar ventas por Máquina + Producto (solo productos con ProductoId válido)
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

                // Datos básicos
                var primeraVenta = ventasGrupo.Min(v => v.FechaLocal);
                var ultimaVenta = ventasGrupo.Max(v => v.FechaLocal);
                var ultimaActividadMaquina = ultimaActividadPorMaquina[maquinaId_];
                var cantidad = ventasGrupo.Count;

                // Calcular precios y costos promedio
                var precioPromedio = ventasGrupo.Average(v => v.PrecioVenta);
                var costoPromedio = ventasGrupo.Average(v =>
                    v.CostoVenta > 0 ? v.CostoVenta : (v.Producto?.CostoPromedio ?? 0));
                var gananciaPromedio = precioPromedio - costoPromedio;

                // 4. DETECCIÓN DE SILENCIO (Stockout)
                // Si la máquina siguió vendiendo OTROS productos después de que ESTE producto
                // dejó de venderse, es un posible quiebre de stock
                var horasDiferencia = (ultimaActividadMaquina - ultimaVenta).TotalHours;
                var posibleQuiebre = horasDiferencia > umbralHorasSilencio;

                // Horas sin stock = desde última venta hasta fin del reporte (o última actividad, lo que sea menor)
                var fechaReferencia = posibleQuiebre ? ultimaActividadMaquina : finAjustado;
                var horasSinStock = (fechaReferencia - ultimaVenta).TotalHours;
                if (horasSinStock < 0) horasSinStock = 0;

                // 5. VELOCIDAD REAL (basada en horas activas, no días calendario)
                // HorasActivas = tiempo desde el inicio del periodo (reposición) hasta la última venta
                var horasActivas = (ultimaVenta - inicio).TotalHours;
                if (horasActivas < 1) horasActivas = 1; // Mínimo 1 hora para evitar división por cero

                var velocidadPorHora = cantidad / (decimal)horasActivas;

                // 6. COSTO DE OPORTUNIDAD
                // Solo calcular si hay posible quiebre
                decimal dineroPerdido = 0;
                decimal gananciaPerdida = 0;

                if (posibleQuiebre && horasSinStock > 0)
                {
                    dineroPerdido = velocidadPorHora * (decimal)horasSinStock * precioPromedio;
                    gananciaPerdida = velocidadPorHora * (decimal)horasSinStock * gananciaPromedio;
                }

                // Obtener nombres
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

            // Ordenar por dinero perdido (mayor primero), luego por posible quiebre
            return result
                .OrderByDescending(x => x.DineroPerdidoEstimado)
                .ThenByDescending(x => x.PosibleQuiebre)
                .ThenByDescending(x => x.HorasSinStock)
                .ToList();
        }

        /// <summary>
        /// Obtiene las ventas diarias de un producto específico en una máquina
        /// </summary>
        public async Task<List<VentaDiariaDto>> GetVentasDiariasAsync(int productoId, int maquinaId, DateTime inicio, DateTime fin)
        {
            // Si fin es exactamente media noche (00:00:00), asumimos que es fecha manual (hasta fin del día)
            // Si tiene hora (ej: 08:49), usamos esa hora estricta (template)
            DateTime finAjustado;
            if (fin.TimeOfDay.TotalSeconds == 0)
                finAjustado = fin.Date.AddDays(1).AddTicks(-1);
            else
                finAjustado = fin;

            DateTime inicioAjustado = inicio; // Usamos inicio estricto (ej: 11:13)

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
        public async Task<List<PurchaseSuggestionDto>> GetPurchaseSuggestionAsync(int dias = 30)
        {
            DateTime fechaInicio = DateTime.Now.Date.AddDays(-dias);
            
            // 1. Obtener Ventas de los últimos X días
            var ventas = await _context.Ventas
                .Where(v => v.FechaLocal >= fechaInicio)
                .Where(v => v.ProductoId != null && v.ProductoId != 0)
                .GroupBy(v => v.ProductoId!.Value)
                .Select(g => new { ProductoId = g.Key, Cantidad = g.Count() })
                .ToListAsync();

            // 2. Obtener Stock Actual en Máquinas
            var stockMaquinas = await _context.ConfiguracionSlots
                .Where(s => s.ProductoId != null && s.ProductoId != 0)
                .GroupBy(s => s.ProductoId!.Value)
                .Select(g => new { ProductoId = g.Key, Stock = g.Sum(s => s.StockActual) })
                .ToListAsync();

            // 2.5 Determinar si existe en alguna máquina (aunque el stock sea 0, si está asignado)
            var configSlots = await _context.ConfiguracionSlots
                .Where(s => s.ProductoId != null && s.ProductoId != 0)
                .Select(s => s.ProductoId!.Value)
                .Distinct()
                .ToListAsync();
            var productosEnSlots = new HashSet<int>(configSlots);

            // 3. Obtener Productos y Stock Bodega
            var productos = await _context.Productos.ToListAsync();

            var result = new List<PurchaseSuggestionDto>();

            foreach (var p in productos)
            {
                var ventasPeriodo = ventas.FirstOrDefault(v => v.ProductoId == p.Id)?.Cantidad ?? 0;
                var stockEnMaquinas = stockMaquinas.FirstOrDefault(s => s.ProductoId == p.Id)?.Stock ?? 0;
                
                // Cálculo de Sugerencia: 
                // Lo que se vendió en el periodo (demanda esperada) - (Lo que ya tengo en máquinas + Lo que tengo en bodega)
                // Si tengo más stock que la demanda esperada, no compro nada (0).
                int sugerido = ventasPeriodo - (stockEnMaquinas + p.StockBodega);
                if (sugerido < 0) sugerido = 0;

                result.Add(new PurchaseSuggestionDto
                {
                    ProductoId = p.Id,
                    NombreProducto = p.Nombre,
                    VentasUltimos30Dias = ventasPeriodo,
                    StockActualMaquinas = stockEnMaquinas,
                    StockBodega = p.StockBodega,
                    CantidadSugerida = sugerido,
                    EnMaquina = productosEnSlots.Contains(p.Id)
                });
            }

            return result.OrderByDescending(x => x.CantidadSugerida).ThenByDescending(x => x.VentasUltimos30Dias).ToList();
        }

        public async Task<(byte[] content, string fileName)> ExportarSugerenciaCompraAsync(int dias = 30)
        {
            var suggestions = await GetPurchaseSuggestionAsync(dias);

            if (suggestions == null || !suggestions.Any())
                throw new InvalidOperationException("No hay sugerencias para exportar.");

            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Sugerencia Compra");

                // Headers
                worksheet.Cell(1, 1).Value = "EN MÁQUINA";
                worksheet.Cell(1, 2).Value = "PRODUCTO";
                worksheet.Cell(1, 3).Value = $"VENTAS ({dias} DÍAS)";
                worksheet.Cell(1, 4).Value = "STOCK MÁQUINAS";
                worksheet.Cell(1, 5).Value = "STOCK BODEGA";
                worksheet.Cell(1, 6).Value = "SUGERIDO";

                var rangoHeader = worksheet.Range("A1:F1");
                rangoHeader.Style.Font.Bold = true;
                rangoHeader.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;

                int row = 2;
                foreach (var item in suggestions)
                {
                    worksheet.Cell(row, 1).Value = item.EnMaquina ? "Sí" : "No";
                    worksheet.Cell(row, 2).Value = item.NombreProducto;
                    worksheet.Cell(row, 3).Value = item.VentasUltimos30Dias;
                    worksheet.Cell(row, 4).Value = item.StockActualMaquinas;
                    worksheet.Cell(row, 5).Value = item.StockBodega;
                    worksheet.Cell(row, 6).Value = item.CantidadSugerida;

                    // Estilos condicionales
                    if (item.CantidadSugerida > 0)
                    {
                        var rowRange = worksheet.Range(row, 1, row, 6);
                        rowRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightYellow;
                        worksheet.Cell(row, 6).Style.Font.Bold = true;
                        worksheet.Cell(row, 6).Style.Font.FontColor = ClosedXML.Excel.XLColor.Red;
                    }

                    if (item.EnMaquina)
                    {
                         worksheet.Cell(row, 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.Green;
                         worksheet.Cell(row, 1).Style.Font.Bold = true;
                    }
                    else
                    {
                        worksheet.Cell(row, 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.Gray;
                    }

                    row++;
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    string name = $"Sugerencia_Compra_{DateTime.Now:ddMMyy}.xlsx";
                    return (stream.ToArray(), name);
                }
            }
        }
        public async Task DeleteVentasRangoAsync(DateTime inicio, DateTime fin, int maquinaId)
        {
            if (maquinaId <= 0) throw new ArgumentException("Debe seleccionar una máquina específica.");

            DateTime finAjustado = fin.Date.AddDays(1).AddTicks(-1);
            DateTime inicioAjustado = inicio.Date;

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Obtener ventas en el rango
                var ventas = await _context.Ventas
                    .Where(v => v.MaquinaId == maquinaId)
                    .Where(v => v.FechaLocal >= inicioAjustado && v.FechaLocal <= finAjustado)
                    .ToListAsync();

                if (!ventas.Any()) return; // Nada que borrar

                // 2. Agrupar por Slot para restaurar stock de manera eficiente
                // Nota: Solo restauramos stock si tenemos un Slot válido y Configuración actual.
                // Si la configuración cambió (otro producto), igual restauramos al slot físico.
                
                var slotsIds = ventas.Select(v => v.NumeroSlot).Distinct().ToList();
                var configs = await _context.ConfiguracionSlots
                    .Where(c => c.MaquinaId == maquinaId && slotsIds.Contains(c.NumeroSlot))
                    .ToListAsync();

                foreach (var venta in ventas)
                {
                    // Solo restaurar si NO es una venta fantasma (que no descontó stock)
                    if (venta.IdOrdenMaquina == "TB-SIN-VENTA" || venta.IdOrdenMaquina == "TB-EXTRA") continue;

                    var config = configs.FirstOrDefault(c => c.NumeroSlot == venta.NumeroSlot);
                    if (config != null)
                    {
                        // Restaurar Stock
                        // Opcional: Validar Top? No, dejemos que suba.
                        config.StockActual++;
                    }
                }

                // 3. Borrar Ventas
                _context.Ventas.RemoveRange(ventas);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
