using VendingManager.Core.Entities;

namespace VendingManager.Core.Interfaces;

/// <summary>
/// Matching service that resolves raw OCR supplier strings to canonical ProveedorCatalog entries.
/// Mirrors IProductMatchingService: Step 0a (alias) → Step 0b (exact canonical) → Step 1 (fuzzy).
/// </summary>
public interface IProveedorMatchingService
{
    /// <summary>Matches a raw supplier name against the catalog using the default threshold (0.6).</summary>
    Task<ProveedorMatchResult> MatchAsync(string proveedorRaw);

    /// <summary>Matches with an explicit threshold (used for backfill at 0.85).</summary>
    Task<ProveedorMatchResult> MatchAsync(string proveedorRaw, double threshold);

    /// <summary>
    /// Persists a raw supplier name as a known alias of the given catalog entry (alias learning).
    /// Normalizes the raw name, upserts the alias. NO SaveChanges — atomicity belongs to the caller.
    /// </summary>
    Task SaveAliasAsync(string proveedorRaw, int proveedorCatalogId);
}

/// <summary>
/// Result of a proveedor matching attempt: the matched catalog entry (or null),
/// confidence score, whether creation should be suggested, and the method used.
/// </summary>
public class ProveedorMatchResult
{
    /// <summary>Matched canonical supplier, or null if no match.</summary>
    public ProveedorCatalog? ProveedorCatalog { get; init; }

    /// <summary>Confidence of the match (0.0 to 1.0).</summary>
    public double Confidence { get; init; }

    /// <summary>True when no match was found — caller should suggest creating a new catalog entry.</summary>
    public bool SugerirCreacion { get; init; }

    /// <summary>The matching method that produced this result.</summary>
    public ProveedorMatchMethod MatchMethod { get; init; }
}

/// <summary>
/// Method used to resolve the supplier match. Enum member uses ExactCanonical (not ExactName) — see Design S1.
/// </summary>
public enum ProveedorMatchMethod
{
    /// <summary>No match found.</summary>
    None,

    /// <summary>Match via exact alias lookup by normalized key (Step 0a).</summary>
    ExactAlias,

    /// <summary>Exact match against ProveedorCatalog.NombreCanonical after normalization (Step 0b).</summary>
    ExactCanonical,

    /// <summary>Tokenized fuzzy match via StringSimilarity (Step 1).</summary>
    Tokenized
}
