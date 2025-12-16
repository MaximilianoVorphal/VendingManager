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

        public async Task<ReporteDto> GetReporteRangoAsync(DateTime inicio, DateTime fin, int maquinaId)
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
                Detalle = new List<DetalleVentaDto>()
            };

            foreach (var v in listaVentas)
            {
                decimal costo = v.CostoVenta;
                if (costo == 0 && v.Producto != null) costo = v.Producto.CostoPromedio;

                decimal ganancia = v.PrecioVenta - costo;

                reporte.Detalle.Add(new DetalleVentaDto
                {

                    FechaRaw = v.FechaLocal,
                    Maquina = v.Maquina?.Nombre ?? "Desconocida",
                    Slot = v.NumeroSlot,
                    Producto = v.Producto?.Nombre ?? "---",
                    Monto = v.PrecioVenta,
                    CostoUnitario = costo,
                    Ganancia = ganancia,
                    Estado = v.Pagado ? "Pagado" : "Pendiente"
                });
            }

            reporte.GananciaTotal = reporte.Detalle.Sum(d => d.Ganancia);

            return reporte;
        }

        public async Task<InformeFinancieroDto> GetInformeFinancieroAsync(DateTime inicio, DateTime fin, int maquinaId)
        {
            DateTime finAjustado = fin.Date.AddDays(1).AddTicks(-1);
            DateTime inicioAjustado = inicio.Date;

            var queryVentas = _context.Ventas
                .Where(v => v.Pagado && v.FechaHora >= inicioAjustado && v.FechaHora <= finAjustado);

            if (maquinaId > 0) queryVentas = queryVentas.Where(v => v.MaquinaId == maquinaId);

            var listaVentas = await queryVentas.Select(v => new { v.PrecioVenta, v.CostoVenta }).ToListAsync();

            decimal ingresosVentas = listaVentas.Sum(v => v.PrecioVenta);
            decimal costoVentas = listaVentas.Sum(v => v.CostoVenta);

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

        public async Task<(byte[] content, string fileName)> ExportarReporteAsync(DateTime inicio, DateTime fin, int maquinaId)
        {
            var reporte = await GetReporteRangoAsync(inicio, fin, maquinaId);

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
    }
}
