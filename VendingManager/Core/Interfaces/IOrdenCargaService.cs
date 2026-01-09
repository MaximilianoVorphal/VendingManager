using System.Collections.Generic;
using System.Threading.Tasks;
using VendingManager.Core.DTOs;

namespace VendingManager.Core.Interfaces
{
    public interface IOrdenCargaService
    {
        Task<OrdenCargaDto> CrearOrdenAsync(CrearOrdenDto dto);
        Task<bool> FinalizarOrdenAsync(FinalizarOrdenDto dto);
        Task<List<OrdenCargaDto>> GetOrdenesAsync(int maquinaId = 0); // 0 = All
        Task<OrdenCargaDto?> GetOrdenByIdAsync(int id);
        
        // Helper to get suggested load items
        Task<List<StockCriticoDto>> GetSugerenciaCargaAsync(int maquinaId);
    }
}
