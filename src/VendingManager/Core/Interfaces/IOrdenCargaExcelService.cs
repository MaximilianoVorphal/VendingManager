using System.Collections.Generic;
using System.Threading.Tasks;
using VendingManager.Shared.DTOs;

namespace VendingManager.Core.Interfaces
{
    public interface IOrdenCargaExcelService
    {
        Task<byte[]> ExportarListaCarga(List<StockCriticoDto> items);
    }
}
