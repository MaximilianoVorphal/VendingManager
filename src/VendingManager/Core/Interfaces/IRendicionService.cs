using VendingManager.Core.Entities;
using VendingManager.Shared.DTOs;

namespace VendingManager.Core.Interfaces;

public interface IRendicionService
{
    Task<IEnumerable<Rendicion>> GetAllAsync();
    Task<Rendicion?> GetByIdAsync(int id);
    Task<Rendicion> CreateAsync(Rendicion rendicion);
    Task<Rendicion> UpdateAsync(int id, Rendicion rendicion);
    Task<Rendicion> CerrarAsync(int id);
    Task<Compra> LinkCompraAsync(int compraId, int transferenciaId);
    Task<Compra> UnlinkCompraAsync(int compraId);
    Task<MovimientoCaja> LinkGastoAsync(int gastoId, int rendicionId);
    Task<MovimientoCaja> UnlinkGastoAsync(int gastoId);
    Task<RendicionResumenDto> GetResumenAsync(int rendicionId);
}