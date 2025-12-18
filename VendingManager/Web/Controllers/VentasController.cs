using Microsoft.AspNetCore.Mvc;

namespace VendingManager.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VentasController : ControllerBase
    {
        private readonly IVentasService _ventasService;

        public VentasController(IVentasService ventasService)
        {
            _ventasService = ventasService;
        }

        [HttpPost("subir-ventas-maquina")]
        public async Task<IActionResult> SubirVentasMaquina(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("Archivo vacío.");
            try
            {
                using (var stream = file.OpenReadStream())
                {
                    await _ventasService.ImportarVentasMaquinaAsync(stream, file.FileName);
                }
                return Ok("Archivo de MÁQUINA procesado correctamente.");
            }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        [HttpGet("fix-dates")]
        public async Task<IActionResult> FixDates()
        {
            await _ventasService.FixDatesAsync();
            return Ok("Fechas corregidas.");
        }

        [HttpPost("subir-transbank")]
        public async Task<IActionResult> SubirTransbank(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("Archivo vacío.");
            try
            {
                using (var stream = file.OpenReadStream())
                {
                    await _ventasService.ImportarTransbankAsync(stream, file.FileName);
                }
                return Ok("Archivo de TRANSBANK procesado correctamente.");
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
        public async Task<ActionResult<ReporteDto>> GetReporteRango(DateTime inicio, DateTime fin, int maquinaId = 0)
        {
            return await _ventasService.GetReporteRangoAsync(inicio, fin, maquinaId);
        }

        [HttpGet("informe-financiero")]
        public async Task<ActionResult<InformeFinancieroDto>> GetInformeFinanciero(DateTime inicio, DateTime fin, int maquinaId = 0)
        {
            return await _ventasService.GetInformeFinancieroAsync(inicio, fin, maquinaId);
        }

        [HttpGet("exportar")]
        public async Task<IActionResult> ExportarReporte([FromQuery] DateTime inicio, [FromQuery] DateTime fin, [FromQuery] int maquinaId = 0)
        {
            try
            {
                var (content, fileName) = await _ventasService.ExportarReporteAsync(inicio, fin, maquinaId);
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
    }
}
