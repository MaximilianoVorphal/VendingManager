using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using System.IO;
using VendingManager.Core.Configuration;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

namespace VendingManager.Infrastructure.Services
{
    public class CajaService : ICajaService
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly IInformesService _informesService;
        private readonly IOptions<VendingConfig> _config;
        private readonly IExcelExportService _excelExportService;
        private readonly CajaBusinessService _business;
        private readonly IFileContentValidator _fileContentValidator;

        public CajaService(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            IInformesService informesService,
            IOptions<VendingConfig> config,
            IExcelExportService excelExportService,
            CajaBusinessService business,
            IFileContentValidator fileContentValidator)
        {
            _context = context;
            _environment = environment;
            _informesService = informesService;
            _config = config;
            _excelExportService = excelExportService;
            _business = business;
            _fileContentValidator = fileContentValidator;
        }

        // UploadComprobanteAsync — kept in service (uses _informesService)
        public async Task<string> UploadComprobanteAsync(Stream fileStream, string fileName, string? webRootPath = null, string? category = null)
        {
            string extension = Path.GetExtension(fileName).ToLower();

            // M-1: Validate file content by magic bytes before accepting the upload.
            // The stream must be re-read after validation, so buffer it first.
            using var bufferStream = new MemoryStream();
            await fileStream.CopyToAsync(bufferStream);
            bufferStream.Position = 0;
            _fileContentValidator.Validate(bufferStream, extension);
            bufferStream.Position = 0;

            using (var memoryStream = new MemoryStream())
            {
                await bufferStream.CopyToAsync(memoryStream);
                var content = memoryStream.ToArray();

                string contentType = "application/octet-stream";

                if (extension == ".pdf") contentType = "application/pdf";
                else if (extension == ".jpg" || extension == ".jpeg") contentType = "image/jpeg";
                else if (extension == ".png") contentType = "image/png";

                string folder = "Caja";
                if (!string.IsNullOrEmpty(category))
                {
                    folder = $"Caja/{category}";
                }

                var informe = new Informe
                {
                    Nombre = Path.GetFileNameWithoutExtension(fileName) + "_CAJA",
                    Extension = extension,
                    Carpeta = folder,
                    TipoContenido = contentType,
                    Contenido = content,
                    FechaSubida = DateTime.Now
                };

                var saved = await _informesService.SubirInformeAsync(informe);
                return $"api/informes/{saved.Id}?ext={extension}";
            }
        }

        public async Task<CajaResumenDto> GetResumenAsync(int month, int year)
        {
            return await _business.GetResumenAsync(month, year);
        }

        public async Task<List<MovimientoCaja>> GetMovimientosAsync(int month, int year)
        {
            return await _business.GetMovimientosAsync(month, year);
        }

        public async Task RegistrarMovimientoAsync(MovimientoCaja mov)
        {
            if (mov.Monto == 0 && mov.Categoria != "MERMA") throw new ArgumentException("El monto no puede ser cero.");

            if (IsMonthLocked(mov.Fecha.Month, mov.Fecha.Year))
            {
                throw new InvalidOperationException($"El mes {mov.Fecha:MM/yyyy} está cerrado y no se puede modificar.");
            }

            if (mov.Fecha < _config.Value.CajaStartDate)
            {
                throw new InvalidOperationException($"No se pueden registrar movimientos anteriores al inicio del cuadre ({_config.Value.CajaStartDate:dd/MM/yyyy}).");
            }

            if (mov.Fecha == DateTime.MinValue) mov.Fecha = DateTime.Now;

            if (mov.Categoria == "MERMA" && mov.ProductoId.HasValue && mov.ProductoId > 0)
            {
                var producto = await _context.Productos.FindAsync(mov.ProductoId);
                if (producto != null)
                {
                    decimal costoTotal = producto.CostoPromedio * mov.Cantidad;
                    mov.Monto = -Math.Abs(costoTotal);

                    if (!mov.Descripcion.Contains(producto.Nombre))
                    {
                        mov.Descripcion = $"{mov.Descripcion} - {producto.Nombre} x{mov.Cantidad}";
                    }

                    producto.StockBodega -= mov.Cantidad;
                    _context.Productos.Update(producto);
                }
            }
            else
            {
                if (mov.Tipo == "GASTO" || mov.Tipo == "RETIRO")
                {
                    if (mov.Monto > 0) mov.Monto = -mov.Monto;
                }
                else
                {
                    if (mov.Monto < 0) mov.Monto = -mov.Monto;
                }
            }

            _context.MovimientosCaja.Add(mov);
            await _context.SaveChangesAsync();
        }

        public bool IsMonthLocked(int month, int year)
        {
            DateTime now = DateTime.Now;
            DateTime targetDateEnd = new DateTime(year, month, 1).AddMonths(1).AddSeconds(-1);
            if (targetDateEnd >= now) return false;
            DateTime lockDate = targetDateEnd.AddDays(5);
            return false;
        }

        public async Task<(byte[] content, string fileName)> ExportarCajaAsync(int month, int year)
        {
            var (resumen, movimientos, ventas) = await _business.GetCajaReportDataAsync(month, year);
            var bytes = await _excelExportService.ExportCajaReportAsync(resumen, movimientos, ventas, month, year);
            return (bytes, $"Reporte_{month}_{year}.xlsx");
        }

        public async Task<(byte[] content, string fileName)> ExportarMovimientosAsync(int month, int year)
        {
            var movimientos = await _business.GetMovimientosAsync(month, year);
            var bytes = await _excelExportService.ExportMovimientosAsync(movimientos, month, year);
            return (bytes, $"Caja_{month}_{year}.xlsx");
        }

        public async Task<ValorizacionStockDto> GetValorizacionStockAsync()
        {
            return await _business.GetValorizacionStockAsync();
        }

        public async Task<List<MovimientoCaja>> GetGastosNoVinculadosAsync(DateTime? fechaDesde = null, DateTime? fechaHasta = null)
        {
            var query = _context.MovimientosCaja
                .Where(m => m.Tipo == "GASTO" && m.RendicionId == null);

            if (fechaDesde.HasValue)
                query = query.Where(m => m.Fecha >= fechaDesde.Value);

            if (fechaHasta.HasValue)
                query = query.Where(m => m.Fecha <= fechaHasta.Value);

            return await query
                .OrderByDescending(m => m.Fecha)
                .ToListAsync();
        }
    }
}