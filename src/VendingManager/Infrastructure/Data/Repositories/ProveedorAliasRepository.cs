using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;

namespace VendingManager.Infrastructure.Data.Repositories;

/// <summary>
/// Repository for ProveedorAlias entities.
/// CRITICAL: Pure EF change-tracker mutations only — NO SaveChanges calls.
/// SaveChanges is the service layer's responsibility (Design S3).
/// </summary>
public class ProveedorAliasRepository(ApplicationDbContext context) : IProveedorAliasRepository
{
    public async Task<ProveedorAlias?> GetByNormalizedNameAsync(string normalizedName, CancellationToken ct = default)
    {
        return await context.ProveedorAlias
            .Include(a => a.ProveedorCatalog)
            .FirstOrDefaultAsync(a => a.RawNameNormalized == normalizedName, ct);
    }

    public async Task<List<ProveedorAlias>> GetAllAsync(CancellationToken ct = default)
    {
        return await context.ProveedorAlias
            .Include(a => a.ProveedorCatalog)
            .OrderBy(a => a.RawNameNormalized)
            .ToListAsync(ct);
    }

    public async Task AddAsync(ProveedorAlias alias, CancellationToken ct = default)
    {
        await context.ProveedorAlias.AddAsync(alias, ct);
        // NO SaveChanges — pure mutation staging (S3)
    }

    public Task UpdateAsync(ProveedorAlias alias, CancellationToken ct = default)
    {
        context.ProveedorAlias.Update(alias);
        // NO SaveChanges — pure mutation staging (S3)
        return Task.CompletedTask;
    }

    public Task DeleteAsync(ProveedorAlias alias, CancellationToken ct = default)
    {
        context.ProveedorAlias.Remove(alias);
        // NO SaveChanges — pure mutation staging (S3)
        return Task.CompletedTask;
    }
}
