using Microsoft.EntityFrameworkCore;

namespace VendingManager.Infrastructure.Services
{
    public class MaquinaService : IMaquinaService
    {
        private readonly ApplicationDbContext _context;

        public MaquinaService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<Maquina>> GetMaquinasAsync()
        {
            return await _context.Maquinas.ToListAsync();
        }

        public async Task<Maquina?> GetMaquinaAsync(int id)
        {
            return await _context.Maquinas.FindAsync(id);
        }

        public async Task<Maquina> CreateMaquinaAsync(Maquina maquina)
        {
            _context.Maquinas.Add(maquina);
            await _context.SaveChangesAsync();
            return maquina;
        }

        public async Task UpdateMaquinaAsync(int id, Maquina maquina)
        {
            _context.Entry(maquina).State = EntityState.Modified;
            await _context.SaveChangesAsync();
        }

        public async Task DeleteMaquinaAsync(int id)
        {
            var maquina = await _context.Maquinas.FindAsync(id);
            if (maquina != null)
            {
                _context.Maquinas.Remove(maquina);
                await _context.SaveChangesAsync();
            }
        }
    }
}
