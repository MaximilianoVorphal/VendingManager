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
                    // Validar EAN: descartar EANs inválidos (Gemini puede alucinar)
                    if (!string.IsNullOrEmpty(item.Ean) && !IsValidEan(item.Ean))
                        item.Ean = null;

                    // Si no hay ni nombre de producto ni EAN/SKU, no hay nada que matchear
                    if (string.IsNullOrWhiteSpace(item.Producto)
                        && string.IsNullOrEmpty(item.Ean)
                        && string.IsNullOrEmpty(item.Sku))
                        continue;

                    var matchResult = await _productMatchingService.MatchAsync(
                        item.Producto ?? string.Empty, item.Ean, item.Sku, result.Proveedor);

                    if (matchResult.Producto != null)
                    {
                        item.ProductoIdMatch = matchResult.Producto.Id;

                        // Pack division: si el ProductoEAN asociado tiene PackSize > 1,
                        // desglosamos cantidad y costo unitario.
                        if (matchResult.ProductoEAN?.PackSize > 1)
                        {
                            var packSize = matchResult.ProductoEAN.PackSize.Value;
                            var newCantidad = item.Cantidad * packSize;
                            item.CostoUnitario = item.Subtotal / newCantidad;
                            item.Cantidad = newCantidad;
                            item.RequiereConfirmacionPack = true;
                        }
                    }

                    item.SugerirCreacion = matchResult.SugerirCreacion;

                    // Learning: si el item tiene EAN y se matcheó, persiste la relación
                    // para que futuras OCR scans automaticen el matching.
                    if (!string.IsNullOrEmpty(item.Ean) && matchResult.Producto != null)
                    {
                        await _productMatchingService.SaveMappingAsync(
                            item.Ean,
                            matchResult.Producto.Id,
                            matchResult.ProductoEAN?.PackSize);
                    }
                }
            }

            return result ?? new OcrInvoiceResultDto();
        }

        /// <summary>
        /// Valida que un EAN tenga el formato correcto: solo dígitos, 8–13 caracteres.
        /// </summary>
        private static bool IsValidEan(string ean)
        {
            if (string.IsNullOrEmpty(ean)) return false;
            return ean.All(char.IsDigit) && ean.Length is >= 8 and <= 13;
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