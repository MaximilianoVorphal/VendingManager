using Microsoft.AspNetCore.Mvc;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;

namespace VendingManager.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VentasController : ControllerBase
    {
        private readonly IVentasService _ventasService;
        private readonly IInformesService _informesService;
        private readonly IExcelService _excelService;

        public VentasController(IVentasService ventasService, IInformesService informesService, IExcelService excelService)
        {
            _ventasService = ventasService;
            _informesService = informesService;
            _excelService = excelService;
        }

        [HttpPost("subir-ventas-maquina")]
        public async Task<IActionResult> SubirVentasMaquina(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("Archivo vacío.");
            try
            {
                // 1. Guardar copia en Documentos (Auto-Save)
                using (var memoryStream = new MemoryStream())
                {
                    await file.CopyToAsync(memoryStream);

                    // Guardar en BD
                    var informe = new Informe
                    {
                        Nombre = Path.GetFileNameWithoutExtension(file.FileName) + "_AUTO_SAVE",
                        Extension = Path.GetExtension(file.FileName),
                        TipoContenido = file.ContentType,
                        Contenido = memoryStream.ToArray(),
                        FechaSubida = DateTime.Now
                    };
                    await _informesService.SubirInformeAsync(informe);

                    // 2. Procesar el archivo (resetear stream o usar nuevo)
                    memoryStream.Position = 0;
                    await _ventasService.ImportarVentasMaquinaAsync(memoryStream, file.FileName);
                }

                return Ok("Archivo de MÁQUINA procesado y guardado en Documentos correctamente.");
            }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        [HttpGet("fix-dates")]
        public async Task<IActionResult> FixDates()
        {
            await _ventasService.FixDatesAsync();
            return Ok("Fechas corregidas.");
        }

        [HttpGet("recalcular-costos")]
        public async Task<IActionResult> RecalcularCostos()
        {
            await _ventasService.RecalcularCostosHistoricosAsync();
            return Ok("Costos históricos recalculados basándose en el producto actual.");
        }

        [HttpPost("subir-transbank")]
        public async Task<IActionResult> SubirTransbank(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("Archivo vacío.");
            try
            {
                // 1. Guardar copia en Documentos (Auto-Save)
                using (var memoryStream = new MemoryStream())
                {
                    await file.CopyToAsync(memoryStream);

                    // Guardar en BD
                    var informe = new Informe
                    {
                        Nombre = Path.GetFileNameWithoutExtension(file.FileName) + "_TRANSBANK_AUTO",
                        Extension = Path.GetExtension(file.FileName),
                        TipoContenido = file.ContentType,
                        Contenido = memoryStream.ToArray(),
                        FechaSubida = DateTime.Now
                    };
                    await _informesService.SubirInformeAsync(informe);

                    // 2. Procesar el archivo
                    memoryStream.Position = 0;
                    await _ventasService.ImportarTransbankAsync(memoryStream, file.FileName);
                }

                return Ok("Archivo de TRANSBANK procesado y guardado en Documentos correctamente.");
            }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        [HttpGet("lista-maquinas")]
        public async Task<ActionResult<List<MaquinaSimpleDto>>> GetMaquinas()
        {
            return await _ventasService.GetMaquinasAsync();
        }

        [HttpGet("dashboard-stats")]
        public async Task<ActionResult<DashboardStats>> GetDashboardStats(int maquinaId = 0)
        {
            return await _ventasService.GetDashboardStatsAsync(maquinaId);
        }

        [HttpGet("stock-critico")]
        public async Task<ActionResult<List<StockCriticoDto>>> GetStockCritico(int maquinaId = 0)
        {
            return await _ventasService.GetStockCriticoAsync(maquinaId);
        }

        [HttpGet("reporte-rango")]
        public async Task<ActionResult<ReporteDto>> GetReporteRango(DateTime inicio, DateTime fin, int maquinaId = 0, bool includePhantom = false)
        {
            return await _ventasService.GetReporteRangoAsync(inicio, fin, maquinaId, includePhantom);
        }

        [HttpGet("informe-financiero")]
        public async Task<ActionResult<InformeFinancieroDto>> GetInformeFinanciero(DateTime inicio, DateTime fin, int maquinaId = 0)
        {
            return await _ventasService.GetInformeFinancieroAsync(inicio, fin, maquinaId);
        }

        [HttpGet("exportar")]
        public async Task<IActionResult> ExportarReporte([FromQuery] DateTime inicio, [FromQuery] DateTime fin, [FromQuery] int maquinaId = 0, [FromQuery] bool includePhantom = false)
        {
            try
            {
                var (content, fileName) = await _ventasService.ExportarReporteAsync(inicio, fin, maquinaId, includePhantom);
                return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return BadRequest("Error al generar Excel: " + ex.Message);
            }
        }

        [HttpPost("sync-portal")]
        public async Task<IActionResult> SincronizarPortal([FromQuery] int maquinaId)
        {
            var resultado = await _excelService.SincronizarDesdePortal(maquinaId);
            if (resultado.StartsWith("Error")) return BadRequest(resultado);
            return Ok(resultado);
        }
        [HttpGet("analisis-productos")]
        public async Task<ActionResult<List<AnalisisProductoDto>>> GetAnalisisProductos([FromQuery] DateTime inicio, [FromQuery] DateTime fin, [FromQuery] int maquinaId = 0)
        {
            return await _ventasService.GetAnalisisProductosAsync(inicio, fin, maquinaId);
        }
    }
}
