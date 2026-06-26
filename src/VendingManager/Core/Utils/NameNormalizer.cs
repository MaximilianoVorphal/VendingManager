using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VendingManager.Core.Utils;

/// <summary>
/// Pure utility for normalizing supplier names for case- and diacritic-insensitive comparison.
/// FormD decomposition + strip NonSpacingMark characters: "LÍDER" → "lider", "café" → "cafe".
/// No I/O, no DI, fully deterministic and unit-testable.
/// </summary>
public static partial class NameNormalizer
{
    /// <summary>
    /// Normalizes a raw supplier name: null/whitespace guard → "", trim + ToLowerInvariant,
    /// collapse internal whitespace via Regex, FormD decompose, strip combining diacritics,
    /// and recompose to FormC.
    /// </summary>
    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var trimmed = raw.Trim().ToLowerInvariant();
        var collapsed = WhitespaceRegex().Replace(trimmed, " ");
        var decomposed = collapsed.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
