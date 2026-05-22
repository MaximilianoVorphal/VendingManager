namespace VendingManager.Tests.Services;

using Microsoft.EntityFrameworkCore;
using Moq;
using FluentAssertions;
using VendingManager.Core.Entities;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.Enums;
using VendingManager.Tests.TestData;

/// <summary>
/// Tests for TemplateRecargaAnalyticsService behavior.
/// Phase 2: Service Split — verifies stockout analysis and sync delegation.
/// </summary>
public class TemplateRecargaAnalyticsServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly TemplateRecargaAnalyticsService _service;

    public TemplateRecargaAnalyticsServiceTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"AnalyticsServiceTestDb_{Guid.NewGuid()}");
        var mockLogger = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<TemplateRecargaAnalyticsService>>();
        _service = new TemplateRecargaAnalyticsService(_context, mockLogger.Object);
    }

    public void Dispose() => _context.Dispose();

    #region AnalyzarPorTemplateAsync tests

    /// <summary>
    /// Returns stockout analysis for a template with snapshot slots.
    /// </summary>
    [Fact]
    public async Task AnalyzarPorTemplateAsync_WithSlots_ReturnsAnalysis()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        _context.Maquinas.Add(maquina);

        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Product 1");
        _context.Productos.Add(producto);

        var template = new TemplateRecarga
        {
            Id = 1,
            Nombre = "Template 1",
            FechaCreacion = new DateTime(2025, 1, 1),
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 1,
                    MaquinaId = 1,
                    FechaRecarga = new DateTime(2025, 1, 1),
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 10, Estado = EstadoSlot.Lleno }
                    }
                }
            }
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.AnalyzarPorTemplateAsync(templateId: 1, umbralHorasSilencio: 24);

        // Assert
        result.Should().NotBeEmpty("the template has a snapshot slot that should be analyzed");
    }

    /// <summary>
    /// Returns empty list when template has no periods.
    /// </summary>
    [Fact]
    public async Task AnalyzarPorTemplateAsync_NoPeriodos_ReturnsEmptyList()
    {
        // Arrange
        var template = new TemplateRecarga
        {
            Id = 2,
            Nombre = "Empty Template",
            FechaCreacion = DateTime.Now
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.AnalyzarPorTemplateAsync(templateId: 2);

        // Assert
        result.Should().BeEmpty();
    }

    /// <summary>
    /// Returns empty list when template does not exist.
    /// </summary>
    [Fact]
    public async Task AnalyzarPorTemplateAsync_NotFound_ReturnsEmptyList()
    {
        // Act
        var result = await _service.AnalyzarPorTemplateAsync(templateId: 9999);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region SyncVentasWithTemplateAsync tests

    /// <summary>
    /// Syncs producto and optionally costs for a template.
    /// </summary>
    [Fact]
    public async Task SyncVentasWithTemplateAsync_WithVentas_SyncsCorrectly()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        _context.Maquinas.Add(maquina);

        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Product 1");
        _context.Productos.Add(producto);

        var template = new TemplateRecarga
        {
            Id = 10,
            Nombre = "Sync Template",
            FechaCreacion = new DateTime(2025, 1, 1),
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 10,
                    MaquinaId = 1,
                    FechaRecarga = new DateTime(2025, 1, 1),
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 10, Estado = EstadoSlot.Lleno }
                    }
                }
            }
        };
        _context.TemplatesRecarga.Add(template);

        var venta = TestDataHelpers.CreateVenta(
            maquinaId: 1,
            productoId: null,
            fechaLocal: new DateTime(2025, 1, 15));
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        // Act
        var count = await _service.SyncVentasWithTemplateAsync(templateId: 10, actualizarCostos: false);

        // Assert
        count.Should().Be(1);
        var updatedVenta = await _context.Ventas.FindAsync(venta.Id);
        updatedVenta!.ProductoId.Should().Be(1);
    }

    /// <summary>
    /// Returns 0 when template has no periods.
    /// </summary>
    [Fact]
    public async Task SyncVentasWithTemplateAsync_NoPeriodos_ReturnsZero()
    {
        // Arrange
        var template = new TemplateRecarga
        {
            Id = 11,
            Nombre = "Empty Template"
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.SyncVentasWithTemplateAsync(templateId: 11, actualizarCostos: false);

        // Assert
        result.Should().Be(0);
    }

    #endregion

    #region SyncAllVentasAsync tests

    /// <summary>
    /// Processes all templates and returns aggregate result.
    /// </summary>
    [Fact]
    public async Task SyncAllVentasAsync_WithTemplates_ReturnsAggregateResult()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        _context.Maquinas.Add(maquina);

        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Product 1");
        _context.Productos.Add(producto);

        var template1 = new TemplateRecarga { Id = 20, Nombre = "Template A" };
        var template2 = new TemplateRecarga { Id = 21, Nombre = "Template B" };
        _context.TemplatesRecarga.AddRange(template1, template2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.SyncAllVentasAsync(actualizarCostos: false);

        // Assert
        result.TemplatesProcesados.Should().Be(2);
        result.Detalles.Should().HaveCount(2);
    }

    #endregion

    #region SyncSlotProductoAsync tests

    /// <summary>
    /// Syncs a specific slot productoId to matching ventas.
    /// </summary>
    [Fact]
    public async Task SyncSlotProductoAsync_WithVentas_SyncsCorrectly()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        _context.Maquinas.Add(maquina);

        var producto1 = TestDataHelpers.CreateProducto(id: 1, nombre: "Old Product");
        var producto2 = TestDataHelpers.CreateProducto(id: 2, nombre: "New Product");
        _context.Productos.AddRange(producto1, producto2);

        var template = new TemplateRecarga
        {
            Id = 30,
            Nombre = "Slot Sync Template",
            FechaCreacion = new DateTime(2025, 1, 1)
        };
        _context.TemplatesRecarga.Add(template);

        var periodo = new PeriodoRecarga
        {
            Id = 30,
            TemplateRecargaId = 30,
            MaquinaId = 1,
            FechaRecarga = new DateTime(2025, 1, 1),
            SnapshotSlots = new List<SnapshotSlot>
            {
                new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 10, Estado = EstadoSlot.Lleno }
            }
        };
        _context.PeriodosRecarga.Add(periodo);

        // Create a venta without explicit ID (let EF generate one)
        var venta = new Venta
        {
            FechaHora = new DateTime(2025, 1, 15),
            FechaLocal = new DateTime(2025, 1, 15),
            MaquinaId = 1,
            ProductoId = null,
            NumeroSlot = "1",
            PrecioVenta = 1000m,
            CostoVenta = 0,
            Pagado = true,
            IdOrdenMaquina = "TEST-001"
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.SyncSlotProductoAsync(templateId: 30, periodoId: 30, numeroSlot: "1", productoId: 2);

        // Assert
        result.ProductoId.Should().Be(2);
        result.MaquinaId.Should().Be(1);
        result.NumeroSlot.Should().Be("1");
    }

    /// <summary>
    /// Throws when periodoId does not match template.
    /// </summary>
    [Fact]
    public async Task SyncSlotProductoAsync_PeriodoNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        var template = new TemplateRecarga
        {
            Id = 31,
            Nombre = "Template Without Periodos"
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.SyncSlotProductoAsync(
            templateId: 31, periodoId: 9999, numeroSlot: "1", productoId: 1);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no encontrado*");
    }

    #endregion
}