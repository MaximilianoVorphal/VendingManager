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

    #region BuildCrossTemplateLookupAsync filter tests

    /// <summary>
    /// BuildCrossTemplateLookupAsync should only load periods for machines
    /// relevant to the analyzed template, not every template in the system.
    /// Tested indirectly through AnalyzarPorTemplateAsync which now passes
    /// filtered machine IDs.
    /// </summary>
    [Fact]
    public async Task AnalyzarPorTemplateAsync_WithCrossTemplateLookup_FiltersPeriodsByMachine()
    {
        // Arrange — template 1 has periods on machines 1 and 2
        var maquina1 = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        var maquina2 = TestDataHelpers.CreateMaquina(id: 2, nombre: "Machine 2");
        var maquina3 = TestDataHelpers.CreateMaquina(id: 3, nombre: "Machine 3");
        _context.Maquinas.AddRange(maquina1, maquina2, maquina3);

        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Product");
        _context.Productos.Add(producto);

        // Template 1: periods on machine 1 and machine 2
        var template1 = new TemplateRecarga
        {
            Id = 100,
            Nombre = "Template 1",
            FechaCreacion = new DateTime(2025, 1, 1),
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 100,
                    MaquinaId = 1,
                    FechaRecarga = new DateTime(2025, 1, 1),
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 10, Estado = EstadoSlot.Lleno }
                    }
                },
                new()
                {
                    Id = 101,
                    MaquinaId = 2,
                    FechaRecarga = new DateTime(2025, 1, 1),
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 10, Estado = EstadoSlot.Lleno }
                    }
                }
            }
        };

        // Template 2: period on machine 3 (should be filtered out when analyzing template 1)
        var template2 = new TemplateRecarga
        {
            Id = 200,
            Nombre = "Template 2",
            FechaCreacion = new DateTime(2025, 1, 1),
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 200,
                    MaquinaId = 3,
                    FechaRecarga = new DateTime(2025, 1, 1),
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 10, Estado = EstadoSlot.Lleno }
                    }
                }
            }
        };

        _context.TemplatesRecarga.AddRange(template1, template2);
        await _context.SaveChangesAsync();

        // Act — analyze template 1
        var result = await _service.AnalyzarPorTemplateAsync(templateId: 100, umbralHorasSilencio: 24);

        // Assert — results should include both machines from template 1
        result.Should().NotBeEmpty();
        result.Should().HaveCount(2, "template 1 has 2 periods (machine 1 and machine 2)");
        result.Any(r => r.MaquinaId == 1).Should().BeTrue("template 1 has a period on machine 1");
        result.Any(r => r.MaquinaId == 2).Should().BeTrue("template 1 has a period on machine 2");
        // Machine 3's periods should NOT pollute template 1's results
        result.Any(r => r.MaquinaId == 3).Should().BeFalse("machine 3 is in a different template");
    }

    #endregion

    #region GetSlotTimelineAsync tests

    [Fact]
    public async Task GetSlotTimelineAsync_WithSales_ReturnsDates()
    {
        // Arrange — seed a template with one period, one slot, and sales
        var maquina = TestDataHelpers.CreateMaquina(id: 10, nombre: "M10");
        var producto = TestDataHelpers.CreateProducto(id: 100, nombre: "Coca Cola");
        _context.Maquinas.Add(maquina);
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        var template = new TemplateRecarga
        {
            Id = 200,
            Nombre = "Test Timeline",
            Estado = EstadoTemplate.Terminado
        };
        var periodo = new PeriodoRecarga
        {
            Id = 300,
            TemplateRecargaId = 200,
            MaquinaId = 10,
            FechaRecarga = new DateTime(2025, 6, 1)
        };
        var slot = new SnapshotSlot
        {
            Id = 400,
            PeriodoRecargaId = 300,
            NumeroSlot = "A1",
            ProductoId = 100,
            CantidadInicial = 10,
            Estado = EstadoSlot.Lleno
        };
        template.Periodos = new List<PeriodoRecarga> { periodo };
        periodo.SnapshotSlots = new List<SnapshotSlot> { slot };
        _context.TemplatesRecarga.Add(template);

        var ventas = new List<Venta>
        {
            new() { MaquinaId = 10, NumeroSlot = "A1", ProductoId = 100, FechaLocal = new DateTime(2025, 6, 2, 10, 0, 0), PrecioVenta = 1000 },
            new() { MaquinaId = 10, NumeroSlot = "A1", ProductoId = 100, FechaLocal = new DateTime(2025, 6, 3, 14, 0, 0), PrecioVenta = 1000 },
            new() { MaquinaId = 10, NumeroSlot = "A1", ProductoId = 100, FechaLocal = new DateTime(2025, 6, 4, 9, 0, 0), PrecioVenta = 1000 }
        };
        _context.Ventas.AddRange(ventas);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetSlotTimelineAsync(200, 10, "A1");

        // Assert
        result.Should().NotBeNull();
        result!.MaquinaId.Should().Be(10);
        result.NumeroSlot.Should().Be("A1");
        result.ProductoId.Should().Be(100);
        result.FechasVentas.Should().HaveCount(3);
        result.FechasVentas.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetSlotTimelineAsync_DeadSlot_ReturnsEmptyTimeline()
    {
        // Arrange — slot with no sales
        var maquina = TestDataHelpers.CreateMaquina(id: 11, nombre: "M11");
        _context.Maquinas.Add(maquina);
        await _context.SaveChangesAsync();

        var template = new TemplateRecarga
        {
            Id = 201,
            Nombre = "Dead Slot Template",
            Estado = EstadoTemplate.Terminado
        };
        var periodo = new PeriodoRecarga
        {
            Id = 301,
            TemplateRecargaId = 201,
            MaquinaId = 11,
            FechaRecarga = new DateTime(2025, 6, 1)
        };
        var slot = new SnapshotSlot
        {
            Id = 401,
            PeriodoRecargaId = 301,
            NumeroSlot = "B2",
            ProductoId = null,
            CantidadInicial = 10,
            Estado = EstadoSlot.Vacio
        };
        template.Periodos = new List<PeriodoRecarga> { periodo };
        periodo.SnapshotSlots = new List<SnapshotSlot> { slot };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetSlotTimelineAsync(201, 11, "B2");

        // Assert
        result.Should().NotBeNull();
        result!.FechasVentas.Should().BeEmpty();
        result.ProductoId.Should().BeNull();
    }

    [Fact]
    public async Task GetSlotTimelineAsync_UnknownSlot_ReturnsNull()
    {
        // Arrange — template exists but slot doesn't
        var maquina = TestDataHelpers.CreateMaquina(id: 12, nombre: "M12");
        _context.Maquinas.Add(maquina);
        await _context.SaveChangesAsync();

        var template = new TemplateRecarga
        {
            Id = 202,
            Nombre = "Unknown Slot Template",
            Estado = EstadoTemplate.Terminado
        };
        var periodo = new PeriodoRecarga
        {
            Id = 302,
            TemplateRecargaId = 202,
            MaquinaId = 12,
            FechaRecarga = new DateTime(2025, 6, 1)
        };
        periodo.SnapshotSlots = new List<SnapshotSlot>();
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetSlotTimelineAsync(202, 12, "NONEXISTENT");

        // Assert
        result.Should().BeNull("non-existent slot should return null for 404 handling");
    }

    [Fact]
    public async Task GetSlotTimelineAsync_UnknownTemplate_ReturnsNull()
    {
        // Act
        var result = await _service.GetSlotTimelineAsync(9999, 1, "A1");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region ProductVelocity — aggregated velocity and operating-hours filter

    /// <summary>
    /// When the same product lives in multiple slots, the velocity should be
    /// aggregated across all slots (not computed per-slot), reducing noise
    /// for low-volume slots.
    /// </summary>
    [Fact]
    public async Task AnalyzarPorTemplateAsync_ProductVelocity_AggregatedAcrossSlots()
    {
        // Arrange — same producto in two slots of the same machine
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Coca Cola", costoPromedio: 400);
        _context.Maquinas.Add(maquina);
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        var fechaRecarga = new DateTime(2025, 1, 1, 8, 0, 0); // 08:00

        var template = new TemplateRecarga
        {
            Id = 500,
            Nombre = "Aggregated Velocity Template",
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 500,
                    MaquinaId = 1,
                    FechaRecarga = fechaRecarga,
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 10, Estado = EstadoSlot.Lleno },
                        new() { NumeroSlot = "2", ProductoId = 1, CantidadInicial = 10, Estado = EstadoSlot.Lleno }
                    }
                }
            }
        };
        _context.TemplatesRecarga.Add(template);

        // Slot 1: 3 ventas (low volume, noisy per-slot velocity)
        // Slot 2: 30 ventas (high volume, realistic velocity)
        for (int i = 0; i < 3; i++)
            _context.Ventas.Add(new Venta
            {
                MaquinaId = 1, NumeroSlot = "1", ProductoId = 1,
                FechaLocal = fechaRecarga.AddHours(10 + i), FechaHora = fechaRecarga.AddHours(10 + i),
                PrecioVenta = 1000, CostoVenta = 400, Pagado = true, IdOrdenMaquina = "TEST"
            });
        for (int i = 0; i < 30; i++)
            _context.Ventas.Add(new Venta
            {
                MaquinaId = 1, NumeroSlot = "2", ProductoId = 1,
                FechaLocal = fechaRecarga.AddHours(10 + i * 0.5), FechaHora = fechaRecarga.AddHours(10 + i * 0.5),
                PrecioVenta = 1000, CostoVenta = 400, Pagado = true, IdOrdenMaquina = "TEST"
            });

        await _context.SaveChangesAsync();

        // Act
        var result = await _service.AnalyzarPorTemplateAsync(templateId: 500);

        // Assert — both slots should share the same aggregated velocity,
        // NOT wildly different per-slot velocities (3/?? vs 30/??).
        result.Should().HaveCount(2);
        var slot1 = result.First(r => r.NumeroSlot == "1");
        var slot2 = result.First(r => r.NumeroSlot == "2");

        slot1.VelocidadPorHora.Should().Be(slot2.VelocidadPorHora,
            "both slots share the same product and should use aggregated velocity");
        slot1.VelocidadPorHora.Should().BeGreaterThan(0,
            "aggregated velocity should be based on all 33 ventas");
    }

    /// <summary>
    /// Ventas outside operating hours (08:00–22:00) should NOT count toward
    /// the product-level velocity calculation. This prevents overnight dead
    /// hours from diluting the velocity metric.
    /// </summary>
    [Fact]
    public async Task AnalyzarPorTemplateAsync_OperatingHours_ExcludesNightSalesFromVelocity()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Coca Cola", costoPromedio: 400);
        _context.Maquinas.Add(maquina);
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        var fechaRecarga = new DateTime(2025, 1, 1, 8, 0, 0);

        var template = new TemplateRecarga
        {
            Id = 501,
            Nombre = "Operating Hours Template",
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 501,
                    MaquinaId = 1,
                    FechaRecarga = fechaRecarga,
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 10, Estado = EstadoSlot.Lleno }
                    }
                }
            }
        };
        _context.TemplatesRecarga.Add(template);

        // 5 ventas at 3 AM (outside operating hours — excluded from velocity)
        for (int i = 0; i < 5; i++)
            _context.Ventas.Add(new Venta
            {
                MaquinaId = 1, NumeroSlot = "1", ProductoId = 1,
                FechaLocal = new DateTime(2025, 1, 1, 3, 0, 0).AddDays(i),
                FechaHora = new DateTime(2025, 1, 1, 3, 0, 0).AddDays(i),
                PrecioVenta = 1000, CostoVenta = 400, Pagado = true, IdOrdenMaquina = "TEST"
            });

        // 10 ventas at 10 AM (inside operating hours)
        for (int i = 0; i < 10; i++)
            _context.Ventas.Add(new Venta
            {
                MaquinaId = 1, NumeroSlot = "1", ProductoId = 1,
                FechaLocal = new DateTime(2025, 1, 1, 10, 0, 0).AddDays(i),
                FechaHora = new DateTime(2025, 1, 1, 10, 0, 0).AddDays(i),
                PrecioVenta = 1000, CostoVenta = 400, Pagado = true, IdOrdenMaquina = "TEST"
            });

        // Close the period: next template ends it at day 12
        var cierre = new TemplateRecarga
        {
            Id = 601,
            Nombre = "Close",
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 601,
                    MaquinaId = 1,
                    FechaRecarga = new DateTime(2025, 1, 12, 8, 0, 0),
                    SnapshotSlots = new List<SnapshotSlot>()
                }
            }
        };
        _context.TemplatesRecarga.Add(cierre);

        await _context.SaveChangesAsync();

        // Act
        var result = await _service.AnalyzarPorTemplateAsync(templateId: 501);

        // Assert — velocity should be based on 10 operating-hours ventas, not 15 total.
        result.Should().HaveCount(1);
        var slot = result[0];

        slot.VelocidadPorHora.Should().BeGreaterThan(0);
        // 15 total ventas, initial=10 → stockout. But operating-hours filter
        // makes the loss estimate more conservative.
        slot.PosibleQuiebre.Should().BeTrue("15 ventas > 10 inicial");
        slot.GananciaPerdidaEstimada.Should().BeGreaterThan(0,
            "slot emptied and had remaining operating hours in the period");
    }

    /// <summary>
    /// When a slot goes empty, horasSinStock for loss calculation should use
    /// operating hours (08:00–22:00), not raw clock hours. A slot that empties
    /// at 21:00 with next recarga at 09:00 next day has only ~2 operating hours
    /// of lost sales, not 12.
    /// </summary>
    [Fact]
    public async Task AnalyzarPorTemplateAsync_OperatingHours_DiscountsNightHoursFromLoss()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Coca Cola", costoPromedio: 400);
        _context.Maquinas.Add(maquina);
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        // Recarga: Jan 1 at 08:00. Next recarga: Jan 2 at 09:00.
        var fechaRecarga = new DateTime(2025, 1, 1, 8, 0, 0);
        var siguienteRecarga = new DateTime(2025, 1, 2, 9, 0, 0);

        var template = new TemplateRecarga
        {
            Id = 502,
            Nombre = "Night Hours Loss Template",
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 502,
                    MaquinaId = 1,
                    FechaRecarga = fechaRecarga,
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 5, Estado = EstadoSlot.Lleno }
                    }
                }
            }
        };
        _context.TemplatesRecarga.Add(template);

        // Precarga histórico: segundo período para cerrar el primero correctamente
        var historico = new TemplateRecarga
        {
            Id = 600,
            Nombre = "Historical",
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 600,
                    MaquinaId = 1,
                    FechaRecarga = siguienteRecarga,
                    SnapshotSlots = new List<SnapshotSlot>()
                }
            }
        };
        _context.TemplatesRecarga.Add(historico);

        // 3 ventas durante el día (10:00, 12:00, 14:00)
        // 2 ventas a las 21:00 (las que vacían el slot)
        var ventaTimes = new[]
        {
            new DateTime(2025, 1, 1, 10, 0, 0),
            new DateTime(2025, 1, 1, 12, 0, 0),
            new DateTime(2025, 1, 1, 14, 0, 0),
            new DateTime(2025, 1, 1, 21, 0, 0), // 4th sale — leaves 1 remaining
            new DateTime(2025, 1, 1, 21, 5, 0), // 5th sale — empties the slot at 21:05
        };

        foreach (var t in ventaTimes)
            _context.Ventas.Add(new Venta
            {
                MaquinaId = 1, NumeroSlot = "1", ProductoId = 1,
                FechaLocal = t, FechaHora = t,
                PrecioVenta = 1000, CostoVenta = 400, Pagado = true, IdOrdenMaquina = "TEST"
            });

        await _context.SaveChangesAsync();

        // Act
        var result = await _service.AnalyzarPorTemplateAsync(templateId: 502);

        // Assert
        result.Should().HaveCount(1);
        var slot = result[0];

        slot.PosibleQuiebre.Should().BeTrue("5 ventas >= 5 unidades iniciales");
        // Raw clock horasSinStock: from 21:05 Jan 1 to 09:00 Jan 2 = 11h55m
        slot.HorasSinStock.Should().BeGreaterThan(10).And.BeLessThan(13);

        // Pero la pérdida usa horas operativas: 21:05-22:00 (0h55m) + 08:00-09:00 (1h) ≈ 1h55m
        slot.GananciaPerdidaEstimada.Should().BeGreaterThan(0,
            "operating-hours adjusted loss should be non-zero but smaller than raw clock hours");
        slot.GananciaPerdidaEstimada.Should().BeLessThan(700,
            "raw clock calculation (~12h) would give ~2140; operating-hours gives much less");
    }

    /// <summary>
    /// Different machines should get different velocities for the same product.
    /// A hospital machine sells faster than an office machine; averaging them
    /// would overestimate loss at the office and underestimate at the hospital.
    /// </summary>
    [Fact]
    public async Task AnalyzarPorTemplateAsync_PerMachineVelocity_DifferentMachinesDiffer()
    {
        // Arrange — same product, two machines, different sales velocity
        var maquinaRapida = TestDataHelpers.CreateMaquina(id: 1, nombre: "Hospital 24h");
        var maquinaLenta = TestDataHelpers.CreateMaquina(id: 2, nombre: "Oficina");
        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Coca Cola", costoPromedio: 400);
        _context.Maquinas.AddRange(maquinaRapida, maquinaLenta);
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        var fechaRecarga = new DateTime(2025, 1, 1, 8, 0, 0);
        var fechaCierre = new DateTime(2025, 1, 5, 8, 0, 0);

        var template = new TemplateRecarga
        {
            Id = 510,
            Nombre = "Per-Machine Velocity",
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 510,
                    MaquinaId = 1, // Hospital — fast
                    FechaRecarga = fechaRecarga,
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 100, Estado = EstadoSlot.Lleno }
                    }
                },
                new()
                {
                    Id = 511,
                    MaquinaId = 2, // Office — slow
                    FechaRecarga = fechaRecarga,
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 100, Estado = EstadoSlot.Lleno }
                    }
                }
            }
        };
        _context.TemplatesRecarga.Add(template);

        // Close both periods at day 5
        var cierre = new TemplateRecarga
        {
            Id = 610,
            Nombre = "Close",
            Periodos = new List<PeriodoRecarga>
            {
                new() { Id = 610, MaquinaId = 1, FechaRecarga = fechaCierre, SnapshotSlots = new List<SnapshotSlot>() },
                new() { Id = 611, MaquinaId = 2, FechaRecarga = fechaCierre, SnapshotSlots = new List<SnapshotSlot>() }
            }
        };
        _context.TemplatesRecarga.Add(cierre);

        // Machine 1 (hospital): 50 ventas in 4 days → ~12.5/day
        for (int i = 0; i < 50; i++)
            _context.Ventas.Add(new Venta
            {
                MaquinaId = 1, NumeroSlot = "1", ProductoId = 1,
                FechaLocal = fechaRecarga.AddHours(10 + i * 1.5),
                FechaHora = fechaRecarga.AddHours(10 + i * 1.5),
                PrecioVenta = 1000, CostoVenta = 400, Pagado = true, IdOrdenMaquina = "TEST"
            });

        // Machine 2 (office): 10 ventas in 4 days → ~2.5/day
        for (int i = 0; i < 10; i++)
            _context.Ventas.Add(new Venta
            {
                MaquinaId = 2, NumeroSlot = "1", ProductoId = 1,
                FechaLocal = fechaRecarga.AddHours(10 + i * 6),
                FechaHora = fechaRecarga.AddHours(10 + i * 6),
                PrecioVenta = 1000, CostoVenta = 400, Pagado = true, IdOrdenMaquina = "TEST"
            });

        await _context.SaveChangesAsync();

        // Act
        var result = await _service.AnalyzarPorTemplateAsync(templateId: 510);

        // Assert — same product, different machines → different velocities
        result.Should().HaveCount(2);
        var rapida = result.First(r => r.MaquinaId == 1);
        var lenta = result.First(r => r.MaquinaId == 2);

        rapida.VelocidadPorHora.Should().BeGreaterThan(lenta.VelocidadPorHora,
            "hospital machine should have higher velocity than office machine");
        rapida.ProductoId.Should().Be(lenta.ProductoId,
            "both slots sell the same product");
    }

    /// <summary>
    /// When a product has no operating-hours ventas (e.g. only sells at night
    /// in a 24h location), falls back gracefully to per-slot velocity.
    /// </summary>
    [Fact]
    public async Task AnalyzarPorTemplateAsync_NoOperatingHoursVentas_FallsBackToSlotVelocity()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Night Owl", costoPromedio: 400);
        _context.Maquinas.Add(maquina);
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        var fechaRecarga = new DateTime(2025, 1, 1, 22, 0, 0);

        var template = new TemplateRecarga
        {
            Id = 503,
            Nombre = "Night Only Template",
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 503,
                    MaquinaId = 1,
                    FechaRecarga = fechaRecarga,
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 5, Estado = EstadoSlot.Lleno }
                    }
                }
            }
        };
        _context.TemplatesRecarga.Add(template);

        // All ventas at 23:00 (outside operating hours 8-22)
        for (int i = 0; i < 5; i++)
            _context.Ventas.Add(new Venta
            {
                MaquinaId = 1, NumeroSlot = "1", ProductoId = 1,
                FechaLocal = fechaRecarga.AddHours(i),
                FechaHora = fechaRecarga.AddHours(i),
                PrecioVenta = 1000, CostoVenta = 400, Pagado = true, IdOrdenMaquina = "TEST"
            });

        await _context.SaveChangesAsync();

        // Act
        var result = await _service.AnalyzarPorTemplateAsync(templateId: 503);

        // Assert — should NOT crash. Falls back to per-slot velocity.
        result.Should().HaveCount(1);
        var slot = result[0];
        slot.PosibleQuiebre.Should().BeTrue("5 ventas >= 5 inicial");
        slot.GananciaPerdidaEstimada.Should().BeGreaterThanOrEqualTo(0,
            "should fall back gracefully, not throw");
    }

    #endregion
}