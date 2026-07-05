using VendingManager.Core.Entities;
using VendingManager.Shared.DTOs;

namespace VendingManager.Core.Interfaces;

public interface ICompraService
{
    // Returns lightweight DTOs projected in the query so the factura image
    // bytes (stored on Compra) are never loaded for list views.
    Task<IEnumerable<CompraDto>> GetComprasAsync(int? count = null);
    Task<Compra?> GetCompraByIdAsync(int id);
    Task<Compra> RegistrarCompraAsync(Compra compra);
    Task MarcarComoPagada(int id);
    Task<Compra> ActualizarCompraAsync(int id, VendingManager.Shared.DTOs.RegistrarCompraRequestDto request);
    Task EliminarCompraAsync(int id);
    Task<string> SaveFacturaImagenAsync(int compraId, IFormFile file);
    string ResolveFacturaPhysicalPath(string relativePath);

    /// <summary>
    /// Migrates legacy on-disk factura images into the database. Reads files
    /// only (never deletes them). Run in the environment where the files exist.
    /// </summary>
    Task<FacturaBackfillResult> BackfillFacturaImagenesAsync();
    Task<IEnumerable<CompraDto>> GetComprasNoVinculadasAsync(string? proveedor = null, string? numeroDocumento = null, DateTime? desde = null, DateTime? hasta = null);
    Task<ReconstruirCostosResult> ReconstruirProductoCostosAsync();
    Task DesvincularDeTransferenciaAsync(int compraId);

    /// <summary>
    /// Reassigns a compra to a different supplier catalog entry.
    /// Updates ProveedorCatalogId, LastSeenAt, persists alias learning,
    /// and handles alias-move logic. All mutations in a single SaveChanges.
    /// </summary>
    Task<Compra> ReasignarProveedorAsync(int id, VendingManager.Shared.DTOs.ReasignarProveedorRequestDto request);
}

/// <summary>Report of a factura image DB backfill run.</summary>
public class FacturaBackfillResult
{
    /// <summary>Rows with a legacy disk path and no DB bytes yet.</summary>
    public int Total { get; set; }

    /// <summary>Rows whose disk file was found and loaded into the DB.</summary>
    public int Migrated { get; set; }

    /// <summary>Ids of rows whose disk file was missing (nothing to migrate).</summary>
    public List<int> MissingFiles { get; set; } = new();
}