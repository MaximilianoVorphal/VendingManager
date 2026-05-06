using Microsoft.AspNetCore.Mvc;
using VendingManager.Shared.DTOs;
using VendingManager.Core.Interfaces;

namespace VendingManager.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrdenCargaController(
        IOrdenCargaService service,
        IOrdenCargaExcelService ordenCargaExcelService,
        IRecargaOcrService recargaOcrService) : ControllerBase
    {
        public async Task<ActionResult<OrdenCargaDto>> CrearOrden(CrearOrdenDto dto)
        {
            try
            {
                var orden = await service.CrearOrdenAsync(dto);
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
                await service.FinalizarOrdenAsync(dto);
                return Ok(new { message = "Orden finalizada y stock retornado correctamente." });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPatch("{id}/nombre")]
        public async Task<IActionResult> ActualizarNombre(int id, [FromBody] string nombre) // using simple string body
        {
            try 
            {
                var success = await service.ActualizarNombreOrdenAsync(id, nombre);
                if (!success) return NotFound();
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> ActualizarOrden(int id, ActualizarOrdenRequestDto dto)
        {
            try
            {
                var success = await service.ActualizarOrdenAsync(id, dto);
                if (!success) return NotFound();
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("historial")]
        public async Task<ActionResult<List<OrdenCargaDto>>> GetOrdenes([FromQuery] int maquinaId = 0)
        {
            return await service.GetOrdenesAsync(maquinaId);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<OrdenCargaDto>> GetOrden(int id)
        {
            var orden = await service.GetOrdenByIdAsync(id);
            if (orden == null) return NotFound();
            return orden;
        }

        [HttpGet("sugerencia")]
        public async Task<ActionResult<List<StockCriticoDto>>> GetSugerencia([FromQuery] int maquinaId)
        {
            return await service.GetSugerenciaCargaAsync(maquinaId);
        }

        [HttpGet("exportar-sugerencia")]
        public async Task<IActionResult> ExportarSugerencia([FromQuery] int maquinaId)
        {
            try
            {
                var sugerencias = await service.GetSugerenciaCargaAsync(maquinaId);
                var content = await ordenCargaExcelService.ExportarListaCarga(sugerencias);
                
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
                var sugerencias = await service.GetSugerenciaGlobalAsync();
                var content = await ordenCargaExcelService.ExportarListaCarga(sugerencias);
                
                string fileName = $"Carga_GLOBAL_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
                return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest("Error generando archivo global: " + ex.Message);
            }
        }

        /// <summary>
        /// OCR endpoint: receives a refill list photo, extracts slot+quantity pairs via OCR,
        /// performs fuzzy matching against the machine's slots, and returns matched results.
        /// </summary>
        [HttpPost("from-photo")]
        public async Task<ActionResult<OcrRecargaResultDto>> ExtractFromPhoto(
            IFormFile file,
            [FromQuery] int maquinaId)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("Archivo no proporcionado o vacío.");
            }

            // Max 10MB
            const long maxSize = 10 * 1024 * 1024;
            if (file.Length > maxSize)
            {
                return BadRequest("La foto supera los 10MB. Usá una foto más pequeña.");
            }

            // Validar tipo MIME de la imagen
            var allowedTypes = new[] { "image/jpeg", "image/png", "image/jpg", "image/heic", "image/webp" };
            if (!allowedTypes.Contains(file.ContentType.ToLowerInvariant()))
            {
                return BadRequest("Formato no soportado. Usá JPG, PNG o HEIC.");
            }

            try
            {
                var result = await recargaOcrService.ExtractRecargaDataAsync(file, maquinaId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error OCR: " + ex.Message);
            }
        }
    }
}

