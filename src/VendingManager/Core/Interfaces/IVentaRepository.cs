using VendingManager.Core.Entities;

namespace VendingManager.Core.Interfaces;

public interface IVentaRepository
{
    // Queries
    Task<Venta?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<Venta>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Venta>> GetByDateRangeAsync(DateTime since, DateTime until, CancellationToken ct = default);
    Task<IReadOnlyList<Venta>> GetPaidInRangeAsync(DateTime since, DateTime until, CancellationToken ct = default);
    Task<IReadOnlyList<Venta>> GetRecentAsync(int count, int? maquinaId = null, CancellationToken ct = default);
    Task<int> CountPaidInRangeAsync(DateTime since, DateTime until, CancellationToken ct = default);
    Task<int> CountPaidInRangeExcludingAsync(DateTime since, DateTime until, string[] excludedOrdenIds, CancellationToken ct = default);
    Task<decimal> SumPrecioVentaPaidInRangeAsync(DateTime since, DateTime until, CancellationToken ct = default);
    Task<decimal> SumCostoVentaPaidInRangeAsync(DateTime since, DateTime until, CancellationToken ct = default);

    // Persistence
    Task AddAsync(Venta venta, CancellationToken ct = default);
    Task UpdateAsync(Venta venta, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}