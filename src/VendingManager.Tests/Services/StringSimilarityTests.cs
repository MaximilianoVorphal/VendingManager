namespace VendingManager.Tests.Services;

using VendingManager.Core.Utils;

public class StringSimilarityTests
{
    // ─── LevenshteinDistance ──────────────────────────────────────────────

    [Fact]
    public void LevenshteinDistance_IdenticalStrings_ReturnsZero()
    {
        var result = StringSimilarity.LevenshteinDistance("coca cola", "coca cola");
        result.Should().Be(0);
    }

    [Fact]
    public void LevenshteinDistance_DifferentStrings_ReturnsCorrectDistance()
    {
        var result = StringSimilarity.LevenshteinDistance("kitten", "sitting");
        result.Should().Be(3);
    }

    [Fact]
    public void LevenshteinDistance_EmptyString_ReturnsLengthOfOther()
    {
        var result = StringSimilarity.LevenshteinDistance("", "abcdef");
        result.Should().Be(6);
    }

    [Fact]
    public void LevenshteinDistance_BothEmpty_ReturnsZero()
    {
        var result = StringSimilarity.LevenshteinDistance("", "");
        result.Should().Be(0);
    }

    [Fact]
    public void LevenshteinDistance_CaseDifference_ReturnsNonZero()
    {
        var result = StringSimilarity.LevenshteinDistance("Coca", "coca");
        result.Should().Be(1);
    }

    [Fact]
    public void LevenshteinDistance_SingleCharSubstitution_ReturnsOne()
    {
        var result = StringSimilarity.LevenshteinDistance("cat", "car");
        result.Should().Be(1);
    }

    // ─── Tokenize ─────────────────────────────────────────────────────────

    [Fact]
    public void Tokenize_NormalString_SplitsAndLowercases()
    {
        var result = StringSimilarity.Tokenize("COCA COLA LATA 350");
        result.Should().BeEquivalentTo(["coca", "cola", "lata", "350"]);
    }

    [Fact]
    public void Tokenize_WithPunctuation_RemovesSpecialChars()
    {
        var result = StringSimilarity.Tokenize("COCA-COLA LATA (350cc)");
        result.Should().BeEquivalentTo(["coca", "cola", "lata", "350cc"]);
    }

    [Fact]
    public void Tokenize_EmptyString_ReturnsEmptyArray()
    {
        var result = StringSimilarity.Tokenize("");
        result.Should().BeEmpty();
    }

    [Fact]
    public void Tokenize_ShortTokens_ExcludesTokensUnderThreeChars()
    {
        var result = StringSimilarity.Tokenize("TV 4K LED");
        result.Should().BeEquivalentTo(["led"]);
    }

    [Fact]
    public void Tokenize_NullInput_ReturnsEmptyArray()
    {
        var result = StringSimilarity.Tokenize(null!);
        result.Should().BeEmpty();
    }

    // ─── TokenizedSimilarity ──────────────────────────────────────────────

    [Fact]
    public void TokenizedSimilarity_ExactMatch_ReturnsOne()
    {
        var result = StringSimilarity.TokenizedSimilarity("COCA COLA LATA 350 CC", "Coca Cola Lata 350cc");
        result.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void TokenizedSimilarity_PartialMatch_ReturnsCorrectRatio()
    {
        // "coca cola" (2 tokens) vs "coca cola lata 350cc" (4 tokens)
        // 2 out of 4 = 0.5
        var result = StringSimilarity.TokenizedSimilarity("COCA COLA", "Coca Cola Lata 350cc");
        result.Should().BeApproximately(0.5, 0.01);
    }

    [Fact]
    public void TokenizedSimilarity_NoMatch_ReturnsZero()
    {
        var result = StringSimilarity.TokenizedSimilarity("SPRITE 3 LT", "Coca Cola Lata 350cc");
        result.Should().Be(0.0);
    }

    [Fact]
    public void TokenizedSimilarity_FuzzyTypos_ToleratesLevenshtein()
    {
        // "chocolat" vs "chocolate" — distance 1, both ≥ 6 chars → threshold 3, so match
        // 1 out of 1 token matches = 1.0
        var result = StringSimilarity.TokenizedSimilarity("CHOCOLAT", "Chocolate");
        result.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void TokenizedSimilarity_DifferentCase_StillMatches()
    {
        var result = StringSimilarity.TokenizedSimilarity("COCA", "coca");
        result.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void TokenizedSimilarity_ShortTokenWithTypo_StillMatchesWithinTolerance()
    {
        // "coca" (4 chars) vs "coka" (4 chars) — distance 1 ≤ 2 → match
        var result = StringSimilarity.TokenizedSimilarity("COCA", "Coka");
        result.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void TokenizedSimilarity_EmptySource_ReturnsZero()
    {
        var result = StringSimilarity.TokenizedSimilarity("", "Coca Cola");
        result.Should().Be(0.0);
    }

    [Fact]
    public void TokenizedSimilarity_EmptyTarget_ReturnsZero()
    {
        var result = StringSimilarity.TokenizedSimilarity("Coca Cola", "");
        result.Should().Be(0.0);
    }

    [Fact]
    public void TokenizedSimilarity_NullSource_ReturnsZero()
    {
        var result = StringSimilarity.TokenizedSimilarity(null!, "Coca Cola");
        result.Should().Be(0.0);
    }

    [Fact]
    public void TokenizedSimilarity_OnlyShortTokens_ReturnsZero()
    {
        // "TV" (< 3 chars, filtered out) vs "Television" — no tokens to match
        var result = StringSimilarity.TokenizedSimilarity("TV", "Television");
        result.Should().Be(0.0);
    }
}
