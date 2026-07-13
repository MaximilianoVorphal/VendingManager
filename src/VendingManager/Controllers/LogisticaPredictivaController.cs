using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

namespace VendingManager.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class LogisticaPredictivaController(ILogisticaPredictivaService service) : ControllerBase
    {
        [HttpGet("zonas")]
        public async Task<ActionResult<List<LogisticaZonaDto>>> GetAnalisisZonas(
            [FromQuery] int diasHistorial = 14,
            [FromQuery] int ventanaDias = 3)
        {
            try
            {
                return await service.GetAnalisisZonasAsync(diasHistorial, ventanaDias);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("generar-orden")]
        public async Task<ActionResult<int>> GenerarOrden(
            [FromQuery] int? zonaLogisticaId,
            [FromQuery] int diasHistorial = 14,
            [FromQuery] int ventanaDias = 3)
        {
            try
            {
                var ordenId = await service.GenerarOrdenCargaBorradorAsync(zonaLogisticaId, diasHistorial, ventanaDias);
                return Ok(ordenId);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
