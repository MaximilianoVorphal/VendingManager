namespace VendingManager.Core.Configuration;

/// <summary>
/// Maps expense categories to keyword lists for automatic inference from proveedor names.
/// Injected via IOptionsSnapshot for per-request config reload.
/// </summary>
public class CategoriaInferenciaConfig
{
    /// <summary>
    /// Category name → list of keywords (case-insensitive matching on proveedor names).
    /// </summary>
    public Dictionary<string, List<string>> Keywords { get; set; } = new();

    /// <summary>
    /// Returns the default keyword set matching the current hardcoded behavior.
    /// Used as fallback when the config section is absent or empty.
    /// </summary>
    public static readonly Dictionary<string, List<string>> DefaultKeywords = new()
    {
        ["LOGISTICA"] = new List<string> { "bencina", "copec", "shell", "petrobras", "petro", "gasolin" },
        ["PEAJES"] = new List<string> { "peaje", "autopista", "tag", "costanera", "vespucio" }
    };
}
