using Microsoft.AspNetCore.Mvc;

namespace VendingManager.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CajaController : ControllerBase
    {
        private readonly ICajaService _cajaService;
        private readonly Core.Interfaces.IInventarioService _inventarioService;
        private readonly Core.Interfaces.IAuditService _auditService;

        public CajaController(ICajaService cajaService, Core.Interfaces.IInventarioService inventarioService, Core.Interfaces.IAuditService auditService)
        {
            _cajaService = cajaService;
            _inventarioService = inventarioService;
            _auditService = auditService;
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

        [HttpGet("productos-simple")]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetProductosSimple()
        {
            // Retornamos lista simple para el select
            var productos = await _inventarioService.GetProductosAsync();
            return Ok(productos.Select(p => new { p.Id, p.Nombre, p.StockBodega }));
        }

        [HttpPost("registrar")]
        public async Task<IActionResult> RegistrarMovimiento([FromBody] MovimientoCaja mov)
        {
            try
            {
                await _cajaService.RegistrarMovimientoAsync(mov);
                await _auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Registrar Movimiento Caja", $"Movimiento: {mov.Tipo}, Monto: {mov.Monto}, Detalle: {mov.Descripcion}");
                return Ok("Movimiento registrado.");
            }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadImage(IFormFile file, [FromForm] string? category)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            try
            {
                using (var stream = file.OpenReadStream())
                {
                    // Service handles path logic. We can pass null for webRootPath if service has it injected.
                    string path = await _cajaService.UploadComprobanteAsync(stream, file.FileName, null, category);
                    await _auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Subir Comprobante", $"Comprobante subido: {file.FileName} (Categoría: {category ?? "General"})");
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
