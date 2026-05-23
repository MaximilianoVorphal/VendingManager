using VendingManager.Core.Entities;

namespace VendingManager.Core.Interfaces;

public interface ITransferenciaService
{
    Task<IEnumerable<Transferencia>> GetAllAsync();
    Task<Transferencia?> GetByIdAsync(int id);
    Task<Transferencia> CreateAsync(Transferencia transferencia);
    Task<Transferencia> UpdateAsync(int id, Transferencia transferencia);
    Task DeleteAsync(int id);
    Task<IEnumerable<Transferencia>> GetTransferenciasByRendicionAsync(int rendicionId);
    Task<IEnumerable<Transferencia>> GetTransferenciasPendientesAsync();
    Task<IEnumerable<Transferencia>> GetTransferenciasNoVinculadasAsync();
}