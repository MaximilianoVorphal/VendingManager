using VendingManager.Shared.DTOs;

namespace VendingManager.Core.Interfaces;

public interface IIntegrityCheckService
{
    Task<List<IntegrityCheckResultDto>> RunAllChecksAsync(CancellationToken ct = default);
}
