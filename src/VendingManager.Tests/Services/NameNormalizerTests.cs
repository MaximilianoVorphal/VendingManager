namespace VendingManager.Tests.Services;

using VendingManager.Core.Utils;

public class NameNormalizerTests
{
    // ─── Diacritic Stripping ──────────────────────────────────────────────

    [Fact]
    public void Normalize_WithAccentedUppercase_StripsDiacriticsAndLowercases()
    {
        var result = NameNormalizer.Normalize("LÍDER");

        result.Should().Be("lider");
    }

    [Fact]
    public void Normalize_WithAccentedLowercase_StripsDiacritics()
    {
        var result = NameNormalizer.Normalize("café");

        result.Should().Be("cafe");
    }

    [Fact]
    public void Normalize_MultipleAccents_StripsAllDiacritics()
    {
        var result = NameNormalizer.Normalize("Álvaró Sánchez Pérez");

        result.Should().Be("alvaro sanchez perez");
    }

    // ─── Whitespace Handling ──────────────────────────────────────────────

    [Fact]
    public void Normalize_WithExtraWhitespace_TrimsAndCollapses()
    {
        var result = NameNormalizer.Normalize("  EL  MOLINO  ");

        result.Should().Be("el molino");
    }

    [Fact]
    public void Normalize_WithTabsAndNewlines_CollapsesToSingleSpace()
    {
        var result = NameNormalizer.Normalize("ALVI\tSpA\nLtda");

        result.Should().Be("alvi spa ltda");
    }

    // ─── Edge Cases ───────────────────────────────────────────────────────

    [Fact]
    public void Normalize_EmptyString_ReturnsEmptyString()
    {
        var result = NameNormalizer.Normalize("");

        result.Should().Be("");
    }

    [Fact]
    public void Normalize_WhitespaceOnly_ReturnsEmptyString()
    {
        var result = NameNormalizer.Normalize("   ");

        result.Should().Be("");
    }

    [Fact]
    public void Normalize_NullInput_ReturnsEmptyString()
    {
        var result = NameNormalizer.Normalize(null!);

        result.Should().Be("");
    }

    [Fact]
    public void Normalize_AlreadyNormalized_ReturnsSame()
    {
        var result = NameNormalizer.Normalize("el molino");

        result.Should().Be("el molino");
    }

    // ─── Special Characters ───────────────────────────────────────────────

    [Fact]
    public void Normalize_WithNumericAndSymbols_PreservesDigitsAndRemovesNonSpacing()
    {
        var result = NameNormalizer.Normalize("Distribuidora N°2");

        result.Should().Be("distribuidora n°2");
    }
}
