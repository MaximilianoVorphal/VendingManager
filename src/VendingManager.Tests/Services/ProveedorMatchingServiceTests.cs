namespace VendingManager.Tests.Services;

using VendingManager.Core.Interfaces;
using VendingManager.Core.Utils;
using VendingManager.Infrastructure.Services;

public class ProveedorMatchingServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IProveedorAliasRepository> _aliasRepoMock;
    private readonly ProveedorMatchingService _service;

    public ProveedorMatchingServiceTests()
    {
        _context = CreateContext();
        SeedCatalog();
        _aliasRepoMock = new Mock<IProveedorAliasRepository>();
        _service = new ProveedorMatchingService(_context, _aliasRepoMock.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"ProveedorMatch_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private void SeedCatalog()
    {
        var catalog = new List<ProveedorCatalog>
        {
            new()
            {
                Id = 1,
                NombreCanonical = "LIDER",
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = 2,
                NombreCanonical = "ALVI SpA",
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = 3,
                NombreCanonical = "Coca Cola Embotelladora",
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = 4,
                NombreCanonical = "ALSA",
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = 5,
                NombreCanonical = "Distribuidora El Molino SA",
                CreatedAt = DateTime.UtcNow
            }
        };
        _context.ProveedorCatalog.AddRange(catalog);
        _context.SaveChanges();
    }

    // ─── Step 0a: Exact Alias Lookup ──────────────────────────────────────

    [Fact]
    public async Task MatchAsync_ExactAlias_ReturnsFullConfidence()
    {
        // Arrange: alias exists for "LIDER" normalized
        var normalized = NameNormalizer.Normalize("LIDER");
        _aliasRepoMock
            .Setup(r => r.GetByNormalizedNameAsync(normalized, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProveedorAlias
            {
                Id = 1,
                RawName = "LIDER",
                RawNameNormalized = normalized,
                ProveedorCatalogId = 1,
                ProveedorCatalog = _context.ProveedorCatalog.Find(1)
            });

        // Act
        var result = await _service.MatchAsync("LIDER");

        // Assert
        result.ProveedorCatalog.Should().NotBeNull();
        result.ProveedorCatalog!.Id.Should().Be(1);
        result.Confidence.Should().Be(1.0);
        result.SugerirCreacion.Should().BeFalse();
        result.MatchMethod.Should().Be(ProveedorMatchMethod.ExactAlias);
    }

    // ─── Step 0b: Exact Canonical Match ───────────────────────────────────

    [Fact]
    public async Task MatchAsync_ExactCanonical_ReturnsFullConfidence()
    {
        // Act — "LIDER" matches catalog NombreCanonical "LIDER" via normalization
        var result = await _service.MatchAsync("LIDER");

        // Assert: normalized "lider" == normalized "lider", should match as ExactCanonical
        result.ProveedorCatalog.Should().NotBeNull();
        result.ProveedorCatalog!.Id.Should().Be(1);
        result.Confidence.Should().Be(1.0);
        result.MatchMethod.Should().Be(ProveedorMatchMethod.ExactCanonical);
    }

    [Fact]
    public async Task MatchAsync_AccentedVariant_ResolvesViaExactCanonical()
    {
        // Act — "LÍDER" (accented) matches catalog "LIDER" via normalization
        var result = await _service.MatchAsync("LÍDER");

        // Assert: normalized "líder" → "lider" == "lider"
        result.ProveedorCatalog.Should().NotBeNull();
        result.ProveedorCatalog!.Id.Should().Be(1);
        result.Confidence.Should().Be(1.0);
        result.MatchMethod.Should().Be(ProveedorMatchMethod.ExactCanonical);
    }

    // ─── Step 1: Multi-token Fuzzy ───────────────────────────────────────

    [Fact]
    public async Task MatchAsync_FuzzyAboveThreshold_ReturnsTokenizedMatch()
    {
        // "Distribuidora Molino SA" should fuzzy-match "Distribuidora El Molino SA" ≥ 0.6
        var result = await _service.MatchAsync("Distribuidora Molino SA");

        result.ProveedorCatalog.Should().NotBeNull();
        result.ProveedorCatalog!.Id.Should().Be(5);
        result.Confidence.Should().BeGreaterOrEqualTo(0.6);
        result.SugerirCreacion.Should().BeFalse();
        result.MatchMethod.Should().Be(ProveedorMatchMethod.Tokenized);
    }

    [Fact]
    public async Task MatchAsync_FuzzyBelowThreshold_ReturnsPending()
    {
        // "Alguna Otra Cosa" should have very low similarity to any catalog entry
        var result = await _service.MatchAsync("Alguna Otra Cosa");

        result.ProveedorCatalog.Should().BeNull();
        result.Confidence.Should().Be(0.0);
        result.SugerirCreacion.Should().BeTrue();
        result.MatchMethod.Should().Be(ProveedorMatchMethod.None);
    }

    // ─── Short-Name Rule ─────────────────────────────────────────────────

    [Fact]
    public async Task MatchAsync_ShortName_SingleTokenRaw_OnlyExactNotFuzzy()
    {
        // "ALVI" is a known catalog entry, but if it's not an alias and the raw
        // name has no alias, single-token fuzzy is blocked → PENDING
        // But Step-0b should catch it: normalized "alvi" == "alvi spa"? No.
        // So both Step-0 fail → PENDING

        // Seed alias repo returns null for "alvi"
        _aliasRepoMock
            .Setup(r => r.GetByNormalizedNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProveedorAlias?)null);

        var result = await _service.MatchAsync("ALVI");

        // Single-token raw "alvi" vs single-token "alsa" → short-name rule blocks fuzzy
        // Actually "alvi spa" tokenizes to ["alvi", "spa"], and "alsa" → ["alsa"]
        // Raw "ALVI" → ["alvi"] — single token
        // min(1, 1) = 1 <= 1 → skip fuzzy → PENDING
        result.ProveedorCatalog.Should().BeNull();
        result.Confidence.Should().Be(0.0);
        result.MatchMethod.Should().Be(ProveedorMatchMethod.None);
    }

    [Fact]
    public async Task MatchAsync_ShortName_SingleTokenCandidate_SkipsFuzzyForThatCandidate()
    {
        // "ALVI SpA" (multi-token raw: ["alvi", "spa"]) vs "ALSA" (single-token candidate: ["alsa"])
        // min(2, 1) = 1 ≤ 1 → skip "ALSA" for fuzzy acceptance
        // No exact match → PENDING
        // But "ALVI SpA" DOES match "ALVI SpA" exactly via Step-0b normalized!
        var result = await _service.MatchAsync("ALVI SpA");

        // Step-0b catches it: normalized "alvi spa" == "alvi spa"
        result.ProveedorCatalog.Should().NotBeNull();
        result.ProveedorCatalog!.Id.Should().Be(2);
        result.MatchMethod.Should().Be(ProveedorMatchMethod.ExactCanonical);
    }

    // ─── Zero-Token Input ────────────────────────────────────────────────

    [Fact]
    public async Task MatchAsync_ZeroTokenRaw_ReturnsPending()
    {
        // "EM" — both chars < 3, Tokenize returns 0 tokens
        var result = await _service.MatchAsync("EM");

        result.ProveedorCatalog.Should().BeNull();
        result.Confidence.Should().Be(0.0);
        result.SugerirCreacion.Should().BeTrue();
        result.MatchMethod.Should().Be(ProveedorMatchMethod.None);
    }

    // ─── Blank Input ─────────────────────────────────────────────────────

    [Fact]
    public async Task MatchAsync_BlankRaw_ReturnsPending()
    {
        var result = await _service.MatchAsync("");

        result.ProveedorCatalog.Should().BeNull();
        result.Confidence.Should().Be(0.0);
        result.SugerirCreacion.Should().BeTrue();
        result.MatchMethod.Should().Be(ProveedorMatchMethod.None);
    }

    [Fact]
    public async Task MatchAsync_NullRaw_ReturnsPending()
    {
        var result = await _service.MatchAsync(null!);

        result.ProveedorCatalog.Should().BeNull();
        result.Confidence.Should().Be(0.0);
        result.SugerirCreacion.Should().BeTrue();
        result.MatchMethod.Should().Be(ProveedorMatchMethod.None);
    }

    // ─── Backfill Threshold ──────────────────────────────────────────────

    [Fact]
    public async Task MatchAsync_BackfillThreshold_Above085_ReturnsTokenized()
    {
        // "Distribuidora El Molino SA" is an exact match → 1.0 ≥ 0.85
        var result = await _service.MatchAsync("Distribuidora El Molino SA", 0.85);

        result.ProveedorCatalog.Should().NotBeNull();
        result.ProveedorCatalog!.Id.Should().Be(5);
        result.Confidence.Should().BeGreaterOrEqualTo(0.85);
        result.MatchMethod.Should().Be(ProveedorMatchMethod.ExactCanonical);
    }

    [Fact]
    public async Task MatchAsync_BackfillThreshold_Below085_ReturnsPending()
    {
        // "ABC Distribuidora" has 1/3 token overlap with "Distribuidora El Molino SA" → 0.333 < 0.85
        var result = await _service.MatchAsync("ABC Distribuidora", 0.85);

        result.ProveedorCatalog.Should().BeNull();
        result.SugerirCreacion.Should().BeTrue();
    }

    // ─── SaveAliasAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task SaveAliasAsync_NewAlias_CallsAddAsync()
    {
        // Act
        await _service.SaveAliasAsync("LIDER EXPRESS", 1);

        // Assert: alias was added via repository
        _aliasRepoMock.Verify(r => r.AddAsync(
            It.Is<ProveedorAlias>(a =>
                a.RawName == "LIDER EXPRESS" &&
                a.RawNameNormalized == "lider express" &&
                a.ProveedorCatalogId == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAliasAsync_ExistingAlias_CallsUpdateAsync()
    {
        // Arrange: alias already exists
        var normalized = NameNormalizer.Normalize("ALVI");
        _aliasRepoMock
            .Setup(r => r.GetByNormalizedNameAsync(normalized, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProveedorAlias
            {
                Id = 10,
                RawName = "ALVI",
                RawNameNormalized = normalized,
                ProveedorCatalogId = 2,
                CreatedAt = DateTime.UtcNow.AddDays(-30),
                LastSeenAt = DateTime.UtcNow.AddDays(-30)
            });

        // Act: save same raw name but to a DIFFERENT catalog (alias-move scenario)
        await _service.SaveAliasAsync("ALVI", 2);

        // Assert: update called with changed ProveedorCatalogId and updated LastSeenAt
        _aliasRepoMock.Verify(r => r.UpdateAsync(
            It.Is<ProveedorAlias>(a =>
                a.Id == 10 &&
                a.ProveedorCatalogId == 2 &&
                a.LastSeenAt != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
