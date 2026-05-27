using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VendingManager.Core.Interfaces;
using VendingManager.Core.Utils;
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

            var mimeType = ResolveMimeType(imageFile.FileName, imageFile.ContentType);
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
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

            // Matching pipeline: offset primero, fuzzy como fallback
            var extractedSlots = new List<MatchedSlotDto>();
            var unmatchedSlots = new List<string>();
            var slotsByNumber = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Primera pasada: deduplicar entradas OCR, la ÚLTIMA escrita siempre gana
            foreach (var slot in ocrResult.Slots)
            {
                var normalizedKey = slot.SlotNumber.Trim();
                var quantity = slot.Quantity;

                // Limitar cantidad a 100 (cualquier cosa mayor a 100 probablemente es error OCR)
                if (quantity > 100)
                {
                    quantity = 100;
                    _logger.LogWarning(
                        "[RecargaOCR] Slot {SlotNumber} quantity capped at 100 for maquinaId={MaquinaId}",
                        slot.SlotNumber, maquinaId);
                }

                // Siempre sobrescribe: la última aparición es la que vale
                slotsByNumber[normalizedKey] = quantity;
            }

            // Precomputar listas numéricas para offset matching
            var allOcrNums = slotsByNumber
                .Select(kvp => (original: kvp.Key, num: int.TryParse(kvp.Key, out var n) ? n : (int?)null))
                .Where(x => x.num.HasValue)
                .Select(x => (x.original, num: x.num!.Value))
                .ToList();

            var machineNums = machineSlotNumbers
                .Select(s => (original: s, num: int.TryParse(s, out var n) ? n : (int?)null))
                .Where(x => x.num.HasValue)
                .Select(x => (x.original, num: x.num!.Value))
                .ToList();

            var matchedByOffset = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Segunda pasada: offset matching (ANTES que fuzzy)
            // Las máquinas extensión (slots 101-200) muestran 1-100 en la foto.
            // Calculamos offset = min(máquina) - min(OCR) y lo aplicamos a TODOS
            // los slots OCR numéricos. Esto evita que el fuzzy matching "robe"
            // slots como "11" → "101" cuando deberían ir a "111".
            if (allOcrNums.Count > 0 && machineNums.Count > 0)
            {
                var minOcr = allOcrNums.Min(u => u.num);
                var minMachine = machineNums.Min(m => m.num);
                var offset = minMachine - minOcr;

                // Solo aplicar si offset es positivo y parece razonable
                if (offset > 0 && offset <= 1000)
                {
                    _logger.LogInformation(
                        "[RecargaOCR] Offset detected: minMachine={MinMachine}, minOcr={MinOcr}, offset=+{Offset} for maquinaId={MaquinaId}",
                        minMachine, minOcr, offset, maquinaId);

                    foreach (var ocr in allOcrNums)
                    {
                        var targetNum = ocr.num + offset;
                        var machineMatch = machineNums.FirstOrDefault(m => m.num == targetNum);

                        if (machineMatch.original != null)
                        {
                            extractedSlots.Add(new MatchedSlotDto
                            {
                                SlotNumber = ocr.original,
                                MatchedSlot = machineMatch.original,
                                Quantity = slotsByNumber[ocr.original],
                                Confidence = 0.7f
                            });

                            matchedByOffset.Add(ocr.original);

                            _logger.LogInformation(
                                "[RecargaOCR] Offset-matched OCR slot '{OcrSlot}' -> machine slot '{MatchedSlot}' (offset=+{Offset}) for maquinaId={MaquinaId}",
                                ocr.original, machineMatch.original, offset, maquinaId);
                        }
                    }
                }
            }

            // Tercera pasada: fuzzy matching para slots NO matcheados por offset
            // (incluye slots alfanuméricos y numéricos donde el offset no produjo match)
            foreach (var ocrSlot in slotsByNumber)
            {
                var ocrSlotNumber = ocrSlot.Key;

                // Saltar slots ya matcheados por offset
                if (matchedByOffset.Contains(ocrSlotNumber))
                    continue;

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
                var distance = StringSimilarity.LevenshteinDistance(ocrSlot, machineSlot);

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