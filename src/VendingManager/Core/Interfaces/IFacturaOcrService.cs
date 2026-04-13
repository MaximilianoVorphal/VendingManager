using Microsoft.AspNetCore.Http;
using VendingManager.Shared.DTOs;

namespace VendingManager.Core.Interfaces
{
    public interface IFacturaOcrService
    {
        Task<OcrInvoiceResultDto> ExtractInvoiceDataAsync(IFormFile imageFile);
    }
}
