using Microsoft.AspNetCore.Http;
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

    /// <summary>
    /// Upload and persist a comprobante image for the given Transferencia.
    /// Validates size (max 5MB), extension (.jpg/.jpeg/.png/.pdf), and file signature.
    /// Stores bytes + content type + original file name directly in the DB.
    /// Replaces any previously stored bytes.
    /// </summary>
    Task SaveComprobanteImagenAsync(int transferenciaId, IFormFile file);

}