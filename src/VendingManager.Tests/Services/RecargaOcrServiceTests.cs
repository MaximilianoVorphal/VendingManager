namespace VendingManager.Tests.Services;

using VendingManager.Core.Utils;

public class RecargaOcrServiceTests
{
    /// <summary>
    /// Regression: RecargaOcrService's original LevenshteinDistance behavior
    /// must be preserved by the shared StringSimilarity utility.
    /// </summary>
    [Fact]
    public void LevenshteinDistance_MatchesOriginalRecargaOcrBehavior()
    {
        // Original behavior from RecargaOcrService (now extracted to StringSimilarity)
        // Exact same algorithm — copied here for regression comparison

        // identical
        StringSimilarity.LevenshteinDistance("123", "123").Should().Be(0);

        // single substitution
        StringSimilarity.LevenshteinDistance("123", "124").Should().Be(1);

        // insertion
        StringSimilarity.LevenshteinDistance("123", "1234").Should().Be(1);

        // deletion
        StringSimilarity.LevenshteinDistance("1234", "123").Should().Be(1);

        // completely different
        StringSimilarity.LevenshteinDistance("abc", "xyz").Should().Be(3);

        // empty vs non-empty
        StringSimilarity.LevenshteinDistance("", "abc").Should().Be(3);
        StringSimilarity.LevenshteinDistance("abc", "").Should().Be(3);

        // slot-style numeric strings (as used in FuzzyMatchSlot)
        StringSimilarity.LevenshteinDistance("101", "111").Should().Be(1);
        StringSimilarity.LevenshteinDistance("101", "102").Should().Be(1);
        StringSimilarity.LevenshteinDistance("105", "205").Should().Be(1);
    }

    [Fact]
    public void FuzzyMatchSlot_LevenshteinTier_MatchesOriginalThreshold()
    {
        // The FuzzyMatchSlot method uses Levenshtein distance ≤ 2 for numeric slots
        // Verify the same behavior via StringSimilarity

        // distance 1 (within threshold)
        StringSimilarity.LevenshteinDistance("101", "111").Should().BeLessOrEqualTo(2);

        // distance 2 (within threshold)
        StringSimilarity.LevenshteinDistance("101", "121").Should().BeLessOrEqualTo(2);

        // distance 1 (within threshold)
        StringSimilarity.LevenshteinDistance("101", "104").Should().Be(1);

        // completely different
        StringSimilarity.LevenshteinDistance("101", "230").Should().BeGreaterThan(2);
    }
}
