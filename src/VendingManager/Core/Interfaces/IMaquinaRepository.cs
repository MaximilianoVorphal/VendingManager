using VendingManager.Core.Entities;

namespace VendingManager.Core.Interfaces;

public interface IMaquinaRepository
{
    // Queries
    Task<Maquina?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Maquina?> GetByIdConSlotsAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<Maquina>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Maquina>> GetAllConSlotsAsync(CancellationToken ct = default);
    Task<Maquina?> GetByCodigoTerminalPosAsync(string codigo, CancellationToken ct = default);

    // Persistence
    Task AddAsync(Maquina maquina, CancellationToken ct = default);
    Task UpdateAsync(Maquina maquina, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}