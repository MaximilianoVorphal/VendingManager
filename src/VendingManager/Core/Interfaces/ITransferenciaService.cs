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

    /// <summary>
    /// One-time migration: reads legacy on-disk comprobante files referenced by
    /// ComprobanteImagenPath and stores them as bytes in the DB (ComprobanteImagen).
    /// Must run while the uploads volume still exists (before compose mounts are removed).
    /// </summary>
    Task<ComprobanteBackfillResult> BackfillComprobantesAsync();
}

/// <summary>Report of a comprobante image DB backfill run for Transferencias.</summary>
public class ComprobanteBackfillResult
{
    /// <summary>Rows with a legacy disk path and no DB bytes yet.</summary>
    public int Total { get; set; }

    /// <summary>Rows whose disk file was found and loaded into the DB.</summary>
    public int Migrated { get; set; }

    /// <summary>Ids of rows whose disk file was missing (nothing to migrate).</summary>
    public List<int> MissingFiles { get; set; } = new();
}