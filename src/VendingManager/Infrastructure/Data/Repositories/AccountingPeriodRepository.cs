using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;

namespace VendingManager.Infrastructure.Data.Repositories;

public class AccountingPeriodRepository(ApplicationDbContext context) : IAccountingPeriodRepository
{
    public async Task<List<AccountingPeriod>> GetAllAsync(CancellationToken ct = default)
    {
        return await context.AccountingPeriods
            .OrderByDescending(p => p.FechaInicio)
            .ToListAsync(ct);
    }

    public async Task<List<AccountingPeriod>> GetByDateRangeAsync(DateTime? desde, DateTime? hasta, CancellationToken ct = default)
    {
        var query = context.AccountingPeriods.AsQueryable();

        if (desde.HasValue)
            query = query.Where(p => p.FechaFin >= desde.Value);

        if (hasta.HasValue)
            query = query.Where(p => p.FechaInicio <= hasta.Value);

        return await query
            .OrderByDescending(p => p.FechaInicio)
            .ToListAsync(ct);
    }

    public async Task<AccountingPeriod?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await context.AccountingPeriods
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<AccountingPeriod?> GetFullByIdAsync(int id, CancellationToken ct = default)
    {
        return await context.AccountingPeriods
            .Include(p => p.Transferencias)
                .ThenInclude(t => t.Compras)
                    .ThenInclude(c => c.Detalles)
            .Include(p => p.Transferencias)
                .ThenInclude(t => t.Rendicion)
                    .ThenInclude(r => r!.Gastos)
            .Include(p => p.Devoluciones)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<AccountingPeriod> CreateAsync(AccountingPeriod period, CancellationToken ct = default)
    {
        context.AccountingPeriods.Add(period);
        await context.SaveChangesAsync(ct);
        return period;
    }

    public async Task UpdateAsync(AccountingPeriod period, CancellationToken ct = default)
    {
        context.AccountingPeriods.Update(period);
        await context.SaveChangesAsync(ct);
    }
}
