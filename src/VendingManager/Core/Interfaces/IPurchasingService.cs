using System.Collections.Generic;
using System.Threading.Tasks;
using VendingManager.Shared.DTOs;

namespace VendingManager.Core.Interfaces
{
    public interface IPurchasingService
    {
        Task<List<StockCriticoDto>> GetStockCriticoAsync(int maquinaId);
        Task<List<PurchaseSuggestionDto>> GetPurchaseSuggestionAsync(int dias = 30, int maquinaId = 0);
        Task<(byte[] content, string fileName)> ExportarSugerenciaCompraAsync(int dias = 30, int maquinaId = 0);
    }
}
