using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;

namespace VendingManager.Infrastructure.Data.Repositories;

public class VentaRepository(ApplicationDbContext context) : IVentaRepository
{
    public async Task<Venta?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await context.Ventas
            .Include(v => v.Maquina)
            .Include(v => v.Producto)
            .FirstOrDefaultAsync(v => v.Id == id, ct);
    }

    public async Task<IReadOnlyList<Venta>> GetAllAsync(CancellationToken ct = default)
    {
        return await context.Ventas
            .Include(v => v.Maquina)
            .Include(v => v.Producto)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Venta>> GetByDateRangeAsync(DateTime since, DateTime until, CancellationToken ct = default)
    {
        return await context.Ventas
            .Include(v => v.Maquina)
            .Include(v => v.Producto)
            .Where(v => v.FechaHora >= since && v.FechaHora <= until)
            .OrderBy(v => v.FechaHora)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Venta>> GetPaidInRangeAsync(DateTime since, DateTime until, CancellationToken ct = default)
    {
        return await context.Ventas
            .Include(v => v.Maquina)
            .Include(v => v.Producto)
            .Where(v => v.Pagado && v.FechaHora >= since && v.FechaHora <= until)
            .OrderBy(v => v.FechaHora)
            .ToListAsync(ct);
    }

    public async Task<int> CountPaidInRangeAsync(DateTime since, DateTime until, CancellationToken ct = default)
    {
        return await context.Ventas
            .Where(v => v.Pagado && v.FechaHora >= since && v.FechaHora <= until)
            .CountAsync(ct);
    }

    public async Task<int> CountPaidInRangeExcludingAsync(DateTime since, DateTime until, string[] excludedOrdenIds, CancellationToken ct = default)
    {
        return await context.Ventas
            .Where(v => v.Pagado && v.FechaHora >= since && v.FechaHora <= until && !excludedOrdenIds.Contains(v.IdOrdenMaquina))
            .CountAsync(ct);
    }

    public async Task<IReadOnlyList<Venta>> GetRecentAsync(int count, int? maquinaId = null, CancellationToken ct = default)
    {
        var query = context.Ventas
            .Include(v => v.Maquina)
            .Include(v => v.Producto)
            .AsQueryable();

        if (maquinaId.HasValue && maquinaId.Value > 0)
            query = query.Where(v => v.MaquinaId == maquinaId.Value);

        return await query
            .OrderByDescending(v => v.FechaHora)
            .Take(count)
            .ToListAsync(ct);
    }

    public async Task<decimal> SumPrecioVentaPaidInRangeAsync(DateTime since, DateTime until, CancellationToken ct = default)
    {
        return await context.Ventas
            .Where(v => v.Pagado && v.FechaHora >= since && v.FechaHora <= until)
            .SumAsync(v => v.PrecioVenta, ct);
    }

    public async Task<decimal> SumCostoVentaPaidInRangeAsync(DateTime since, DateTime until, CancellationToken ct = default)
    {
        return await context.Ventas
            .Where(v => v.Pagado && v.FechaHora >= since && v.FechaHora <= until)
            .SumAsync(v => v.CostoVenta, ct);
    }

    public async Task AddAsync(Venta venta, CancellationToken ct = default)
    {
        await context.Ventas.AddAsync(venta, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Venta venta, CancellationToken ct = default)
    {
        context.Ventas.Update(venta);
        await context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var venta = await context.Ventas.FindAsync(new object[] { id }, ct);
        if (venta != null)
        {
            context.Ventas.Remove(venta);
            await context.SaveChangesAsync(ct);
        }
    }
}