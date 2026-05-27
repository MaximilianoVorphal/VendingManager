using System.Text.RegularExpressions;

namespace VendingManager.Core.Utils;

public static partial class StringSimilarity
{
    /// <summary>
    /// Calcula la distancia de Levenshtein entre dos strings.
    /// Extraído de RecargaOcrService para ser compartido.
    /// </summary>
    public static int LevenshteinDistance(string s1, string s2)
    {
        if (s1 == null) throw new ArgumentNullException(nameof(s1));
        if (s2 == null) throw new ArgumentNullException(nameof(s2));

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

    /// <summary>
    /// Tokeniza un string: lowercase, split por espacios, remueve puntuación, filtra tokens &lt; 3 chars.
    /// </summary>
    public static string[] Tokenize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        // Remover puntuación (todo lo que no sea letra, dígito o espacio)
        var cleaned = NonWordCharRegex().Replace(text, " ");
        var tokens = cleaned
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 3)
            .ToArray();

        return tokens;
    }

    /// <summary>
    /// Calcula el ratio de similitud tokenizada entre dos strings.
    /// Cada token source se matchea contra tokens target usando Levenshtein con umbral por longitud.
    /// Ratio = tokens matcheados / max(tokens_source, tokens_target) en rango [0, 1].
    /// </summary>
    public static double TokenizedSimilarity(string source, string target)
    {
        var sourceTokens = Tokenize(source);
        var targetTokens = Tokenize(target);

        if (sourceTokens.Length == 0 || targetTokens.Length == 0)
            return 0.0;

        var matched = 0;
        var used = new bool[targetTokens.Length];

        foreach (var srcToken in sourceTokens)
        {
            for (var j = 0; j < targetTokens.Length; j++)
            {
                if (used[j]) continue;

                var distance = LevenshteinDistance(srcToken, targetTokens[j]);
                var maxLen = Math.Max(srcToken.Length, targetTokens[j].Length);

                // Threshold por longitud: ≤ 2 para < 6 chars, ≤ 3 para ≥ 6 chars
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
        return (double)matched / maxTokens;
    }

    [GeneratedRegex(@"[^\w\d]")]
    private static partial Regex NonWordCharRegex();
}
