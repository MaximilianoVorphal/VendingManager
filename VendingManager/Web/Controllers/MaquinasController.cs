using Microsoft.AspNetCore.Mvc;

namespace VendingManager.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MaquinasController : ControllerBase
    {
        private readonly IMaquinaService _maquinaService;

        public MaquinasController(IMaquinaService maquinaService)
        {
            _maquinaService = maquinaService;
        }

        [HttpGet]
        public async Task<ActionResult<List<Maquina>>> GetMaquinas()
        {
            return await _maquinaService.GetMaquinasAsync();
        }

        [HttpPost]
        public async Task<ActionResult<Maquina>> PostMaquina(Maquina maquina)
        {
            var created = await _maquinaService.CreateMaquinaAsync(maquina);
            return CreatedAtAction("GetMaquinas", new { id = created.Id }, created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutMaquina(int id, Maquina maquina)
        {
            if (id != maquina.Id) return BadRequest();

            try
            {
                await _maquinaService.UpdateMaquinaAsync(id, maquina);
                return NoContent();
            }
            catch (Exception)
            {
                if (await _maquinaService.GetMaquinaAsync(id) == null) return NotFound();
                throw;
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMaquina(int id)
        {
            if (await _maquinaService.GetMaquinaAsync(id) == null) return NotFound();

            // Note: Service handles deletion logic (and potentially check for orphans if implemented there)
            await _maquinaService.DeleteMaquinaAsync(id);
            return NoContent();
        }
    }
}
