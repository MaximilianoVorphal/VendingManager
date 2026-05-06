using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.DTOs;
using System.Text.Json;

namespace VendingManager.Infrastructure.Services
{
    public class RecargaOcrService : IRecargaOcrService
    {
        private readonly HttpClient _httpClient;
        private readonly ApplicationDbContext _context;
        private readonly string? _scraperServiceUrl;
        private readonly ILogger<RecargaOcrService> _logger;

        public RecargaOcrService(
            HttpClient httpClient,
            IConfiguration configuration,
            ApplicationDbContext context,
            ILogger<RecargaOcrService> logger)
        {
            _httpClient = httpClient;
            _context = context;
            _scraperServiceUrl = configuration["ScraperServiceUrl"];
            _logger = logger;
        }

        public async Task<OcrRecargaResultDto> ExtractRecargaDataAsync(IFormFile imageFile, int maquinaId)
        {
            if (string.IsNullOrEmpty(_scraperServiceUrl))
            {
                throw new Exception("ScraperServiceUrl no está configurado.");
            }

            // Cargar slots de la máquina para fuzzy matching
            var machineSlots = await _context.ConfiguracionSlots
                .Where(s => s.MaquinaId == maquinaId)
                .Select(s => s.NumeroSlot)
                .ToListAsync();

            var machineSlotNumbers = machineSlots.ToList();

            _logger.LogInformation(
                "[RecargaOCR] Processing image {FileName} for maquinaId={MaquinaId}. Machine slots: {SlotCount}",
                imageFile.FileName, maquinaId, machineSlotNumbers.Count);

            // Llamar al endpoint de OCR de Python
            using var content = new MultipartFormDataContent();
            using var stream = imageFile.OpenReadStream();
            using var streamContent = new StreamContent(stream);

            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(imageFile.ContentType);
            content.Add(streamContent, "file", imageFile.FileName);

            var requestUri = $"{_scraperServiceUrl}/api/ocr/recarga";
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

            var ocrResult = JsonSerializer.Deserialize<OcrRecargaResultDto>(jsonResponse, options);

            if (ocrResult == null || ocrResult.Slots.Count == 0)
            {
                _logger.LogWarning(
                    "[RecargaOCR] No slots detected in image {FileName} for maquinaId={MaquinaId}",
                    imageFile.FileName, maquinaId);

                return new OcrRecargaResultDto
                {
                    Slots = new(),
                    ExtractedSlots = new(),
                    UnmatchedOcrSlots = new(),
                    MachineSlotNumbers = machineSlotNumbers
                };
            }

            // Aplicar fuzzy matching
            var extractedSlots = new List<MatchedSlotDto>();
            var unmatchedSlots = new List<string>();
            var slotsByNumber = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Primera pasada: deduplicar entradas OCR, mantener la mayor cantidad por número de slot
            foreach (var slot in ocrResult.Slots)
            {
                var normalizedKey = slot.SlotNumber.Trim();
                var quantity = slot.Quantity;

                // Limitar cantidad a 100
                if (quantity > 100)
                {
                    quantity = 100;
                    _logger.LogWarning(
                        "[RecargaOCR] Slot {SlotNumber} quantity capped at 100 for maquinaId={MaquinaId}",
                        slot.SlotNumber, maquinaId);
                }

                if (slotsByNumber.TryGetValue(normalizedKey, out var existingQty))
                {
                    if (quantity > existingQty)
                    {
                        slotsByNumber[normalizedKey] = quantity;
                    }
                }
                else
                {
                    slotsByNumber[normalizedKey] = quantity;
                }
            }

            // Segunda pasada: fuzzy match de cada slot OCR contra slots de la máquina
            foreach (var ocrSlot in slotsByNumber)
            {
                var ocrSlotNumber = ocrSlot.Key;
                var quantity = ocrSlot.Value;
                var match = FuzzyMatchSlot(ocrSlotNumber, machineSlotNumbers);

                if (match != null)
                {
                    extractedSlots.Add(new MatchedSlotDto
                    {
                        SlotNumber = ocrSlotNumber,
                        MatchedSlot = match.Value.slot,
                        Quantity = quantity,
                        Confidence = match.Value.confidence
                    });

                    _logger.LogInformation(
                        "[RecargaOCR] Matched OCR slot '{OcrSlot}' -> machine slot '{MatchedSlot}' (conf={Confidence:F2}) for maquinaId={MaquinaId}",
                        ocrSlotNumber, match.Value.slot, match.Value.confidence, maquinaId);
                }
                else
                {
                    unmatchedSlots.Add(ocrSlotNumber);

                    _logger.LogWarning(
                        "[RecargaOCR] No match found for OCR slot '{OcrSlot}' on maquinaId={MaquinaId}",
                        ocrSlotNumber, maquinaId);
                }
            }

            _logger.LogInformation(
                "[RecargaOCR] Completed for maquinaId={MaquinaId}. Extracted: {ExtractedCount}, Unmatched: {UnmatchedCount}",
                maquinaId, extractedSlots.Count, unmatchedSlots.Count);

            return new OcrRecargaResultDto
            {
                Slots = ocrResult.Slots,
                ExtractedSlots = extractedSlots,
                UnmatchedOcrSlots = unmatchedSlots,
                MachineSlotNumbers = machineSlotNumbers
            };
        }

        /// <summary>
        /// Fuzzy match de un número de slot OCR contra los slots disponibles de la máquina.
        /// Slots numéricos usan distancia Levenshtein ≤ 2; slots alfanuméricos usan coincidencia exacta.
        /// </summary>
        private (string slot, float confidence)? FuzzyMatchSlot(string ocrSlot, List<string> machineSlots)
        {
            // Verificar si el slot OCR es alfanumérico (contiene letras)
            bool isAlphanumeric = ocrSlot.Any(char.IsLetter);

            if (isAlphanumeric)
            {
                // Coincidencia exacta para slots alfanuméricos (ej: "A1", "B2")
                var exactMatch = machineSlots.FirstOrDefault(ms =>
                    string.Equals(ms, ocrSlot, StringComparison.OrdinalIgnoreCase));

                if (exactMatch != null)
                {
                    return (exactMatch, 1.0f);
                }
                return null;
            }

            // Slot numérico — aplicar tolerancia Levenshtein (distancia ≤ 2)
            string? bestMatch = null;
            int bestDistance = int.MaxValue;

            foreach (var machineSlot in machineSlots)
            {
                var distance = LevenshteinDistance(ocrSlot, machineSlot);

                if (distance <= 2 && distance < bestDistance)
                {
                    bestMatch = machineSlot;
                    bestDistance = distance;
                }
            }

            if (bestMatch != null)
            {
                // Confianza: 1 - (distancia / longitud máxima)
                var maxLen = Math.Max(ocrSlot.Length, bestMatch.Length);
                var confidence = 1.0f - ((float)bestDistance / maxLen);

                // Umbral: confianza >= 0.6
                if (confidence >= 0.6f)
                {
                    return (bestMatch, confidence);
                }
            }

            return null;
        }

        /// <summary>
        /// Calcula la distancia de Levenshtein entre dos strings.
        /// </summary>
        private static int LevenshteinDistance(string s1, string s2)
        {
            var len1 = s1.Length;
            var len2 = s2.Length;
            var d = new int[len1 + 1, len2 + 1];

            for (var i = 0; i <= len1; i++)
                d[i, 0] = i;
            for (var j = 0; j <= len2; j++)
                d[0, j] = j;

            for (var i = 1; i <= len1; i++)
            {
                for (var j = 1; j <= len2; j++)
                {
                    var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[len1, len2];
        }
    }
}