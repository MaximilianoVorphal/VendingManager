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
        private readonly IExcelService _excelService;

        public OrdenCargaController(IOrdenCargaService service, IExcelService excelService)
        {
            _service = service;
            _excelService = excelService;
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

        [HttpGet("exportar-sugerencia")]
        public async Task<IActionResult> ExportarSugerencia([FromQuery] int maquinaId)
        {
            try
            {
                var sugerencias = await _service.GetSugerenciaCargaAsync(maquinaId);
                var content = await _excelService.ExportarListaCarga(sugerencias);
                
                string fileName = $"Carga_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
                return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest("Error generando archivo: " + ex.Message);
            }
        }

        [HttpGet("exportar-consolidado")]
        public async Task<IActionResult> ExportarConsolidado()
        {
            try
            {
                var sugerencias = await _service.GetSugerenciaGlobalAsync();
                var content = await _excelService.ExportarListaCarga(sugerencias);
                
                string fileName = $"Carga_GLOBAL_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
                return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest("Error generando archivo global: " + ex.Message);
            }
        }
    }
}
