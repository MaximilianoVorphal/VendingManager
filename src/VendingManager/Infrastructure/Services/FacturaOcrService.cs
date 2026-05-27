using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;
using System.Text.Json;

namespace VendingManager.Infrastructure.Services
{
    public class FacturaOcrService : IFacturaOcrService
    {
        private readonly HttpClient _httpClient;
        private readonly IProductMatchingService _productMatchingService;
        private readonly string? _scraperServiceUrl;

        public FacturaOcrService(HttpClient httpClient, IConfiguration configuration, IProductMatchingService productMatchingService)
        {
            _httpClient = httpClient;
            _productMatchingService = productMatchingService;
            _scraperServiceUrl = configuration["ScraperServiceUrl"];
        }

        public async Task<OcrInvoiceResultDto> ExtractInvoiceDataAsync(IFormFile imageFile)
        {
            if (string.IsNullOrEmpty(_scraperServiceUrl))
            {
                throw new Exception("ScraperServiceUrl no está configurado.");
            }

            using var content = new MultipartFormDataContent();
            using var stream = imageFile.OpenReadStream();
            using var streamContent = new StreamContent(stream);
            
            var mimeType = ResolveMimeType(imageFile.FileName, imageFile.ContentType);
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
            content.Add(streamContent, "file", imageFile.FileName);

            var requestUri = $"{_scraperServiceUrl}/api/ocr/invoice";
            var response = await _httpClient.PostAsync(requestUri, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = await response.Content.ReadAsStringAsync();
                throw new Exception($"Error en servicio OCR ({response.StatusCode}): {errorMsg}");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString 
            };
            var result = JsonSerializer.Deserialize<OcrInvoiceResultDto>(jsonResponse, options);

            if (result != null)
            {
                foreach (var item in result.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.Producto)) continue;

                    var matchResult = await _productMatchingService.MatchAsync(item.Producto);

                    if (matchResult.Producto != null)
                    {
                        item.ProductoIdMatch = matchResult.Producto.Id;
                    }

                    item.SugerirCreacion = matchResult.SugerirCreacion;
                }
            }

            return result ?? new OcrInvoiceResultDto();
        }

        private static string ResolveMimeType(string fileName, string? contentType)
        {
            if (!string.IsNullOrEmpty(contentType))
                return contentType;

            var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" or ".jpge" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                ".gif" => "image/gif",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
        }
    }
}