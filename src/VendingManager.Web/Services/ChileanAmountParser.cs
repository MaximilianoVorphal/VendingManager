using System.Globalization;
using System.Text;

namespace VendingManager.Web.Services;

/// <summary>
/// Parses user-entered monetary amounts following Chilean (es-CL) conventions,
/// where the period is the thousands separator and the comma is the decimal
/// separator. Also accepts a few common tolerant variants so the field never
/// silently loses data when the user types in a slightly off format.
///
/// Accepted inputs (all resolve to 340000):
///   "340.000"  – period as thousands separator (canonical Chilean form)
///   "340000"   – no separator at all
///   "340,000"  – comma as thousands separator (lenient, common typo)
///
/// Accepted decimals (all resolve to 340.5):
///   "340,5"    – comma as decimal separator (canonical Chilean)
///   "340.5"    – period as decimal separator (lenient)
///
/// Mixed European form (resolves to 1000.5):
///   "1.000,5"  – dots as thousands, comma as decimal
///
/// Empty / whitespace / unparseable inputs return false with value = 0.
/// </summary>
public static class ChileanAmountParser
{
    public static bool TryParse(string? input, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var s = input.Trim();

        var negative = false;
        if (s.StartsWith('-'))
        {
            negative = true;
            s = s[1..].Trim();
        }
        if (s.StartsWith('$'))
        {
            s = s[1..].Trim();
        }

        if (s.Length == 0)
            return false;

        // Reject anything other than digits, '.', ',' and whitespace we already stripped.
        foreach (var c in s)
        {
            if (!char.IsDigit(c) && c != '.' && c != ',')
                return false;
        }

        var dots = 0;
        var commas = 0;
        foreach (var c in s)
        {
            if (c == '.') dots++;
            else if (c == ',') commas++;
        }

        string normalized;
        if (dots == 0 && commas == 0)
        {
            normalized = s;
        }
        else if (dots > 0 && commas == 0)
        {
            normalized = NormalizeSingleType(s, '.', thousandsWhen: 3);
        }
        else if (commas > 0 && dots == 0)
        {
            // If we strip commas, replace with '.' for the decimal branch.
            normalized = NormalizeSingleType(s, ',', thousandsWhen: 3, replacement: '.');
        }
        else
        {
            // Mixed (e.g. "1.000,5"). The rightmost separator is the decimal mark;
            // every other separator is a thousands separator and must be stripped.
            normalized = NormalizeMixed(s);
        }

        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return false;

        value = negative ? -parsed : parsed;
        return true;
    }

    /// <summary>
    /// Handles a string containing only one kind of separator ('.' or ',').
    /// If the separator appears more than once, every occurrence is a thousands
    /// separator and gets stripped. If it appears exactly once and is followed
    /// by exactly <paramref name="thousandsWhen"/> digits, it is treated as a
    /// thousands separator too (this is what makes "340.000" → 340000 work).
    /// Otherwise the lone separator is the decimal mark.
    /// </summary>
    private static string NormalizeSingleType(string s, char sep, int thousandsWhen, char replacement = '.')
    {
        var count = 0;
        foreach (var c in s)
            if (c == sep) count++;

        if (count > 1)
        {
            // Multiple of the same char → all are thousands separators.
            return s.Replace(sep.ToString(), string.Empty);
        }

        // Exactly one occurrence: decide by the digit count that follows.
        var idx = s.IndexOf(sep);
        var after = s[(idx + 1)..];
        if (after.Length == thousandsWhen && idx > 0)
        {
            // Looks like a thousands separator (e.g. "340.000").
            return s.Replace(sep.ToString(), string.Empty);
        }

        // Otherwise it's the decimal mark. Replace the original char with '.'
        // so InvariantCulture can parse it.
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(c == sep ? replacement : c);
        return sb.ToString();
    }

    /// <summary>
    /// Handles a string that mixes '.' and ',' (e.g. "1.000,5" or "1,000.5").
    /// The rightmost separator is the decimal mark; everything before it is
    /// integer + thousands separators, which we strip.
    /// </summary>
    private static string NormalizeMixed(string s)
    {
        var lastSepIdx = -1;
        for (var i = s.Length - 1; i >= 0; i--)
        {
            if (s[i] == '.' || s[i] == ',')
            {
                lastSepIdx = i;
                break;
            }
        }

        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '.' || c == ',')
            {
                if (i == lastSepIdx)
                    sb.Append('.');
                // else: thousands separator → drop
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
