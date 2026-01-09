using Microsoft.AspNetCore.Mvc;
using VendingManager.Core.DTOs;
using VendingManager.Core.Interfaces;

namespace VendingManager.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrdenCargaController : ControllerBase
    {
        private readonly IOrdenCargaService _service;

        public OrdenCargaController(IOrdenCargaService service)
        {
            _service = service;
        }

        [HttpPost]
        public async Task<ActionResult<OrdenCargaDto>> CrearOrden(CrearOrdenDto dto)
        {
            try
            {
                var orden = await _service.CrearOrdenAsync(dto);
                return Ok(orden);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("finalizar")]
        public async Task<IActionResult> FinalizarOrden(FinalizarOrdenDto dto)
        {
            try
            {
                await _service.FinalizarOrdenAsync(dto);
                return Ok(new { message = "Orden finalizada y stock retornado correctamente." });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("historial")]
        public async Task<ActionResult<List<OrdenCargaDto>>> GetOrdenes([FromQuery] int maquinaId = 0)
        {
            return await _service.GetOrdenesAsync(maquinaId);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<OrdenCargaDto>> GetOrden(int id)
        {
            var orden = await _service.GetOrdenByIdAsync(id);
            if (orden == null) return NotFound();
            return orden;
        }

        [HttpGet("sugerencia")]
        public async Task<ActionResult<List<StockCriticoDto>>> GetSugerencia([FromQuery] int maquinaId)
        {
            return await _service.GetSugerenciaCargaAsync(maquinaId);
        }
    }
}
