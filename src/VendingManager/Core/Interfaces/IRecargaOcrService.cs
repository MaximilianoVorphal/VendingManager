using Microsoft.AspNetCore.Http;
using VendingManager.Shared.DTOs;

namespace VendingManager.Core.Interfaces
{
    public interface IRecargaOcrService
    {
        Task<OcrRecargaResultDto> ExtractRecargaDataAsync(IFormFile imageFile, int maquinaId);
    }
}