using VendingManager.Core.Entities;

namespace VendingManager.Core.Interfaces;

public interface ICompraService
{
    Task<IEnumerable<Compra>> GetComprasAsync(int? count = null);
    Task<Compra?> GetCompraByIdAsync(int id);
    Task<Compra> RegistrarCompraAsync(Compra compra);
    Task MarcarComoPagada(int id);
    Task<Compra> ActualizarCompraAsync(int id, VendingManager.Shared.DTOs.RegistrarCompraRequestDto request);
    Task EliminarCompraAsync(int id);
    Task<string> SaveFacturaImagenAsync(int compraId, IFormFile file);
    string ResolveFacturaPhysicalPath(string relativePath);
    Task<IEnumerable<Compra>> GetComprasNoVinculadasAsync(string? proveedor = null, string? numeroDocumento = null, DateTime? desde = null, DateTime? hasta = null);
    Task<ReconstruirCostosResult> ReconstruirProductoCostosAsync();
}