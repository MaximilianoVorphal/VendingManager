using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Interfaces;
using VendingManager.Core.Utils;
using VendingManager.Infrastructure.Data;

namespace VendingManager.Infrastructure.Services;

public class ProductMatchingService : IProductMatchingService
{
    private readonly ApplicationDbContext _context;
    private readonly double _threshold;

    public ProductMatchingService(ApplicationDbContext context, double threshold = 0.6)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _threshold = threshold;
    }

    public async Task<ProductMatchResult> MatchAsync(string productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            return new ProductMatchResult
            {
                Producto = null,
                Confidence = 0.0,
                SugerirCreacion = true,
                MatchMethod = MatchMethod.None
            };
        }

        var catalog = await _context.Productos.AsNoTracking().ToListAsync();

        var trimmedName = productName.Trim();

        // Step 1: Barcode exact match — if the input looks like a barcode
        if (IsBarcode(trimmedName))
        {
            var barcodeMatch = catalog.FirstOrDefault(p =>
                p.CodigoBarras != null &&
                p.CodigoBarras.Trim().Equals(trimmedName, StringComparison.OrdinalIgnoreCase));

            if (barcodeMatch != null)
            {
                return new ProductMatchResult
                {
                    Producto = barcodeMatch,
                    Confidence = 1.0,
                    SugerirCreacion = false,
                    MatchMethod = MatchMethod.Barcode
                };
            }
        }

        // Step 2: Try exact barcode match by CodigoBarras even for non-barcode-looking names
        var exactBarcodeMatch = catalog.FirstOrDefault(p =>
            p.CodigoBarras != null &&
            p.CodigoBarras.Trim().Equals(trimmedName, StringComparison.OrdinalIgnoreCase));

        if (exactBarcodeMatch != null)
        {
            return new ProductMatchResult
            {
                Producto = exactBarcodeMatch,
                Confidence = 1.0,
                SugerirCreacion = false,
                MatchMethod = MatchMethod.Barcode
            };
        }

        // Step 3: Tokenized fuzzy matching
        var ocrTokens = StringSimilarity.Tokenize(productName);
        if (ocrTokens.Length == 0)
        {
            return new ProductMatchResult
            {
                Producto = null,
                Confidence = 0.0,
                SugerirCreacion = true,
                MatchMethod = MatchMethod.None
            };
        }

        Producto? bestProduct = null;
        double bestScore = 0;
        int bestOverlap = 0;

        foreach (var dbProduct in catalog)
        {
            var dbTokens = StringSimilarity.Tokenize(dbProduct.Nombre);
            if (dbTokens.Length == 0) continue;

            var (score, overlap) = CalculateTokenMatch(ocrTokens, dbTokens);

            if (score > bestScore || (Math.Abs(score - bestScore) < 0.001 && overlap > bestOverlap))
            {
                bestScore = score;
                bestOverlap = overlap;
                bestProduct = dbProduct;
            }
        }

        if (bestProduct != null && bestScore >= _threshold)
        {
            return new ProductMatchResult
            {
                Producto = bestProduct,
                Confidence = bestScore,
                SugerirCreacion = false,
                MatchMethod = MatchMethod.Tokenized
            };
        }

        return new ProductMatchResult
        {
            Producto = null,
            Confidence = 0.0,
            SugerirCreacion = true,
            MatchMethod = MatchMethod.None
        };
    }

    /// <summary>
    /// Calcula el score de matching tokenizado y el overlap absoluto.
    /// </summary>
    private static (double score, int overlap) CalculateTokenMatch(string[] sourceTokens, string[] targetTokens)
    {
        var matched = 0;
        var used = new bool[targetTokens.Length];

        foreach (var srcToken in sourceTokens)
        {
            for (var j = 0; j < targetTokens.Length; j++)
            {
                if (used[j]) continue;

                var distance = StringSimilarity.LevenshteinDistance(srcToken, targetTokens[j]);
                var threshold = srcToken.Length < 6 ? 2 : 3;

                if (distance <= threshold)
                {
                    matched++;
                    used[j] = true;
                    break;
                }
            }
        }

        var maxTokens = Math.Max(sourceTokens.Length, targetTokens.Length);
        var score = (double)matched / maxTokens;
        return (score, matched);
    }

    /// <summary>
    /// Determina si un string parece un código de barras (solo dígitos, largo típico).
    /// </summary>
    private static bool IsBarcode(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        // Códigos de barras EAN-13: 13 dígitos
        // También aceptamos 8 (EAN-8) o 12 (UPC-A)
        return text.All(char.IsDigit) && text.Length is 8 or 12 or 13;
    }
}
