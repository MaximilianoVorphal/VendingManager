using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;

namespace VendingManager.Infrastructure.Data.Repositories;

public class MaquinaRepository : IMaquinaRepository
{
    private readonly ApplicationDbContext _context;

    public MaquinaRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Maquina?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _context.Maquinas
            .FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<Maquina?> GetByIdConSlotsAsync(int id, CancellationToken ct = default)
    {
        return await _context.Maquinas
            .Include(m => m.Slots)
            .ThenInclude(s => s.Producto)
            .FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<IReadOnlyList<Maquina>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Maquinas.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Maquina>> GetAllConSlotsAsync(CancellationToken ct = default)
    {
        return await _context.Maquinas
            .Include(m => m.Slots)
            .ThenInclude(s => s.Producto)
            .ToListAsync(ct);
    }

    public async Task<Maquina?> GetByCodigoTerminalPosAsync(string codigo, CancellationToken ct = default)
    {
        return await _context.Maquinas
            .FirstOrDefaultAsync(m => m.CodigoTerminalPos == codigo, ct);
    }

    public async Task AddAsync(Maquina maquina, CancellationToken ct = default)
    {
        await _context.Maquinas.AddAsync(maquina, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Maquina maquina, CancellationToken ct = default)
    {
        _context.Maquinas.Update(maquina);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var maquina = await _context.Maquinas.FindAsync(new object[] { id }, ct);
        if (maquina != null)
        {
            _context.Maquinas.Remove(maquina);
            await _context.SaveChangesAsync(ct);
        }
    }
}