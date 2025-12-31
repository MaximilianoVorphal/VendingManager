using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;

namespace VendingManager.Infrastructure.Services
{
    public class CajaService : ICajaService
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly IInformesService _informesService;

        // FECHA DE INICIO GLOBAL PARA EL CUADRE DE CAJA
        private static readonly DateTime GlobalStartDate = new DateTime(2025, 12, 18);

        public CajaService(ApplicationDbContext context, IWebHostEnvironment environment, IInformesService informesService)
        {
            _context = context;
            _environment = environment;
            _informesService = informesService;
        }

        // 2. UploadComprobanteAsync
        public async Task<string> UploadComprobanteAsync(Stream fileStream, string fileName, string? webRootPath = null)
        {
            using (var memoryStream = new MemoryStream())
            {
                await fileStream.CopyToAsync(memoryStream);
                var content = memoryStream.ToArray();

                string extension = Path.GetExtension(fileName).ToLower();
                string contentType = "application/octet-stream";

                if (extension == ".pdf") contentType = "application/pdf";
                else if (extension == ".jpg" || extension == ".jpeg") contentType = "image/jpeg";
                else if (extension == ".png") contentType = "image/png";

                var informe = new Informe
                {
                    Nombre = Path.GetFileNameWithoutExtension(fileName) + "_CAJA",
                    Extension = extension,
                    TipoContenido = contentType,
                    Contenido = content,
                    FechaSubida = DateTime.Now
                };

                // Create a more robust MIME type detection if needed, but for now extension based is likely fine or passed in? 
                // The filename has extension.

                // Actually, I can just use a generic image type or try to get it. 
                // But let's check what Controller passes. It passes file.FileName.

                var saved = await _informesService.SubirInformeAsync(informe);
                // Append extension as query param to allow client-side type detection
                return $"api/informes/{saved.Id}?ext={extension}";
            }
        }

        public async Task<CajaResumenDto> GetResumenAsync(int month, int year)
        {
            DateTime startOfMonth = new DateTime(year, month, 1);
            DateTime endOfMonth = startOfMonth.AddMonths(1).AddSeconds(-1);

            // 1. SALDO ANTERIOR
            // Se considera todo lo anterior al inicio de mes, PERO respetando la fecha global de inicio
            var prevIngresosVentas = await _context.Ventas
                .Where(v => v.Pagado && v.FechaHora < startOfMonth && v.FechaHora >= GlobalStartDate)
                .SumAsync(v => v.PrecioVenta);

            var prevMovimientos = await _context.MovimientosCaja
                .Where(m => m.Fecha < startOfMonth && m.Fecha >= GlobalStartDate)
                .SumAsync(m => m.Monto);

            decimal saldoAnterior = prevIngresosVentas + prevMovimientos;

            // 2. MOVIMIENTOS DEL MES
            // Solo considerar si están dentro del rango global
            var monthIngresosVentas = await _context.Ventas
                .Where(v => v.Pagado && v.FechaHora >= startOfMonth && v.FechaHora <= endOfMonth && v.FechaHora >= GlobalStartDate)
                .SumAsync(v => v.PrecioVenta);

            var monthGastos = await _context.MovimientosCaja
                .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= GlobalStartDate && m.Monto < 0)
                .SumAsync(m => m.Monto);

