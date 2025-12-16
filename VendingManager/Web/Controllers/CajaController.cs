using Microsoft.AspNetCore.Mvc;

namespace VendingManager.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CajaController : ControllerBase
    {
        private readonly ICajaService _cajaService;

        public CajaController(ICajaService cajaService)
        {
            _cajaService = cajaService;
        }

        [HttpGet("resumen")]
        public async Task<ActionResult<CajaResumenDto>> GetResumen([FromQuery] int? month, [FromQuery] int? year)
        {
            int targetMonth = month ?? DateTime.Now.Month;
            int targetYear = year ?? DateTime.Now.Year;
            return await _cajaService.GetResumenAsync(targetMonth, targetYear);
        }

        [HttpGet("movimientos")]
        public async Task<ActionResult<List<MovimientoCaja>>> GetMovimientos([FromQuery] int? month, [FromQuery] int? year)
        {
            int targetMonth = month ?? DateTime.Now.Month;
            int targetYear = year ?? DateTime.Now.Year;
            return await _cajaService.GetMovimientosAsync(targetMonth, targetYear);
        }

        [HttpPost("registrar")]
        public async Task<IActionResult> RegistrarMovimiento([FromBody] MovimientoCaja mov)
        {
            try
            {
                await _cajaService.RegistrarMovimientoAsync(mov);
                return Ok("Movimiento registrado.");
            }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            try
            {
                using (var stream = file.OpenReadStream())
                {
                    // Service handles path logic. We can pass null for webRootPath if service has it injected.
                    string path = await _cajaService.UploadImageAsync(stream, file.FileName, null);
                    return Ok(new { Path = path });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("exportar")]
        public async Task<IActionResult> ExportarCaja([FromQuery] int? month, [FromQuery] int? year)
        {
            try
            {
                int targetMonth = month ?? DateTime.Now.Month;
                int targetYear = year ?? DateTime.Now.Year;

                // This endpoint corresponds to "ExportarCajaAsync" (Resumen + Movimientos + Ventas)
                var (content, fileName) = await _cajaService.ExportarCajaAsync(targetMonth, targetYear);
                return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest("Error al generar Excel: " + ex.Message);
            }
        }
    }
}
