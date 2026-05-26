using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.DTOs;
using System.Text.Json;

namespace VendingManager.Infrastructure.Services
{
    public class FacturaOcrService : IFacturaOcrService
    {
        private readonly HttpClient _httpClient;
        private readonly ApplicationDbContext _context;
        private readonly string? _scraperServiceUrl;

        public FacturaOcrService(HttpClient httpClient, IConfiguration configuration, ApplicationDbContext context)
        {
            _httpClient = httpClient;
            _context = context;
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
                // Intentar hacer match de los productos
                var productosDb = await _context.Productos.ToListAsync();
                
                foreach (var item in result.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.Producto)) continue;
                    
                    var queryTerm = item.Producto.ToLower();
                    
                    // Lógica muy simple de fuzzy match: Si el nombre en DB contiene parte del nombre sacado o viceversa
                    var bestMatch = productosDb.FirstOrDefault(p => 
                        p.Nombre.ToLower().Contains(queryTerm) || 
                        queryTerm.Contains(p.Nombre.ToLower())
                    );
                    
                    if (bestMatch != null)
                    {
                        item.ProductoIdMatch = bestMatch.Id;
                    }
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