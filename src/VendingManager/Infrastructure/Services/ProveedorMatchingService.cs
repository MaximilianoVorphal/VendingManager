using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Core.Utils;
using VendingManager.Infrastructure.Data;

namespace VendingManager.Infrastructure.Services;

/// <summary>
/// Resolves raw OCR supplier strings to canonical ProveedorCatalog entries.
/// Algorithm: Step 0a (alias lookup) → Step 0b (exact canonical) → Step 1 (tokenized fuzzy with short-name rule).
/// One catalog load shared between Step 0b and Step 1 (Design S2).
/// </summary>
public class ProveedorMatchingService : IProveedorMatchingService
{
    private readonly ApplicationDbContext _context;
    private readonly IProveedorAliasRepository _aliasRepo;
    private readonly double _threshold;

    public ProveedorMatchingService(
        ApplicationDbContext context,
        IProveedorAliasRepository aliasRepo,
        double threshold = 0.6)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _aliasRepo = aliasRepo ?? throw new ArgumentNullException(nameof(aliasRepo));
        _threshold = threshold;
    }

    public async Task<ProveedorMatchResult> MatchAsync(string proveedorRaw)
    {
        return await MatchAsync(proveedorRaw, _threshold);
    }

    public async Task<ProveedorMatchResult> MatchAsync(string proveedorRaw, double threshold)
    {
        // Guard: blank input
        if (string.IsNullOrWhiteSpace(proveedorRaw))
        {
            return new ProveedorMatchResult
            {
                ProveedorCatalog = null,
                Confidence = 0.0,
                SugerirCreacion = true,
                MatchMethod = ProveedorMatchMethod.None
            };
        }

        var normalized = NameNormalizer.Normalize(proveedorRaw);

        // Step 0a: exact alias lookup by normalized key
        var alias = await _aliasRepo.GetByNormalizedNameAsync(normalized);
        if (alias?.ProveedorCatalog != null)
        {
            return new ProveedorMatchResult
            {
                ProveedorCatalog = alias.ProveedorCatalog,
                Confidence = 1.0,
                SugerirCreacion = false,
                MatchMethod = ProveedorMatchMethod.ExactAlias
            };
        }

        // Load catalog ONCE for both Step 0b and Step 1 (S2: no second round-trip)
        var catalog = await _context.ProveedorCatalog
            .AsNoTracking()
            .ToListAsync();

        // Step 0b: exact canonical name match (normalized comparison)
        foreach (var entry in catalog)
        {
            var canonicalNormalized = NameNormalizer.Normalize(entry.NombreCanonical);
            if (normalized == canonicalNormalized)
            {
                return new ProveedorMatchResult
                {
                    ProveedorCatalog = entry,
                    Confidence = 1.0,
                    SugerirCreacion = false,
                    MatchMethod = ProveedorMatchMethod.ExactCanonical
                };
            }
        }

        // Step 1: tokenized fuzzy matching
        var sourceTokens = StringSimilarity.Tokenize(proveedorRaw);
        if (sourceTokens.Length == 0)
        {
            // All tokens filtered out (e.g. all sub-3-char) — cannot fuzzy
            return new ProveedorMatchResult
            {
                ProveedorCatalog = null,
                Confidence = 0.0,
                SugerirCreacion = true,
                MatchMethod = ProveedorMatchMethod.None
            };
        }

        ProveedorCatalog? best = null;
        double bestScore = 0;
        int bestOverlap = 0;

        foreach (var entry in catalog)
        {
            var candidateTokens = StringSimilarity.Tokenize(entry.NombreCanonical);
            if (candidateTokens.Length == 0)
                continue;

            // Short-name rule: skip fuzzy acceptance when either side is single-token
            if (sourceTokens.Length <= 1 || candidateTokens.Length <= 1)
                continue;

            var score = StringSimilarity.TokenizedSimilarity(proveedorRaw, entry.NombreCanonical);
            var overlap = CalculateTokenOverlap(sourceTokens, candidateTokens);

            if (score > bestScore ||
                (Math.Abs(score - bestScore) < 0.001 && overlap > bestOverlap))
            {
                bestScore = score;
                bestOverlap = overlap;
                best = entry;
            }
        }

        if (best != null && bestScore >= threshold)
        {
            return new ProveedorMatchResult
            {
                ProveedorCatalog = best,
                Confidence = bestScore,
                SugerirCreacion = false,
                MatchMethod = ProveedorMatchMethod.Tokenized
            };
        }

        return new ProveedorMatchResult
        {
            ProveedorCatalog = null,
            Confidence = 0.0,
            SugerirCreacion = true,
            MatchMethod = ProveedorMatchMethod.None
        };
    }

    public async Task SaveAliasAsync(string proveedorRaw, int proveedorCatalogId)
    {
        if (string.IsNullOrWhiteSpace(proveedorRaw))
            return;

        var normalized = NameNormalizer.Normalize(proveedorRaw);
        var existing = await _aliasRepo.GetByNormalizedNameAsync(normalized);

        if (existing != null)
        {
            existing.ProveedorCatalogId = proveedorCatalogId;
            existing.LastSeenAt = DateTime.UtcNow;
            await _aliasRepo.UpdateAsync(existing);
        }
        else
        {
            await _aliasRepo.AddAsync(new ProveedorAlias
            {
                RawName = proveedorRaw,
                RawNameNormalized = normalized,
                ProveedorCatalogId = proveedorCatalogId,
                CreatedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow
            });
        }
        // NO SaveChanges — atomicity belongs to the service caller (S3)
    }

    /// <summary>
    /// Calculates the number of overlapping tokens between source and candidate.
    /// Used as a tie-breaker when multiple candidates have the same score.
    /// </summary>
    private static int CalculateTokenOverlap(string[] sourceTokens, string[] candidateTokens)
    {
        var matched = 0;
        var used = new bool[candidateTokens.Length];

        foreach (var src in sourceTokens)
        {
            for (var j = 0; j < candidateTokens.Length; j++)
            {
                if (used[j]) continue;

                var distance = StringSimilarity.LevenshteinDistance(src, candidateTokens[j]);
                var threshold = src.Length < 6 ? 2 : 3;

                if (distance <= threshold)
                {
                    matched++;
                    used[j] = true;
                    break;
                }
            }
        }

        return matched;
    }
}
