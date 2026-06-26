namespace VendingManager.Tests.Services;

using Microsoft.AspNetCore.Hosting;
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

        var uploadProvider = new DefaultUploadPathProvider(_mockEnv.Object, _config);
        _service = new CompraService(_context, _mockProductMatching.Object, uploadProvider, _mockProveedorMatching.Object);
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
    public async Task GetComprasAsync_WithProveedorCatalog_IncludesNavigationProperty()
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
        fetched.ProveedorCatalog.Should().NotBeNull();
        fetched.ProveedorCatalog!.NombreCanonical.Should().Be("Supplier Nav");
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
