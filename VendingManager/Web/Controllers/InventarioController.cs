using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace VendingManager.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class InventarioController : ControllerBase
    {
        private readonly IInventarioService _inventarioService;

        public InventarioController(IInventarioService inventarioService)
        {
            _inventarioService = inventarioService;
        }

        [HttpPost("subir-catalogo")]
        public async Task<IActionResult> SubirCatalogo(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Por favor sube un archivo vÃ¡lido.");

            try
            {
                using (var stream = file.OpenReadStream())
                {
                    await _inventarioService.ImportarCatalogoAsync(stream, file.FileName);
                }
                return Ok(new { message = "CatÃ¡logo importado correctamente." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno: {ex.Message}");
            }
        }

        [HttpGet("lista")]
        public async Task<IActionResult> GetInventario()
        {
            try
            {
                var productos = await _inventarioService.GetProductosAsync();
                return Ok(productos);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ðŸ”¥ ERROR AL CARGAR INVENTARIO: {ex.Message}");
                return StatusCode(500, "Error interno al cargar la lista de productos.");
            }
        }
    }
}
