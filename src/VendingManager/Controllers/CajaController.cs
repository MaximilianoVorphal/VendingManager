using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace VendingManager.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class CajaController(ICajaService cajaService, Core.Interfaces.IInventarioService inventarioService, Core.Interfaces.IAuditService auditService, ILogger<CajaController> logger) : ControllerBase
    {
        [HttpGet("resumen")]
        public async Task<ActionResult<CajaResumenDto>> GetResumen([FromQuery] int? month, [FromQuery] int? year)
        {
            int targetMonth = month ?? DateTime.Now.Month;
            int targetYear = year ?? DateTime.Now.Year;
            return await cajaService.GetResumenAsync(targetMonth, targetYear);
        }

        [HttpGet("movimientos")]
        public async Task<ActionResult<List<VendingManager.Shared.DTOs.MovimientoCajaDto>>> GetMovimientos([FromQuery] int? month, [FromQuery] int? year)
        {
            int targetMonth = month ?? DateTime.Now.Month;
            int targetYear = year ?? DateTime.Now.Year;
            var movimientos = await cajaService.GetMovimientosAsync(targetMonth, targetYear);
            
            return movimientos.Select(m => new VendingManager.Shared.DTOs.MovimientoCajaDto
            {
                Id = m.Id,
                Fecha = m.Fecha,
                Descripcion = m.Descripcion,
                Monto = m.Monto,
                Tipo = m.Tipo,
                Categoria = m.Categoria,
                ImagenPath = m.ImagenPath,
                ProductoId = m.ProductoId,
                Cantidad = m.Cantidad,
                OrdenCargaId = m.OrdenCargaId,
                CompraId = m.CompraId,
                GastoRecurrenteId = m.GastoRecurrenteId
            }).ToList();
        }

        [HttpGet("productos-simple")]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetProductosSimple()
        {
            // Retornamos lista simple para el select
            var productos = await inventarioService.GetProductosAsync();
            return Ok(productos.Select(p => new { p.Id, p.Nombre, p.StockBodega }));
        }

        [HttpPost("registrar")]
        public async Task<IActionResult> RegistrarMovimiento([FromBody] MovimientoCaja mov)
        {
            try
            {
                await cajaService.RegistrarMovimientoAsync(mov);
                await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Registrar Movimiento Caja", $"Movimiento: {mov.Tipo}, Monto: {mov.Monto}, Detalle: {mov.Descripcion}");
                return Ok("Movimiento registrado.");
            }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in {Action}.", nameof(RegistrarMovimiento));
                throw;
            }
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
                    // El servicio maneja la lógica de rutas. Podemos pasar null para webRootPath si el servicio lo tiene inyectado.
                    string path = await cajaService.UploadComprobanteAsync(stream, file.FileName, null, category);
                    await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Subir Comprobante", $"Comprobante subido: {file.FileName} (Categoría: {category ?? "General"})");
                    return Ok(new { Path = path });
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in {Action}.", nameof(UploadImage));
                throw;
            }
        }

        [HttpGet("exportar")]
        public async Task<IActionResult> ExportarCaja([FromQuery] int? month, [FromQuery] int? year)
        {
            try
            {
                int targetMonth = month ?? DateTime.Now.Month;
                int targetYear = year ?? DateTime.Now.Year;

                // Este endpoint corresponde a "ExportarCajaAsync" (Resumen + Movimientos + Ventas)
                var (content, fileName) = await cajaService.ExportarCajaAsync(targetMonth, targetYear);
                return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in {Action}.", nameof(ExportarCaja));
                throw;
            }
        }
        [HttpGet("valorizacion")]
        public async Task<ActionResult<VendingManager.Shared.DTOs.ValorizacionStockDto>> GetValorizacionStock()
        {
            return await cajaService.GetValorizacionStockAsync();
        }
    }
}
