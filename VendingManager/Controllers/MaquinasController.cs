using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VendingManager.Data;
using VendingManager.Models;

namespace VendingManager.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MaquinasController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public MaquinasController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. OBTENER TODAS (GET)
        [HttpGet]
        public async Task<ActionResult<List<Maquina>>> GetMaquinas()
        {
            return await _context.Maquinas.ToListAsync();
        }

        // 2. CREAR NUEVA (POST)
        [HttpPost]
        public async Task<ActionResult<Maquina>> PostMaquina(Maquina maquina)
        {
            _context.Maquinas.Add(maquina);
            await _context.SaveChangesAsync();
            return CreatedAtAction("GetMaquinas", new { id = maquina.Id }, maquina);
        }

        // 3. EDITAR (PUT)
        [HttpPut("{id}")]
        public async Task<IActionResult> PutMaquina(int id, Maquina maquina)
        {
            if (id != maquina.Id) return BadRequest();

            _context.Entry(maquina).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Maquinas.Any(e => e.Id == id)) return NotFound();
                else throw;
            }

            return NoContent();
        }

        // 4. BORRAR (DELETE)
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMaquina(int id)
        {
            var maquina = await _context.Maquinas.FindAsync(id);
            if (maquina == null) return NotFound();

            // Ojo: Si borras una máquina, podrías dejar ventas huérfanas. 
            // Por seguridad, SQL podría bloquearlo si hay ventas asociadas.
            _context.Maquinas.Remove(maquina);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}