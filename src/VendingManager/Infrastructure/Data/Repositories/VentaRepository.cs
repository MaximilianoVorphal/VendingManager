using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;

namespace VendingManager.Infrastructure.Data.Repositories;

public class VentaRepository : IVentaRepository
{
    private readonly ApplicationDbContext _context;

    public VentaRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Venta?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _context.Ventas
            .Include(v => v.Maquina)
            .Include(v => v.Producto)
            .FirstOrDefaultAsync(v => v.Id == id, ct);
    }

    public async Task<IReadOnlyList<Venta>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Ventas
            .Include(v => v.Maquina)
            .Include(v => v.Producto)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Venta>> GetByDateRangeAsync(DateTime since, DateTime until, CancellationToken ct = default)
    {
        return await _context.Ventas
            .Include(v => v.Maquina)
            .Include(v => v.Producto)
            .Where(v => v.FechaHora >= since && v.FechaHora <= until)
            .OrderBy(v => v.FechaHora)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Venta>> GetPaidInRangeAsync(DateTime since, DateTime until, CancellationToken ct = default)
    {
        return await _context.Ventas
            .Include(v => v.Maquina)
            .Include(v => v.Producto)
            .Where(v => v.Pagado && v.FechaHora >= since && v.FechaHora <= until)
            .OrderBy(v => v.FechaHora)
            .ToListAsync(ct);
    }

    public async Task<int> CountPaidInRangeAsync(DateTime since, DateTime until, CancellationToken ct = default)
    {
        return await _context.Ventas
            .Where(v => v.Pagado && v.FechaHora >= since && v.FechaHora <= until)
            .CountAsync(ct);
    }

    public async Task<int> CountPaidInRangeExcludingAsync(DateTime since, DateTime until, string[] excludedOrdenIds, CancellationToken ct = default)
    {
        return await _context.Ventas
            .Where(v => v.Pagado && v.FechaHora >= since && v.FechaHora <= until && !excludedOrdenIds.Contains(v.IdOrdenMaquina))
            .CountAsync(ct);
    }

    public async Task<decimal> SumPrecioVentaPaidInRangeAsync(DateTime since, DateTime until, CancellationToken ct = default)
    {
        return await _context.Ventas
            .Where(v => v.Pagado && v.FechaHora >= since && v.FechaHora <= until)
            .SumAsync(v => v.PrecioVenta, ct);
    }

    public async Task<decimal> SumCostoVentaPaidInRangeAsync(DateTime since, DateTime until, CancellationToken ct = default)
    {
        return await _context.Ventas
            .Where(v => v.Pagado && v.FechaHora >= since && v.FechaHora <= until)
            .SumAsync(v => v.CostoVenta, ct);
    }

    public async Task AddAsync(Venta venta, CancellationToken ct = default)
    {
        await _context.Ventas.AddAsync(venta, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Venta venta, CancellationToken ct = default)
    {
        _context.Ventas.Update(venta);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var venta = await _context.Ventas.FindAsync(new object[] { id }, ct);
        if (venta != null)
        {
            _context.Ventas.Remove(venta);
            await _context.SaveChangesAsync(ct);
        }
    }
}