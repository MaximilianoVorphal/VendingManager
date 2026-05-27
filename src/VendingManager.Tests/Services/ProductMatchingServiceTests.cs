namespace VendingManager.Tests.Services;

using Moq;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Infrastructure.Data.Repositories;
using VendingManager.Infrastructure.Services;

public class ProductMatchingServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ProductMatchingService _service;

    public ProductMatchingServiceTests()
    {
        _context = CreateContext();
        SeedCatalog();
        var eanRepo = new ProductoEANRepository(_context);
        _service = new ProductMatchingService(_context, eanRepo);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"ProductMatching_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private void SeedCatalog()
    {
        var products = new List<Producto>
        {
            new()
            {
                Id = 1,
                Nombre = "Coca Cola Lata 350cc",
                CodigoBarras = "7791234567890",
                SKU = "COC-001"
            },
            new()
            {
                Id = 2,
                Nombre = "Sprite 3 LT",
                CodigoBarras = "7791234567891",
                SKU = "SPR-001"
            },
            new()
            {
                Id = 3,
                Nombre = "Agua Mineral Sin Gas 500cc",
                CodigoBarras = "7791234567892",
                SKU = "AGU-001"
            }
        };
        _context.Productos.AddRange(products);
        _context.SaveChanges();
    }

    // ─── Exact Match ─────────────────────────────────────────────────────

    [Fact]
    public async Task MatchAsync_ExactMatch_ReturnsProductWithHighConfidence()
    {
        var result = await _service.MatchAsync("Coca Cola Lata 350cc");

        result.Should().NotBeNull();
        result.Producto.Should().NotBeNull();
        result.Producto!.Id.Should().Be(1);
        result.Confidence.Should().BeGreaterOrEqualTo(0.6);
        result.SugerirCreacion.Should().BeFalse();
        result.MatchMethod.Should().Be(MatchMethod.Tokenized);
    }

    // ─── Fuzzy / Typo Tolerance ──────────────────────────────────────────

    [Fact]
    public async Task MatchAsync_FuzzyMatch_CocaColaLata350CC_MatchesCatalog()
    {
        // "COCA COLA LATA 350 CC" → should match "Coca Cola Lata 350cc"
        var result = await _service.MatchAsync("COCA COLA LATA 350 CC");

        result.Should().NotBeNull();
        result.Producto.Should().NotBeNull();
        result.Producto!.Id.Should().Be(1);
        result.Confidence.Should().BeGreaterOrEqualTo(0.6);
        result.SugerirCreacion.Should().BeFalse();
    }

    [Fact]
    public async Task MatchAsync_FuzzyMatch_TyposTolerated()
    {
        // "COCA COLA LATA 350CC" with typo in "COLA" (shouldn't matter since Levenshtein handles it)
        var result = await _service.MatchAsync("COCA KOLA LATA 350CC");

        result.Should().NotBeNull();
        result.Producto.Should().NotBeNull();
        result.Producto!.Id.Should().Be(1);
    }

    // ─── No Match ─────────────────────────────────────────────────────────

    [Fact]
    public async Task MatchAsync_NoMatch_ReturnsNullWithSugerirCreacionTrue()
    {
        var result = await _service.MatchAsync("YERBA MATE PLAYADITO 1KG");

        result.Should().NotBeNull();
        result.Producto.Should().BeNull();
        result.SugerirCreacion.Should().BeTrue();
        result.Confidence.Should().Be(0.0);
        result.MatchMethod.Should().Be(MatchMethod.None);
    }

    [Fact]
    public async Task MatchAsync_FalsePositive_COCA3LT_DoesNotMatch_Sprite3LT()
    {
        // "COCA 3 LT" should NOT match "Sprite 3 LT" (only "3" overlaps — false positive blocked)
        var result = await _service.MatchAsync("COCA 3 LT");

        result.Should().NotBeNull();
        result.Producto.Should().BeNull();
        result.SugerirCreacion.Should().BeTrue();
    }

    // ─── Barcode Match ────────────────────────────────────────────────────

    [Fact]
    public async Task MatchAsync_BarcodeName_MatchesExactByCodigoBarras()
    {
        // If the product name IS a barcode, match by CodigoBarras
        var result = await _service.MatchAsync("7791234567890");

        result.Should().NotBeNull();
        result.Producto.Should().NotBeNull();
        result.Producto!.Id.Should().Be(1);
        result.Confidence.Should().Be(1.0);
        result.MatchMethod.Should().Be(MatchMethod.Barcode);
        result.SugerirCreacion.Should().BeFalse();
    }

    [Fact]
    public async Task MatchAsync_BarcodeNoMatch_FallsThroughToFuzzy()
    {
        // Non-existent barcode should fall through to fuzzy matching
        var result = await _service.MatchAsync("7799999999999");

        result.Should().NotBeNull();
        result.Producto.Should().BeNull();
        result.SugerirCreacion.Should().BeTrue();
    }

    // ─── Empty / Null Input ───────────────────────────────────────────────

    [Fact]
    public async Task MatchAsync_NullInput_ReturnsNoMatch()
    {
        var result = await _service.MatchAsync(null!);

        result.Should().NotBeNull();
        result.Producto.Should().BeNull();
        result.SugerirCreacion.Should().BeTrue();
        result.MatchMethod.Should().Be(MatchMethod.None);
    }

    [Fact]
    public async Task MatchAsync_EmptyString_ReturnsNoMatch()
    {
        var result = await _service.MatchAsync("");

        result.Should().NotBeNull();
        result.Producto.Should().BeNull();
        result.SugerirCreacion.Should().BeTrue();
    }

    [Fact]
    public async Task MatchAsync_WhitespaceOnly_ReturnsNoMatch()
    {
        var result = await _service.MatchAsync("   ");

        result.Should().NotBeNull();
        result.Producto.Should().BeNull();
        result.SugerirCreacion.Should().BeTrue();
    }

    // ─── Threshold Boundary ───────────────────────────────────────────────

    [Fact]
    public async Task MatchAsync_LowOverlap_BelowThreshold_ReturnsNoMatch()
    {
        // "Agua Mineral" (2 tokens) vs "Agua Mineral Sin Gas 500cc" (5 tokens)
        // 2/5 = 0.4 < 0.6 → no match
        var result = await _service.MatchAsync("Agua Mineral");

        // Wait — 2 out of 5 is 0.4, below 0.6. But "agua" and "mineral" match exactly.
        // Actually tokens: source ["agua","mineral"] vs target ["agua","mineral","sin","gas","500cc"]
        // matched=2, max=5, ratio=0.4. Below 0.6 threshold. Should be no match.
        result.Should().NotBeNull();
        result.Producto.Should().BeNull();
        result.SugerirCreacion.Should().BeTrue();
    }

    [Fact]
    public async Task MatchAsync_HighOverlap_AboveThreshold_ReturnsMatch()
    {
        // "Agua Mineral Sin Gas" (4 tokens) vs "Agua Mineral Sin Gas 500cc" (5 tokens)
        // 4/5 = 0.8 ≥ 0.6 → match
        var result = await _service.MatchAsync("Agua Mineral Sin Gas");

        result.Should().NotBeNull();
        result.Producto.Should().NotBeNull();
        result.Producto!.Id.Should().Be(3);
        result.Confidence.Should().BeGreaterOrEqualTo(0.6);
        result.SugerirCreacion.Should().BeFalse();
    }

    // ─── Tie-Breaking by Overlap ─────────────────────────────────────────

    [Fact]
    public async Task MatchAsync_TieBreaking_ByOverlap_SelectsHigherOverlap()
    {
        // Set threshold bajo para que scores de 0.5 pasen y se pueda verificar el tie-breaker
        var tieService = new ProductMatchingService(_context, new ProductoEANRepository(_context), threshold: 0.4);

        _context.Productos.Add(new Producto
        {
            Id = 4,
            Nombre = "Palta Tomate Cebolla Lechuga",
            CodigoBarras = "7790000000040",
            SKU = "TIE-001"
        });
        _context.Productos.Add(new Producto
        {
            Id = 5,
            Nombre = "Palta Cebolla",
            CodigoBarras = "7790000000050",
            SKU = "TIE-002"
        });
        _context.SaveChanges();

        // Source: "Palta Tomate" (2 tokens: ["palta", "tomate"])
        //   Producto 4: "Palta Tomate Cebolla Lechuga" (4 tokens) → match "palta","tomate" → matched=2, max=4, score=0.5, overlap=2
        //   Producto 5: "Palta Cebolla" (2 tokens) → match solo "palta", max=2, score=0.5, overlap=1
        // Mismo ratio (0.5) pero distinto overlap → gana producto con overlap mayor (Producto 4)
        var result = await tieService.MatchAsync("Palta Tomate");

        result.Should().NotBeNull();
        result.Producto.Should().NotBeNull();
        result.Producto!.Id.Should().Be(4);
        result.SugerirCreacion.Should().BeFalse();
    }

    // ─── Custom Threshold ─────────────────────────────────────────────────

    [Fact]
    public async Task MatchAsync_CustomThreshold_RejectsBelowThreshold()
    {
        // Add product that matches "Coca Lata" at ~0.666 but below 0.8
        _context.Productos.Add(new Producto
        {
            Id = 10,
            Nombre = "Coca Cola Lata",
            CodigoBarras = "7790000000100",
            SKU = "THR-001"
        });
        _context.SaveChanges();

        // With 0.6 threshold → should match (0.666 >= 0.6)
        var defaultService = new ProductMatchingService(_context, new ProductoEANRepository(_context), threshold: 0.6);
        var defaultResult = await defaultService.MatchAsync("Coca Lata");
        defaultResult.Producto.Should().NotBeNull();
        defaultResult.Producto!.Id.Should().Be(10);

        // With 0.8 threshold → should NOT match (0.666 < 0.8)
        var strictService = new ProductMatchingService(_context, new ProductoEANRepository(_context), threshold: 0.8);
        var strictResult = await strictService.MatchAsync("Coca Lata");
        strictResult.Producto.Should().BeNull();
        strictResult.SugerirCreacion.Should().BeTrue();
    }

    // ─── Numeric Product Names ────────────────────────────────────────────

    [Fact]
    public async Task MatchAsync_NumericProductName_MatchesCorrectly()
    {
        _context.Productos.Add(new Producto
        {
            Id = 20,
            Nombre = "12345",
            CodigoBarras = "7790000000200",
            SKU = "NUM-001"
        });
        _context.SaveChanges();

        var result = await _service.MatchAsync("12345");

        result.Should().NotBeNull();
        result.Producto.Should().NotBeNull();
        result.Producto!.Id.Should().Be(20);
        result.Confidence.Should().BeGreaterOrEqualTo(0.6);
        result.SugerirCreacion.Should().BeFalse();
    }

    // ─── Special Characters ───────────────────────────────────────────────

    [Fact]
    public async Task MatchAsync_SpecialCharactersInName_StrippedAndMatched()
    {
        // "COCA-COLA LATA (350cc)" → after strip: "coca cola lata 350cc"
        var result = await _service.MatchAsync("COCA-COLA LATA (350cc)");

        result.Should().NotBeNull();
        result.Producto.Should().NotBeNull();
        result.Producto!.Id.Should().Be(1);
    }

    // ─── EAN Step 0 (PR 3) ─────────────────────────────────────────────────

    [Fact]
    public async Task MatchAsync_EanFound_ReturnsEanMatch()
    {
        // Arrange: seed a ProductoEAN mapping for product 1
        _context.ProductoEANs.Add(new ProductoEAN
        {
            EAN = "7791234567890",
            ProductoId = 1,
            PackSize = 1,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        });
        _context.SaveChanges();

        // Act: match by EAN (productName is null-ish, but EAN is provided)
        var result = await _service.MatchAsync("", "7791234567890", null, null);

        // Assert: EAN match with full confidence, fuzzy skipped
        result.Should().NotBeNull();
        result.Producto.Should().NotBeNull();
        result.Producto!.Id.Should().Be(1);
        result.Confidence.Should().Be(1.0);
        result.MatchMethod.Should().Be(MatchMethod.Ean);
        result.SugerirCreacion.Should().BeFalse();
        result.ProductoEAN.Should().NotBeNull();
        result.ProductoEAN!.EAN.Should().Be("7791234567890");
    }

    [Fact]
    public async Task MatchAsync_EanNotFound_FallsThroughToFuzzy()
    {
        // Act: unknown EAN should fall through to tokenized matching
        var result = await _service.MatchAsync("Coca Cola Lata 350cc", "7799999999999", null, null);

        // Assert: EAN not found → falls through to tokenized match
        result.Should().NotBeNull();
        result.Producto.Should().NotBeNull();
        result.Producto!.Id.Should().Be(1);
        result.MatchMethod.Should().Be(MatchMethod.Tokenized);
        result.ProductoEAN.Should().BeNull();
    }

    // ─── SaveMappingAsync (PR 3) ──────────────────────────────────────────

    [Fact]
    public async Task SaveMappingAsync_CreatesNewMapping()
    {
        // Act: save mapping for a new EAN
        await _service.SaveMappingAsync("7798888888888", 2, packSize: 6);

        // Assert: record was created
        var saved = _context.ProductoEANs.FirstOrDefault(e => e.EAN == "7798888888888");
        saved.Should().NotBeNull();
        saved!.ProductoId.Should().Be(2);
        saved.PackSize.Should().Be(6);
        saved.LastSeenAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SaveMappingAsync_UpdatesExistingMapping()
    {
        // Arrange: seed an existing mapping
        _context.ProductoEANs.Add(new ProductoEAN
        {
            EAN = "7791234567890",
            ProductoId = 1,
            PackSize = 1,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow.AddDays(-30)
        });
        _context.SaveChanges();

        // Act: update the mapping — change ProductoId and PackSize
        await _service.SaveMappingAsync("7791234567890", 2, packSize: 12);

        // Assert: record updated
        var updated = _context.ProductoEANs.FirstOrDefault(e => e.EAN == "7791234567890");
        updated.Should().NotBeNull();
        updated!.ProductoId.Should().Be(2);
        updated.PackSize.Should().Be(12);
        updated.LastSeenAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}
