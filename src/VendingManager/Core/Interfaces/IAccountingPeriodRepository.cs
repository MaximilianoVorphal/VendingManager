using VendingManager.Core.Entities;

namespace VendingManager.Core.Interfaces;

public interface IAccountingPeriodRepository
{
    Task<List<AccountingPeriod>> GetAllAsync(CancellationToken ct = default);
    Task<List<AccountingPeriod>> GetByDateRangeAsync(DateTime? desde, DateTime? hasta, CancellationToken ct = default);
    Task<AccountingPeriod?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<AccountingPeriod?> GetFullByIdAsync(int id, CancellationToken ct = default);
    Task<AccountingPeriod> CreateAsync(AccountingPeriod period, CancellationToken ct = default);
    Task UpdateAsync(AccountingPeriod period, CancellationToken ct = default);
}
