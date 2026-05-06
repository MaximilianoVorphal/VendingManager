using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;

namespace VendingManager.Infrastructure.Data.Repositories;

public class MaquinaRepository(ApplicationDbContext context) : IMaquinaRepository
{
    public async Task<Maquina?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await context.Maquinas
            .FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<Maquina?> GetByIdConSlotsAsync(int id, CancellationToken ct = default)
    {
        return await context.Maquinas
            .Include(m => m.Slots)
            .ThenInclude(s => s.Producto)
            .FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<IReadOnlyList<Maquina>> GetAllAsync(CancellationToken ct = default)
    {
        return await context.Maquinas.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Maquina>> GetAllConSlotsAsync(CancellationToken ct = default)
    {
        return await context.Maquinas
            .Include(m => m.Slots)
            .ThenInclude(s => s.Producto)
            .ToListAsync(ct);
    }

    public async Task<Maquina?> GetByCodigoTerminalPosAsync(string codigo, CancellationToken ct = default)
    {
        return await context.Maquinas
            .FirstOrDefaultAsync(m => m.CodigoTerminalPos == codigo, ct);
    }

    public async Task AddAsync(Maquina maquina, CancellationToken ct = default)
    {
        await context.Maquinas.AddAsync(maquina, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Maquina maquina, CancellationToken ct = default)
    {
        context.Maquinas.Update(maquina);
        await context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var maquina = await context.Maquinas.FindAsync(new object[] { id }, ct);
        if (maquina != null)
        {
            context.Maquinas.Remove(maquina);
            await context.SaveChangesAsync(ct);
        }
    }
}