using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;

namespace VendingManager.Infrastructure.Data.Repositories;

public class ProductoEANRepository(ApplicationDbContext context) : IProductoEANRepository
{
    public async Task<ProductoEAN?> GetByEanAsync(string ean, CancellationToken ct = default)
    {
        return await context.ProductoEANs
            .Include(e => e.Producto)
            .FirstOrDefaultAsync(e => e.EAN == ean, ct);
    }

    public async Task<IReadOnlyList<ProductoEAN>> GetAllAsync(CancellationToken ct = default)
    {
        return await context.ProductoEANs
            .Include(e => e.Producto)
            .OrderBy(e => e.EAN)
            .ToListAsync(ct);
    }

    public async Task AddAsync(ProductoEAN entity, CancellationToken ct = default)
    {
        await context.ProductoEANs.AddAsync(entity, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ProductoEAN entity, CancellationToken ct = default)
    {
        context.ProductoEANs.Update(entity);
        await context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await context.ProductoEANs.FindAsync(new object[] { id }, ct);
        if (entity != null)
        {
            context.ProductoEANs.Remove(entity);
            await context.SaveChangesAsync(ct);
        }
    }
}
