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
        public async Task<IActionResult> SubirVentasMaquina(IFormFile file, [FromQuery] DateTime? fechaLimite = null)
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
                    string resultado = await _ventasService.ImportarVentasMaquinaAsync(memoryStream, file.FileName, fechaLimite);
                    return Ok($"Archivo procesado. {resultado}");
                }
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
        public async Task<IActionResult> SubirTransbank(IFormFile file, [FromQuery] DateTime? fechaLimite = null)
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
                    await _ventasService.ImportarTransbankAsync(memoryStream, file.FileName, fechaLimite);
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

        [HttpGet("lista-productos")]
        public async Task<ActionResult<List<ProductoSimpleDto>>> GetListaProductos()
        {
            return await _ventasService.GetProductosAsync();
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
        public async Task<ActionResult<ReporteDto>> GetReporteRango(DateTime inicio, DateTime fin, int maquinaId = 0, bool includePhantom = false, int? templateId = null)
        {
            return await _ventasService.GetReporteRangoAsync(inicio, fin, maquinaId, includePhantom, templateId);
        }

        [HttpGet("informe-financiero")]
        public async Task<ActionResult<InformeFinancieroDto>> GetInformeFinanciero(DateTime inicio, DateTime fin, int maquinaId = 0)
        {
            return await _ventasService.GetInformeFinancieroAsync(inicio, fin, maquinaId);
        }

        [HttpGet("exportar")]
        public async Task<IActionResult> ExportarReporte([FromQuery] DateTime inicio, [FromQuery] DateTime fin, [FromQuery] int maquinaId = 0, [FromQuery] bool includePhantom = false, [FromQuery] int? templateId = null)
        {
            try
            {
                var (content, fileName) = await _ventasService.ExportarReporteAsync(inicio, fin, maquinaId, includePhantom, templateId);
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
        public async Task<IActionResult> SincronizarPortal([FromQuery] int maquinaId, [FromQuery] DateTime? fechaLimite = null)
        {
            var resultado = await _excelService.SincronizarDesdePortal(maquinaId, fechaLimite);
            if (resultado.StartsWith("Error")) return BadRequest(resultado);
            return Ok(resultado);
        }
        [HttpGet("analisis-productos")]
        public async Task<ActionResult<List<AnalisisProductoDto>>> GetAnalisisProductos([FromQuery] DateTime inicio, [FromQuery] DateTime fin, [FromQuery] int maquinaId = 0)
        {
            return await _ventasService.GetAnalisisProductosAsync(inicio, fin, maquinaId);
        }

        /// <summary>
        /// Análisis de Quiebres de Stock y Costo de Oportunidad.
        /// Detecta productos con posible falta de stock y calcula dinero perdido estimado.
        /// </summary>
        [HttpGet("stockout-analysis")]
        public async Task<ActionResult<List<StockoutAnalysisDto>>> GetStockoutAnalysis(
            [FromQuery] DateTime inicio,
            [FromQuery] DateTime fin,
            [FromQuery] int maquinaId = 0,
            [FromQuery] double umbralHoras = 24)
        {
            var result = await _ventasService.GetStockoutAnalysisAsync(inicio, fin, maquinaId, umbralHoras);
            return Ok(result);
        }

        /// <summary>
        /// Obtiene las ventas diarias de un producto específico en una máquina
        /// </summary>
        [HttpGet("ventas-diarias")]
        public async Task<ActionResult<List<VentaDiariaDto>>> GetVentasDiarias(
            [FromQuery] int productoId,
            [FromQuery] int maquinaId,
            [FromQuery] DateTime inicio,
            [FromQuery] DateTime fin)
        {
            var result = await _ventasService.GetVentasDiariasAsync(productoId, maquinaId, inicio, fin);
            return Ok(result);
        }
        [HttpGet("purchase-suggestion")]
        public async Task<IActionResult> GetPurchaseSuggestion([FromQuery] int days = 30)
        {
            var result = await _ventasService.GetPurchaseSuggestionAsync(days);
            return Ok(result);
        }

        [HttpGet("purchase-suggestion/export")]
        public async Task<IActionResult> ExportPurchaseSuggestion([FromQuery] int days = 30)
        {
            try
            {
                var (content, fileName) = await _ventasService.ExportarSugerenciaCompraAsync(days);
                return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest("Error al exportar: " + ex.Message);
            }
        }
        [HttpDelete("borrar-rango")]
        public async Task<IActionResult> BorrarVentasRango([FromQuery] DateTime inicio, [FromQuery] DateTime fin, [FromQuery] int maquinaId)
        {
            try
            {
                await _ventasService.DeleteVentasRangoAsync(inicio, fin, maquinaId);
                return Ok("Ventas eliminadas y stock restaurado correctamente.");
            }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, "Error interno: " + ex.Message); }
        }
    }
}
