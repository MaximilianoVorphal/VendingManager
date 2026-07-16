using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VendingManager.Shared.DTOs;

namespace VendingManager.Core.Interfaces
{
    public interface IVentasService
    {
        Task<List<MaquinaSimpleDto>> GetMaquinasAsync();
        Task<List<ProductoSimpleDto>> GetProductosAsync();
        Task FixDatesAsync();
        Task DeleteVentasRangoAsync(DateTime inicio, DateTime fin, int maquinaId);
    }
}
