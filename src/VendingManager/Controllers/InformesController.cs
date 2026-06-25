using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Threading.Tasks;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;

namespace VendingManager.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class InformesController(IInformesService informesService) : ControllerBase
    {
        [HttpGet("{id}")]
        public async Task<IActionResult> Download(int id)
        {
            var informe = await informesService.ObtenerPorIdAsync(id);
            if (informe == null)
            {
                return NotFound();
            }

            return File(informe.Contenido, informe.TipoContenido, $"{informe.Nombre}{informe.Extension}");
        }

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file, [FromForm] string? folder = "General")
        {
            if (file == null || file.Length == 0)
                return BadRequest("No se ha seleccionado ningún archivo.");

            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream);

                var informe = new Informe
                {
                    Nombre = Path.GetFileNameWithoutExtension(file.FileName),
                    Extension = Path.GetExtension(file.FileName),
                    TipoContenido = file.ContentType,
                    Carpeta = folder ?? "General",
                    Contenido = memoryStream.ToArray(),
                    FechaSubida = DateTime.Now
                };

                await informesService.SubirInformeAsync(informe);
            }

            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            await informesService.EliminarInformeAsync(id);
            return Ok();
        }
    }
}