            var monthAportes = await _context.MovimientosCaja
                .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= GlobalStartDate && m.Monto > 0)
                .SumAsync(m => m.Monto);

            return new CajaResumenDto
            {
                SaldoAnterior = saldoAnterior,
                IngresosVentas = monthIngresosVentas,
                GastosOperativos = Math.Abs(monthGastos),
                AportesExtra = monthAportes,
                SaldoFinal = saldoAnterior + monthIngresosVentas + monthAportes + monthGastos,
                IsLocked = IsMonthLocked(month, year)
            };
        }

        public async Task<List<MovimientoCaja>> GetMovimientosAsync(int month, int year)
        {
            return await _context.MovimientosCaja
                .Where(m => m.Fecha.Month == month && m.Fecha.Year == year && m.Fecha >= GlobalStartDate)
                .OrderByDescending(m => m.Fecha)
                .ToListAsync();
        }

        public async Task RegistrarMovimientoAsync(MovimientoCaja mov)
        {
            if (mov.Monto == 0) throw new ArgumentException("El monto no puede ser cero.");

            if (IsMonthLocked(mov.Fecha.Month, mov.Fecha.Year))
            {
                throw new InvalidOperationException($"El mes {mov.Fecha:MM/yyyy} está cerrado y no se puede modificar.");
            }

            // Validar que no se registren movimientos antes de la fecha global
            if (mov.Fecha < GlobalStartDate)
            {
                // Opcional: Permitir registro pero advertir, o bloquear duro. 
                // Dado el requerimiento "desde el dia 18... hacer el cuadre", 
                // bloquear registros antiguos parece coherente para mantener la integridad.
                // Sin embargo, el usuario podría querer registrar algo antiguo como "histórico".
                // Pero si el sistema filtra por GlobalStartDate, ese registro no se vería nunca.
                // Mejor lo dejamos pasar pero sabiendo que no sumará, O lanzamos error.
                // Voy a lanzar error para evitar data "fantasma".
                 throw new InvalidOperationException($"No se pueden registrar movimientos anteriores al inicio del cuadre ({GlobalStartDate:dd/MM/yyyy}).");
            }

            if (mov.Tipo == "GASTO" || mov.Tipo == "RETIRO")
            {
                if (mov.Monto > 0) mov.Monto = -mov.Monto;
            }
            else
            {
                if (mov.Monto < 0) mov.Monto = -mov.Monto;
            }

            if (mov.Fecha == DateTime.MinValue) mov.Fecha = DateTime.Now;

            _context.MovimientosCaja.Add(mov);
            await _context.SaveChangesAsync();
        }



        public bool IsMonthLocked(int month, int year)
        {
            DateTime now = DateTime.Now;
            DateTime targetDateEnd = new DateTime(year, month, 1).AddMonths(1).AddSeconds(-1);

            if (targetDateEnd >= now) return false;

            DateTime lockDate = targetDateEnd.AddDays(5);
            // return now > lockDate; // Deshabilitado temporalmente en original
            return false;
        }

        public async Task<(byte[] content, string fileName)> ExportarCajaAsync(int month, int year)
        {
            var resumen = await GetResumenAsync(month, year);
            var movimientos = await _context.MovimientosCaja
                .Where(m => m.Fecha.Month == month && m.Fecha.Year == year && m.Fecha >= GlobalStartDate)
                .OrderBy(m => m.Fecha)
                .ToListAsync();

            var ventas = await _context.Ventas
                .Include(v => v.Maquina)
                .Include(v => v.Producto)
                .Where(v => v.Pagado && v.FechaHora.Month == month && v.FechaHora.Year == year && v.FechaHora >= GlobalStartDate)
                .OrderBy(v => v.FechaHora)
                .ToListAsync();

            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                // Implementación de lógica de exportación... (copiada de ExportarCaja en Controller)
                // ... (Simplificado para no repetir todo el código literal, pero importante incluirlo)
                // Voy a incluir la lógica completa

                var s1 = workbook.Worksheets.Add("Resumen Financiero");
                s1.Cell(1, 1).Value = "REPORTE FINANCIERO MENSUAL";
                s1.Range(1, 1, 1, 4).Merge().Style.Font.Bold = true;
                s1.Cell(2, 1).Value = $"PERIODO: {month}/{year}";

                s1.Cell(4, 1).Value = "CONCEPTO";
                s1.Cell(4, 2).Value = "MONTO";
                s1.Row(4).Style.Font.Bold = true;

                s1.Cell(5, 1).Value = "Saldo Anterior";
                s1.Cell(5, 2).Value = resumen.SaldoAnterior;

                s1.Cell(6, 1).Value = "(+) Ingresos por Ventas";
                s1.Cell(6, 2).Value = resumen.IngresosVentas;

                s1.Cell(7, 1).Value = "(+) Aportes de Capital";
                s1.Cell(7, 2).Value = resumen.AportesExtra;

                s1.Cell(8, 1).Value = "(-) Gastos y Retiros";
                s1.Cell(8, 2).Value = resumen.GastosOperativos;

                s1.Cell(9, 1).Value = "SALDO FINAL CAJA";
                s1.Cell(9, 2).Value = resumen.SaldoFinal;
                s1.Row(9).Style.Font.Bold = true;

                s1.Column(2).Style.NumberFormat.Format = "$ #,##0";
                s1.Columns().AdjustToContents();

                var s2 = workbook.Worksheets.Add("Libro Caja");
                s2.Cell(1, 1).Value = "Fecha";
                s2.Cell(1, 2).Value = "Tipo";
                s2.Cell(1, 3).Value = "Categoría";
                s2.Cell(1, 4).Value = "Descripción";
                s2.Cell(1, 5).Value = "Monto";
                s2.Row(1).Style.Font.Bold = true;

                int row = 2;
                foreach (var m in movimientos)
                {
                    s2.Cell(row, 1).Value = m.Fecha;
                    s2.Cell(row, 2).Value = m.Tipo;
                    s2.Cell(row, 3).Value = m.Categoria;
                    s2.Cell(row, 4).Value = m.Descripcion;
                    s2.Cell(row, 5).Value = m.Monto;
                    row++;
                }
                s2.Column(5).Style.NumberFormat.Format = "$ #,##0";
                s2.Columns().AdjustToContents();

                var s3 = workbook.Worksheets.Add("Detalle Ventas");
                s3.Cell(1, 1).Value = "Fecha";
                s3.Cell(1, 2).Value = "Máquina";
                s3.Cell(1, 3).Value = "Slot";
                s3.Cell(1, 4).Value = "Producto";
                s3.Cell(1, 5).Value = "P. Venta";
                s3.Cell(1, 6).Value = "P. Costo (Histórico)";
                s3.Cell(1, 7).Value = "Margen $";
                s3.Row(1).Style.Font.Bold = true;

                row = 2;
                foreach (var v in ventas)
                {
                    s3.Cell(row, 1).Value = v.FechaHora;
                    s3.Cell(row, 2).Value = v.Maquina?.Nombre ?? "N/A";
                    s3.Cell(row, 3).Value = v.NumeroSlot;
                    s3.Cell(row, 4).Value = v.Producto?.Nombre ?? "Indefinido";
                    s3.Cell(row, 5).Value = v.PrecioVenta;
                    s3.Cell(row, 6).Value = v.CostoVenta;
                    s3.Cell(row, 7).FormulaA1 = $"E{row}-F{row}";
                    row++;
                }
                s3.Range(2, 5, row, 7).Style.NumberFormat.Format = "$ #,##0";
                s3.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return (stream.ToArray(), $"Reporte_{month}_{year}.xlsx");
                }
            }
        }

        public async Task<(byte[] content, string fileName)> ExportarMovimientosAsync(int month, int year)
        {
            var movimientos = await _context.MovimientosCaja
                .Where(m => m.Fecha.Month == month && m.Fecha.Year == year && m.Fecha >= GlobalStartDate)
                .OrderBy(m => m.Fecha)
                .ToListAsync();

            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add($"Caja {month}-{year}");
                // Headers... (Logic from ExportarExcel in Controller)
                worksheet.Cell(1, 1).Value = "ID";
                worksheet.Cell(1, 2).Value = "FECHA";
                worksheet.Cell(1, 3).Value = "TIPO";
                worksheet.Cell(1, 4).Value = "CATEGORIA";
                worksheet.Cell(1, 5).Value = "DESCRIPCION";
                worksheet.Cell(1, 6).Value = "MONTO";

                var rangoHeader = worksheet.Range("A1:F1");
                rangoHeader.Style.Font.Bold = true;
                rangoHeader.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;

                int row = 2;
                foreach (var m in movimientos)
                {
                    worksheet.Cell(row, 1).Value = m.Id;
                    worksheet.Cell(row, 2).Value = m.Fecha;
                    worksheet.Cell(row, 3).Value = m.Tipo;
                    worksheet.Cell(row, 4).Value = m.Categoria;
                    worksheet.Cell(row, 5).Value = m.Descripcion;
                    worksheet.Cell(row, 6).Value = m.Monto;
                    worksheet.Cell(row, 6).Style.NumberFormat.Format = "$ #,##0";
                    if (m.Monto < 0) worksheet.Cell(row, 6).Style.Font.FontColor = ClosedXML.Excel.XLColor.Red;
                    else worksheet.Cell(row, 6).Style.Font.FontColor = ClosedXML.Excel.XLColor.Green;
                    row++;
                }
                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return (stream.ToArray(), $"Caja_{month}_{year}.xlsx");
                }
            }
        }
    }
}
