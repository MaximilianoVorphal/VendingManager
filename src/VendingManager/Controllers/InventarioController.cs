using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VendingManager.Core.Interfaces;

namespace VendingManager.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class InventarioController(IInventarioService inventarioService, IAuditService auditService, ILogger<InventarioController> logger) : ControllerBase
    {
        [Microsoft.AspNetCore.Mvc.HttpPost("importar-catalogo")]
        [Microsoft.AspNetCore.Authorization.Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> SubirCatalogo(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Por favor sube un archivo vÃ¡lido.");

            try
            {
                using (var stream = file.OpenReadStream())
                {
                    await inventarioService.ImportarCatalogoAsync(stream, file.FileName);
                }
                await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Importar Catálogo", $"Catálogo importado: {file.FileName}");
                return Ok(new { message = "CatÃ¡logo importado correctamente." });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in {Action}.", nameof(SubirCatalogo));
                throw;
            }
        }

        [HttpGet("lista")]
        public async Task<IActionResult> GetInventario()
        {
            try
            {
                var productos = await inventarioService.GetProductosAsync();
                return Ok(productos);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, “Unhandled error in {Action}.”, nameof(GetInventario));
                throw;
            }
        }
    }
}
