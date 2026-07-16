namespace VendingManager.Tests.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using VendingManager.Core.Configuration;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.DTOs;
using VendingManager.Tests.TestData;

public class CompraServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IWebHostEnvironment> _mockEnv;
    private readonly IOptions<VendingConfig> _config;
    private readonly Mock<IProductMatchingService> _mockProductMatching;
    private readonly Mock<IProveedorMatchingService> _mockProveedorMatching;
    private readonly Mock<IProveedorAliasRepository> _mockAliasRepo;
    private readonly Mock<IOptionsSnapshot<CategoriaInferenciaConfig>> _mockCategoriaConfig;
    private readonly Mock<ILogger<CompraService>> _mockLogger;
    private readonly CompraService _service;

    public CompraServiceTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"CompraServiceTestDb_{Guid.NewGuid()}");

        _mockEnv = new Mock<IWebHostEnvironment>();
        _mockEnv.SetupGet(e => e.WebRootPath).Returns("/tmp/wwwroot");
        _mockEnv.SetupGet(e => e.ContentRootPath).Returns("/tmp");

        var vendingConfig = new VendingConfig();
        _config = Options.Create(vendingConfig);

        _mockProductMatching = new Mock<IProductMatchingService>();
        _mockProductMatching
            .Setup(m => m.SaveMappingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);

        _mockProveedorMatching = new Mock<IProveedorMatchingService>();
        _mockProveedorMatching
            .Setup(m => m.SaveAliasAsync(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        _mockAliasRepo = new Mock<IProveedorAliasRepository>();

        _mockCategoriaConfig = new Mock<IOptionsSnapshot<CategoriaInferenciaConfig>>();
        _mockCategoriaConfig.Setup(o => o.Value).Returns(new CategoriaInferenciaConfig
        {
            Keywords = CategoriaInferenciaConfig.DefaultKeywords
        });

        _mockLogger = new Mock<ILogger<CompraService>>();

        var uploadProvider = new DefaultUploadPathProvider(_mockEnv.Object, _config);
        _service = new CompraService(
            _context,
            _mockProductMatching.Object,
            uploadProvider,
            _mockProveedorMatching.Object,
            _mockCategoriaConfig.Object,
            _mockLogger.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    // ─── ProveedorCatalogId persistence (T22/T23) ─────────────────────

    [Fact]
    public async Task RegistrarCompraAsync_WithProveedorCatalogId_PersistsForeignKey()
    {
        // Arrange
        var catalog = new ProveedorCatalog { NombreCanonical = "Supplier Test" };
        _context.ProveedorCatalog.Add(catalog);
        await _context.SaveChangesAsync();

        var compra = new Compra
        {
            Proveedor = "Supplier Test",
            ProveedorCatalogId = catalog.Id,
            FechaCompra = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Local),
            Estado = "PAGADA",
            PagadaCaja = false,
            MontoTotal = 0,
            Detalles = new List<DetalleCompra>()
        };

        // Act
        var saved = await _service.RegistrarCompraAsync(compra);

        // Assert
        saved.ProveedorCatalogId.Should().Be(catalog.Id);

        // Re-fetch to verify persistence in DB
        var fetched = await _context.Compras
            .Include(c => c.ProveedorCatalog)
            .FirstOrDefaultAsync(c => c.Id == saved.Id);
        fetched.Should().NotBeNull();
        fetched!.ProveedorCatalogId.Should().Be(catalog.Id);
        fetched.ProveedorCatalog.Should().NotBeNull();
        fetched.ProveedorCatalog!.NombreCanonical.Should().Be("Supplier Test");
    }

    [Fact]
    public async Task ActualizarCompraAsync_WithProveedorCatalogId_UpdatesForeignKey()
    {
        // Arrange
        var catalog = new ProveedorCatalog { NombreCanonical = "Supplier Updated" };
        _context.ProveedorCatalog.Add(catalog);
        await _context.SaveChangesAsync();

        var compra = new Compra
        {
            Proveedor = "Old Supplier",
            FechaCompra = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Local),
            Estado = "PAGADA",
            PagadaCaja = false,
            MontoTotal = 0,
            Detalles = new List<DetalleCompra>()
        };
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync();
        var compraId = compra.Id;

        var request = new RegistrarCompraRequestDto
        {
            Proveedor = "Old Supplier",
            FechaCompra = compra.FechaCompra,
            Estado = "PAGADA",
            PagadaCaja = false,
            ProveedorCatalogId = catalog.Id,
            Detalles = new List<RegistrarDetalleCompraRequestDto>()
        };

        // Act
        await _service.ActualizarCompraAsync(compraId, request);

        // Assert
        var fetched = await _context.Compras
            .Include(c => c.ProveedorCatalog)
            .FirstOrDefaultAsync(c => c.Id == compraId);
        fetched.Should().NotBeNull();
        fetched!.ProveedorCatalogId.Should().Be(catalog.Id);
        fetched.ProveedorCatalog.Should().NotBeNull();
        fetched.ProveedorCatalog!.NombreCanonical.Should().Be("Supplier Updated");
    }

    [Fact]
    public async Task GetComprasAsync_WithProveedorCatalog_ExposesCanonicalName()
    {
        // Arrange
        var catalog = new ProveedorCatalog { NombreCanonical = "Supplier Nav" };
        _context.ProveedorCatalog.Add(catalog);
        await _context.SaveChangesAsync();

        var compra = new Compra
        {
            Proveedor = "Supplier Nav",
            ProveedorCatalogId = catalog.Id,
            FechaCompra = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Local),
            Estado = "PAGADA",
            PagadaCaja = true,
            MontoTotal = 1000,
            Detalles = new List<DetalleCompra>()
        };
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync();

        // Act
        var compras = await _service.GetComprasAsync();

        // Assert
        compras.Should().NotBeEmpty();
        var fetched = compras.First();
        fetched.ProveedorCatalogId.Should().Be(catalog.Id);
        fetched.ProveedorCanonical.Should().Be("Supplier Nav");
    }

    [Fact]
    public async Task GetCompraByIdAsync_WithProveedorCatalog_IncludesNavigationProperty()
    {
        // Arrange
        var catalog = new ProveedorCatalog { NombreCanonical = "Supplier ById" };
        _context.ProveedorCatalog.Add(catalog);
        await _context.SaveChangesAsync();

        var compra = new Compra
        {
            Proveedor = "Supplier ById",
            ProveedorCatalogId = catalog.Id,
            FechaCompra = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Local),
            Estado = "PAGADA",
            PagadaCaja = true,
            MontoTotal = 1000,
            Detalles = new List<DetalleCompra>()
        };
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync();

        // Act
        var fetched = await _service.GetCompraByIdAsync(compra.Id);

        // Assert
        fetched.Should().NotBeNull();
        fetched!.ProveedorCatalogId.Should().Be(catalog.Id);
        fetched.ProveedorCatalog.Should().NotBeNull();
        fetched.ProveedorCatalog!.NombreCanonical.Should().Be("Supplier ById");
    }

    [Fact]
    public async Task RegistrarCompraAsync_ConItemPendienteSinProducto_DeberiaReproducirError()
    {
        // Arrange
        var compra = new Compra
        {
            Proveedor = "Proveedor Test",
            NumeroDocumento = "FACT-001",
            FechaCompra = new DateTime(2026, 5, 27, 10, 0, 0, DateTimeKind.Local),
            Estado = "PENDIENTE",
            PagadaCaja = false,
            TipoFactura = "MERCADERIA",
            MontoTotal = 0,
            Detalles = new List<DetalleCompra>
            {
                new()
                {
                    ProductoId = null,
                    EsPendiente = true,
                    DescripcionItem = null,
                    Cantidad = 1,
                    CostoUnitario = 0,
                    Subtotal = 0
                }
            }
        };

        // Act
        Exception? ex = null;
        try
        {
            var result = await _service.RegistrarCompraAsync(compra);
        }
        catch (Exception e)
        {
            ex = e;
        }

        // Assert — capture full exception chain
        if (ex != null)
        {
            var fullMessage = BuildExceptionChain(ex);
            throw new Xunit.Sdk.XunitException(
                $"ERROR REPRODUCIDO:\n" +
                $"Type: {ex.GetType().FullName}\n" +
                $"Full chain:\n{fullMessage}");
        }

        // Si no hay error, la compra se guardó correctamente
        var savedCompra = await _context.Compras
            .Include(c => c.Detalles)
            .FirstOrDefaultAsync(c => c.Id == compra.Id);
        savedCompra.Should().NotBeNull();
        savedCompra!.Detalles.Should().ContainSingle();
        savedCompra.Detalles[0].ProductoId.Should().BeNull();
        savedCompra.Detalles[0].EsPendiente.Should().BeTrue();
    }

    // ─── ReasignarProveedorAsync (T24/T25) ──────────────────────────

    [Fact]
    public async Task ReasignarProveedorAsync_WithExistingCatalog_SetsForeignKeyAndUpdatesLastSeenAt()
    {
        // Arrange
        var catalog = new ProveedorCatalog { NombreCanonical = "Supplier A" };
        _context.ProveedorCatalog.Add(catalog);
        await _context.SaveChangesAsync();

        var compra = new Compra
        {
            Proveedor = "Supplier A",
            FechaCompra = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Local),
            Estado = "PAGADA",
            PagadaCaja = false,
            MontoTotal = 0,
            Detalles = new List<DetalleCompra>()
        };
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var request = new ReasignarProveedorRequestDto
        {
            ProveedorCatalogId = catalog.Id
        };

        // Act
        var result = await _service.ReasignarProveedorAsync(compra.Id, request);

        // Assert
        result.Should().NotBeNull();
        result.ProveedorCatalogId.Should().Be(catalog.Id);

        var saved = await _context.Compras
            .Include(c => c.ProveedorCatalog)
            .FirstOrDefaultAsync(c => c.Id == compra.Id);
        saved.Should().NotBeNull();
        saved!.ProveedorCatalogId.Should().Be(catalog.Id);

        // Verify LastSeenAt was updated
        var savedCatalog = await _context.ProveedorCatalog.FindAsync(catalog.Id);
        savedCatalog.Should().NotBeNull();
        savedCatalog!.LastSeenAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ReasignarProveedorAsync_CallsSaveAliasAsyncOnce()
    {
        // Arrange
        var catalog = new ProveedorCatalog { NombreCanonical = "Supplier A" };
        _context.ProveedorCatalog.Add(catalog);
        await _context.SaveChangesAsync();

        var compra = new Compra
        {
            Proveedor = "Raw Supplier Name",
            FechaCompra = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Local),
            Estado = "PAGADA",
            PagadaCaja = false,
            MontoTotal = 0,
            Detalles = new List<DetalleCompra>()
        };
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var request = new ReasignarProveedorRequestDto
        {
            ProveedorCatalogId = catalog.Id
        };

        // Act
        await _service.ReasignarProveedorAsync(compra.Id, request);

        // Assert: SaveAliasAsync called once with the raw name and catalog id
        _mockProveedorMatching.Verify(
            m => m.SaveAliasAsync("Raw Supplier Name", catalog.Id),
            Times.Once);
    }

    [Fact]
    public async Task ReasignarProveedorAsync_WithNewCatalogName_CreatesCatalogAndLinks()
    {
        // Arrange
        var compra = new Compra
        {
            Proveedor = "Unknown Supplier",
            FechaCompra = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Local),
            Estado = "PAGADA",
            PagadaCaja = false,
            MontoTotal = 0,
            Detalles = new List<DetalleCompra>()
        };
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var request = new ReasignarProveedorRequestDto
        {
            NuevoNombreCanonical = "New Supplier"
        };

        // Act
        var result = await _service.ReasignarProveedorAsync(compra.Id, request);

        // Assert
        result.Should().NotBeNull();
        result.ProveedorCatalogId.Should().NotBeNull();

        var savedCompra = await _context.Compras
            .Include(c => c.ProveedorCatalog)
            .FirstOrDefaultAsync(c => c.Id == compra.Id);
        savedCompra.Should().NotBeNull();
        savedCompra!.ProveedorCatalogId.Should().NotBeNull();
        savedCompra.ProveedorCatalog.Should().NotBeNull();
        savedCompra.ProveedorCatalog!.NombreCanonical.Should().Be("New Supplier");
    }

    [Fact]
    public async Task ReasignarProveedorAsync_WithMissingCompra_ThrowsKeyNotFoundException()
    {
        // Arrange
        var request = new ReasignarProveedorRequestDto
        {
            ProveedorCatalogId = 999
        };

        // Act & Assert
        var act = () => _service.ReasignarProveedorAsync(99999, request);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task ReasignarProveedorAsync_AliasMove_RemovesAliasFromOldSupplier()
    {
        // Arrange: compra's raw Proveedor "Raw Supplier" is an alias of "Supplier Old"
        var oldCatalog = new ProveedorCatalog { NombreCanonical = "Supplier Old" };
        _context.ProveedorCatalog.Add(oldCatalog);
        var newCatalog = new ProveedorCatalog { NombreCanonical = "Supplier New" };
        _context.ProveedorCatalog.Add(newCatalog);
        await _context.SaveChangesAsync();

        var alias = new ProveedorAlias
        {
            RawName = "Raw Supplier",
            RawNameNormalized = "raw supplier",
            ProveedorCatalogId = oldCatalog.Id
        };
        _context.ProveedorAlias.Add(alias);

        var compra = new Compra
        {
            Proveedor = "Raw Supplier",
            FechaCompra = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Local),
            Estado = "PAGADA",
            PagadaCaja = false,
            MontoTotal = 0,
            Detalles = new List<DetalleCompra>()
        };
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var request = new ReasignarProveedorRequestDto
        {
            ProveedorCatalogId = newCatalog.Id
        };

        // Act
        await _service.ReasignarProveedorAsync(compra.Id, request);

        // Assert: old alias row removed
        var savedAlias = await _context.ProveedorAlias
            .FirstOrDefaultAsync(a => a.RawNameNormalized == "raw supplier");
        savedAlias.Should().BeNull();

        // And SaveAliasAsync was called to create the new alias
        _mockProveedorMatching.Verify(
            m => m.SaveAliasAsync("Raw Supplier", newCatalog.Id),
            Times.Once);
    }

    // ─── Upload signature validation (M-1a, REQ-UPLOAD-02) ──────────────

    [Fact]
    public async Task SaveFacturaImagenAsync_SpoofedContent_ThrowsArgumentException()
    {
        // Arrange
        var compra = new Compra
        {
            Proveedor = "Supplier Upload",
            FechaCompra = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Local),
            Estado = "PAGADA",
            PagadaCaja = false,
            MontoTotal = 0,
            Detalles = new List<DetalleCompra>()
        };
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync();

        // Plain text content renamed with a .jpg extension — signature mismatch.
        var spoofedFile = CreateMockFormFile(
            System.Text.Encoding.ASCII.GetBytes("this is not really a jpeg"),
            "image/jpeg",
            "factura.jpg");

        // Act
        var act = () => _service.SaveFacturaImagenAsync(compra.Id, spoofedFile);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();

        var fetched = await _context.Compras.FindAsync(compra.Id);
        fetched!.FacturaImagen.Should().BeNull();
    }

    [Fact]
    public async Task SaveFacturaImagenAsync_ValidJpegContent_SucceedsUnchanged()
    {
        // Arrange
        var compra = new Compra
        {
            Proveedor = "Supplier Upload Valid",
            FechaCompra = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Local),
            Estado = "PAGADA",
            PagadaCaja = false,
            MontoTotal = 0,
            Detalles = new List<DetalleCompra>()
        };
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync();

        byte[] jpegBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        var validFile = CreateMockFormFile(jpegBytes, "image/jpeg", "factura.jpg");

        // Act
        var path = await _service.SaveFacturaImagenAsync(compra.Id, validFile);

        // Assert
        path.Should().Be($"/api/compras/{compra.Id}/factura");
        var fetched = await _context.Compras.FindAsync(compra.Id);
        fetched!.FacturaImagen.Should().BeEquivalentTo(jpegBytes);
    }

    private static IFormFile CreateMockFormFile(byte[] content, string contentType, string fileName)
    {
        var stream = new MemoryStream(content);
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(content.Length);
        fileMock.Setup(f => f.ContentType).Returns(contentType);
        fileMock.Setup(f => f.FileName).Returns(fileName);
        fileMock.Setup(f => f.OpenReadStream()).Returns(stream);
        fileMock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns((Stream s, CancellationToken ct) => stream.CopyToAsync(s, ct));
        return fileMock.Object;
    }

    // ─── Slice 3: Expense category inference ──────────────────────────

    [Fact]
    public void ResolverCategoriaMovimiento_KeywordMatch_ReturnsMappedCategory()
    {
        // 3.1: Keyword match returns mapped category
        // Arrange
        var config = new CategoriaInferenciaConfig
        {
            Keywords = new Dictionary<string, List<string>>
            {
                ["LOGISTICA"] = new List<string> { "bencina", "copec", "shell" },
                ["PEAJES"] = new List<string> { "peaje", "tag" }
            }
        };
        var mockOptions = new Mock<IOptionsSnapshot<CategoriaInferenciaConfig>>();
        mockOptions.Setup(o => o.Value).Returns(config);
        var mockLogger = new Mock<ILogger<CompraService>>();

        var service = CreateServiceForInference(mockOptions.Object, mockLogger.Object);

        var compra = new Compra
        {
            Proveedor = "COPEC",
            TipoFactura = "GASTO",
            SubcategoriaGasto = null
        };

        // Act
        var result = service.ResolverCategoriaMovimiento(compra);

        // Assert
        result.Should().Be("LOGISTICA");
    }

    [Fact]
    public void ResolverCategoriaMovimiento_NoMatch_ReturnsGastosGeneralesAndLogsWarning()
    {
        // 3.2: No match returns "GASTOS GENERALES" with Log.Warning verification
        // Arrange
        var config = new CategoriaInferenciaConfig
        {
            Keywords = new Dictionary<string, List<string>>
            {
                ["LOGISTICA"] = new List<string> { "bencina", "copec" },
                ["PEAJES"] = new List<string> { "peaje", "tag" }
            }
        };
        var mockOptions = new Mock<IOptionsSnapshot<CategoriaInferenciaConfig>>();
        mockOptions.Setup(o => o.Value).Returns(config);
        var mockLogger = new Mock<ILogger<CompraService>>();

        var service = CreateServiceForInference(mockOptions.Object, mockLogger.Object);

        var compra = new Compra
        {
            Proveedor = "TIENDA XYZ",
            TipoFactura = "GASTO",
            SubcategoriaGasto = null
        };

        // Act
        var result = service.ResolverCategoriaMovimiento(compra);

        // Assert
        result.Should().Be("GASTOS GENERALES");

        // Verify Serilog warning was logged with the unmatched proveedor name
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, t) => state.ToString()!.Contains("TIENDA XYZ")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ResolverCategoriaMovimiento_EmptyConfig_FallsBackToDefaultKeywords()
    {
        // 3.3: Empty config section → fallback to default keywords
        // Arrange
        var config = new CategoriaInferenciaConfig
        {
            Keywords = new Dictionary<string, List<string>>() // empty
        };
        var mockOptions = new Mock<IOptionsSnapshot<CategoriaInferenciaConfig>>();
        mockOptions.Setup(o => o.Value).Returns(config);
        var mockLogger = new Mock<ILogger<CompraService>>();

        var service = CreateServiceForInference(mockOptions.Object, mockLogger.Object);

        var compra = new Compra
        {
            Proveedor = "Copec Station",
            TipoFactura = "GASTO",
            SubcategoriaGasto = null
        };

        // Act
        var result = service.ResolverCategoriaMovimiento(compra);

        // Assert — should fall back to default keywords, which include "copec"
        result.Should().Be("LOGISTICA");
    }

    [Fact]
    public void ResolverCategoriaMovimiento_ConfigReload_PicksUpNewKeywords()
    {
        // 3.4: Config reload picks up new keywords (IOptionsSnapshot)
        // Arrange
        var config = new CategoriaInferenciaConfig
        {
            Keywords = new Dictionary<string, List<string>>
            {
                ["COMISIONES"] = new List<string> { "mercadopago", "transbank" },
                ["LOGISTICA"] = new List<string> { "bencina", "copec" }
            }
        };
        var mockOptions = new Mock<IOptionsSnapshot<CategoriaInferenciaConfig>>();
        mockOptions.Setup(o => o.Value).Returns(config);
        var mockLogger = new Mock<ILogger<CompraService>>();

        var service = CreateServiceForInference(mockOptions.Object, mockLogger.Object);

        var compra = new Compra
        {
            Proveedor = "MERCADOPAGO",
            TipoFactura = "GASTO",
            SubcategoriaGasto = null
        };

        // Act
        var result = service.ResolverCategoriaMovimiento(compra);

        // Assert — new keyword should map to new category
        result.Should().Be("COMISIONES");
    }

    /// <summary>
    /// Creates a CompraService instance with only the dependencies needed for
    /// ResolverCategoriaMovimiento inference tests. Non-inference dependencies
    /// are passed as null/default (not exercised by these tests).
    /// </summary>
    private static CompraService CreateServiceForInference(
        IOptionsSnapshot<CategoriaInferenciaConfig> options,
        ILogger<CompraService> logger)
    {
        // We construct CompraService with all required dependencies.
        // The non-options/logger deps are unused by ResolverCategoriaMovimiento,
        // so we pass minimal safe defaults.
        return new CompraService(
            null!, // ApplicationDbContext — not used by inference
            null!, // IProductMatchingService — not used
            null!, // IUploadPathProvider — not used
            null!, // IProveedorMatchingService — not used
            options,
            logger);
    }

    private static string BuildExceptionChain(Exception ex)
    {
        var parts = new List<string>();
        var current = ex;
        int depth = 0;
        while (current != null && depth < 10)
        {
            parts.Add($"[{depth}] {current.GetType().Name}: {current.Message}");
            current = current.InnerException;
            depth++;
        }
        return string.Join("\n", parts);
    }
}
