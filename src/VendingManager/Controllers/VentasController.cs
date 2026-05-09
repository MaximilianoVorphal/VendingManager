using Microsoft.AspNetCore.Mvc;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;

namespace VendingManager.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class VentasController(
        IVentasService ventasService,
        IInformesService informesService,
        ISyncOrchestratorService syncService,
        ISalesAnalyticsService salesAnalyticsService,
        IPurchasingService purchasingService,
        ISalesImportService salesImportService,
        IAuditService auditService) : ControllerBase
    {
        public async Task<IActionResult> SubirVentasMaquina(IFormFile file, [FromQuery] DateTime? fechaLimite = null)
        {
            if (file == null || file.Length == 0) return BadRequest("Archivo vacío.");
            try
            {
                using (var memoryStream = new MemoryStream())
                {
                    await file.CopyToAsync(memoryStream);

                    var informe = new Informe
                    {
                        Nombre = Path.GetFileNameWithoutExtension(file.FileName) + "_AUTO_SAVE",
                        Extension = Path.GetExtension(file.FileName),
                        TipoContenido = file.ContentType,
                        Contenido = memoryStream.ToArray(),
                        FechaSubida = DateTime.Now
                    };
                    await informesService.SubirInformeAsync(informe);

                    memoryStream.Position = 0;
                    string resultado = await salesImportService.ImportarVentasMaquina(memoryStream, file.FileName, fechaLimite);
                    await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Importar Ventas Máquina", $"Archivo importado: {file.FileName}. Resultado: {resultado}");
                    return Ok($"Archivo procesado. {resultado}");
                }
            }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        [HttpGet("fix-dates")]
        public async Task<IActionResult> FixDates()
        {
            await ventasService.FixDatesAsync();
            await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Mantenimiento", "Ejecutado FixDates");
            return Ok("Fechas corregidas.");
        }

        [HttpGet("recalcular-costos")]
        public async Task<IActionResult> RecalcularCostos()
        {
            await ventasService.RecalcularCostosHistoricosAsync();
            await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Mantenimiento", "Ejecutado RecalcularCostosHistoricos");
            return Ok("Costos históricos recalculados basándose en el producto actual.");
        }

        [HttpPost("subir-transbank")]
        public async Task<IActionResult> SubirTransbank(IFormFile file, [FromQuery] DateTime? fechaLimite = null)
        {
            if (file == null || file.Length == 0) return BadRequest("Archivo vacío.");
            try
            {
                using (var memoryStream = new MemoryStream())
                {
                    await file.CopyToAsync(memoryStream);

                    var informe = new Informe
                    {
                        Nombre = Path.GetFileNameWithoutExtension(file.FileName) + "_TRANSBANK_AUTO",
                        Extension = Path.GetExtension(file.FileName),
                        TipoContenido = file.ContentType,
                        Contenido = memoryStream.ToArray(),
                        FechaSubida = DateTime.Now
                    };
                    await informesService.SubirInformeAsync(informe);

                    memoryStream.Position = 0;
                    await salesImportService.ImportarTransbank(memoryStream, file.FileName, fechaLimite);
                }
                
                await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Importar Transbank", $"Archivo importado: {file.FileName}");
                return Ok("Archivo de TRANSBANK procesado y guardado en Documentos correctamente.");
            }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        [HttpGet("lista-maquinas")]
        public async Task<ActionResult<List<MaquinaSimpleDto>>> GetMaquinas()
        {
            return await ventasService.GetMaquinasAsync();
        }

        [HttpGet("lista-productos")]
        public async Task<ActionResult<List<ProductoSimpleDto>>> GetListaProductos()
        {
            return await ventasService.GetProductosAsync();
        }

        [HttpGet("dashboard-stats")]
        public async Task<ActionResult<DashboardStats>> GetDashboardStats(int maquinaId = 0)
        {
            return await salesAnalyticsService.GetDashboardStatsAsync(maquinaId);
        }

        [HttpGet("stock-critico")]
        public async Task<ActionResult<List<StockCriticoDto>>> GetStockCritico(int maquinaId = 0)
        {
            return await purchasingService.GetStockCriticoAsync(maquinaId);
        }

        [HttpGet("reporte-rango")]
        public async Task<ActionResult<ReporteDto>> GetReporteRango(DateTime inicio, DateTime fin, int maquinaId = 0, bool includePhantom = false, int? templateId = null)
        {
            return await salesAnalyticsService.GetReporteRangoAsync(inicio, fin, maquinaId, includePhantom, templateId);
        }

        [HttpGet("informe-financiero")]
        public async Task<ActionResult<InformeFinancieroDto>> GetInformeFinanciero(DateTime inicio, DateTime fin, int maquinaId = 0)
        {
            return await salesAnalyticsService.GetInformeFinancieroAsync(inicio, fin, maquinaId);
        }

        [HttpGet("exportar")]
        public async Task<IActionResult> ExportarReporte([FromQuery] DateTime inicio, [FromQuery] DateTime fin, [FromQuery] int maquinaId = 0, [FromQuery] bool includePhantom = false, [FromQuery] int? templateId = null)
        {
            try
            {
                var (content, fileName) = await salesAnalyticsService.ExportarReporteAsync(inicio, fin, maquinaId, includePhantom, templateId);
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
            var resultado = await syncService.SincronizarDesdePortal(maquinaId, fechaLimite);
            if (resultado.StartsWith("Error")) return BadRequest(resultado);
            await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Sincronizar Portal", $"Sincronización manual máquina {maquinaId}. Fecha límite: {(fechaLimite?.ToString("dd/MM/yyyy HH:mm") ?? "Sin límite")}. Resultado: {resultado}");
            return Ok(resultado);
        }

        [HttpPost("sync-portal-alt")]
        public async Task<IActionResult> SincronizarPortalAlt([FromQuery] DateTime? fechaLimite = null)
        {
            var resultado = await syncService.SincronizarDesdePortalAlt(0, fechaLimite);
            if (resultado.StartsWith("Error")) return BadRequest(resultado);
            await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Sincronizar Portal ALT",
                $"Sincronización ALT (inglés, todas las máquinas). Fecha límite: {(fechaLimite?.ToString("dd/MM/yyyy HH:mm") ?? "Sin límite")}. Resultado: {resultado}");
            return Ok(resultado);
        }
        
        [HttpGet("analisis-productos")]
        public async Task<ActionResult<List<AnalisisProductoDto>>> GetAnalisisProductos(
            [FromQuery] DateTime inicio,
            [FromQuery] DateTime fin,
            [FromQuery] int maquinaId = 0,
            [FromQuery] bool includePendientes = false)
        {
            return await salesAnalyticsService.GetAnalisisProductosAsync(inicio, fin, maquinaId, includePendientes);
        }

        [HttpGet("stockout-analysis")]
        public async Task<ActionResult<List<StockoutAnalysisDto>>> GetStockoutAnalysis(
            [FromQuery] DateTime inicio,
            [FromQuery] DateTime fin,
            [FromQuery] int maquinaId = 0,
            [FromQuery] double umbralHoras = 24)
        {
            var result = await salesAnalyticsService.GetStockoutAnalysisAsync(inicio, fin, maquinaId, umbralHoras);
            return Ok(result);
        }

        [HttpGet("ventas-diarias")]
        public async Task<ActionResult<List<VentaDiariaDto>>> GetVentasDiarias(
            [FromQuery] int productoId,
            [FromQuery] int maquinaId,
            [FromQuery] DateTime inicio,
            [FromQuery] DateTime fin)
        {
            var result = await salesAnalyticsService.GetVentasDiariasAsync(productoId, maquinaId, inicio, fin);
            return Ok(result);
        }

        [HttpGet("categoria-analisis")]
        public async Task<ActionResult<List<CategoriaAnalisisDto>>> GetCategoriaAnalisis(
            [FromQuery] DateTime inicio,
            [FromQuery] DateTime fin,
            [FromQuery] int maquinaId = 0)
        {
            return await salesAnalyticsService.GetCategoriaAnalisisAsync(inicio, fin, maquinaId);
        }
        
        [HttpGet("purchase-suggestion")]
        public async Task<IActionResult> GetPurchaseSuggestion([FromQuery] int days = 30, [FromQuery] int maquinaId = 0)
        {
            var result = await purchasingService.GetPurchaseSuggestionAsync(days, maquinaId);
            return Ok(result);
        }

        [HttpGet("purchase-suggestion/export")]
        public async Task<IActionResult> ExportPurchaseSuggestion([FromQuery] int days = 30, [FromQuery] int maquinaId = 0)
        {
            try
            {
                var (content, fileName) = await purchasingService.ExportarSugerenciaCompraAsync(days, maquinaId);
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
                await ventasService.DeleteVentasRangoAsync(inicio, fin, maquinaId);
                await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Admin", "Borrar Ventas", $"Ventas borradas. Maquina: {maquinaId}, Rango: {inicio} - {fin}");
                return Ok("Ventas eliminadas y stock restaurado correctamente.");
            }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, "Error interno: " + ex.Message); }
        }
    }
}
