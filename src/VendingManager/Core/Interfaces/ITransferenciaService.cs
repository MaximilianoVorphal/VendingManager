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
    /// Mirrors CompraService.SaveFacturaImagenAsync. Validates size (max 5MB)
    /// and extension (.jpg/.jpeg/.png/.pdf). Replaces any previously stored file.
    /// Returns the relative path /uploads/transferencias/{guid}.ext.
    /// </summary>
    Task<string> SaveComprobanteImagenAsync(int transferenciaId, IFormFile file);

    /// <summary>
    /// Resolves the physical file path for a given relative comprobante path.
    /// </summary>
    string ResolveComprobantePhysicalPath(string relativePath);
}