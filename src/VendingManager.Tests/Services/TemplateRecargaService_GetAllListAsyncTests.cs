namespace VendingManager.Tests.Services;

using FluentAssertions;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;
using VendingManager.Tests.TestData;

/// <summary>
/// Tests for TemplateRecargaService.GetAllListAsync — lightweight list projection.
/// Verifies the EF Core .Select() projection returns correct counts and flat DTO.
/// </summary>
public class TemplateRecargaService_GetAllListAsyncTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly TemplateRecargaService _service;

    public TemplateRecargaService_GetAllListAsyncTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"GetAllListTest_{Guid.NewGuid()}");
        var mockLogger = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<TemplateRecargaService>>();
        var mockLifecycle = new Moq.Mock<ITemplateRecargaLifecycleService>();
        var mockAnalytics = new Moq.Mock<ITemplateRecargaAnalyticsService>();
        _service = new TemplateRecargaService(_context, mockLogger.Object, mockLifecycle.Object, mockAnalytics.Object);
    }

    public void Dispose() => _context.Dispose();

    private async Task SeedTemplateWithDataAsync()
    {
        var maquina1 = TestDataHelpers.CreateMaquina(id: 1, nombre: "Máquina A");
        var maquina2 = TestDataHelpers.CreateMaquina(id: 2, nombre: "Máquina B");
        _context.Maquinas.AddRange(maquina1, maquina2);

        var template = new TemplateRecarga
        {
            Id = 1,
            Nombre = "Recarga Test",
            Descripcion = "Descripción test",
            FechaCreacion = new DateTime(2025, 6, 15),
            Estado = EstadoTemplate.Terminado
        };
        _context.TemplatesRecarga.Add(template);

        var periodo1 = new PeriodoRecarga
        {
            Id = 1,
            TemplateRecargaId = 1,
            MaquinaId = 1,
            FechaRecarga = new DateTime(2025, 6, 10),
            SnapshotSlots = new List<SnapshotSlot>
            {
                new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 10, CapacidadSlot = 20, Estado = EstadoSlot.Lleno },
                new() { NumeroSlot = "2", ProductoId = 2, CantidadInicial = 5, CapacidadSlot = 15, Estado = EstadoSlot.Lleno },
                new() { NumeroSlot = "3", ProductoId = null, CantidadInicial = 0, CapacidadSlot = 10, Estado = EstadoSlot.Pendiente }
            }
        };

        var periodo2 = new PeriodoRecarga
        {
            Id = 2,
            TemplateRecargaId = 1,
            MaquinaId = 2,
            FechaRecarga = new DateTime(2025, 6, 12),
            SnapshotSlots = new List<SnapshotSlot>
            {
                new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 8, CapacidadSlot = 20, Estado = EstadoSlot.Lleno },
                new() { NumeroSlot = "2", ProductoId = null, CantidadInicial = 0, CapacidadSlot = 10, Estado = EstadoSlot.Vacio }
            }
        };

        _context.PeriodosRecarga.AddRange(periodo1, periodo2);
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAllListAsync_ReturnsFlatProjectionWithCorrectCounts()
    {
        await SeedTemplateWithDataAsync();

        var result = await _service.GetAllListAsync();

        result.Should().HaveCount(1);
        var item = result[0];
        item.Id.Should().Be(1);
        item.Nombre.Should().Be("Recarga Test");
        item.Descripcion.Should().Be("Descripción test");
        item.Estado.Should().Be(EstadoTemplate.Terminado);
        item.EsActivo.Should().BeTrue("template is Terminado");
        item.FechaCreacion.Should().Be(new DateTime(2025, 6, 15));
        item.MaquinaNombres.Should().BeEquivalentTo(new[] { "Máquina A", "Máquina B" }, "machines ordered by FechaRecarga");
        item.PeriodoCount.Should().Be(2, "template has 2 periods");
        item.TotalProducts.Should().Be(3, "3 slots have ProductoId != null (2 from periodo1 + 1 from periodo2)");
    }

    [Fact]
    public async Task GetAllListAsync_EmptyDatabase_ReturnsEmptyList()
    {
        var result = await _service.GetAllListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllListAsync_PendienteTemplate_EsActivoIsFalse()
    {
        var maquina = TestDataHelpers.CreateMaquina(id: 10);
        _context.Maquinas.Add(maquina);

        var template = new TemplateRecarga
        {
            Id = 99,
            Nombre = "Pendiente Template",
            Estado = EstadoTemplate.Pendiente,
            FechaCreacion = DateTime.Now,
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    MaquinaId = 10,
                    FechaRecarga = DateTime.Now,
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 5, CapacidadSlot = 10, Estado = EstadoSlot.Lleno }
                    }
                }
            }
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        var result = await _service.GetAllListAsync();

        result.Should().HaveCount(1);
        result[0].EsActivo.Should().BeFalse("Pendiente template should not be active");
        result[0].Estado.Should().Be(EstadoTemplate.Pendiente);
        result[0].PeriodoCount.Should().Be(1);
        result[0].TotalProducts.Should().Be(1);
    }

    [Fact]
    public async Task GetAllListAsync_MultipleTemplates_ReturnsOrderedByFechaCreacionDescending()
    {
        var maquina = TestDataHelpers.CreateMaquina(id: 5);
        _context.Maquinas.Add(maquina);

        _context.TemplatesRecarga.AddRange(
            new TemplateRecarga
            {
                Id = 10,
                Nombre = "Older Template",
                Estado = EstadoTemplate.Terminado,
                FechaCreacion = new DateTime(2025, 1, 1),
                Periodos = new List<PeriodoRecarga>
                {
                    new() { MaquinaId = 5, FechaRecarga = new DateTime(2025, 1, 1) }
                }
            },
            new TemplateRecarga
            {
                Id = 20,
                Nombre = "Newer Template",
                Estado = EstadoTemplate.Terminado,
                FechaCreacion = new DateTime(2025, 6, 1),
                Periodos = new List<PeriodoRecarga>
                {
                    new() { MaquinaId = 5, FechaRecarga = new DateTime(2025, 6, 1) }
                }
            }
        );
        await _context.SaveChangesAsync();

        var result = await _service.GetAllListAsync();

        result.Should().HaveCount(2);
        result[0].Nombre.Should().Be("Newer Template", "ordered by FechaCreacion descending");
        result[1].Nombre.Should().Be("Older Template");
    }

    [Fact]
    public async Task GetAllListAsync_TemplateWithNoSlots_TotalProductsIsZero()
    {
        var maquina = TestDataHelpers.CreateMaquina(id: 7);
        _context.Maquinas.Add(maquina);

        _context.TemplatesRecarga.Add(new TemplateRecarga
        {
            Id = 77,
            Nombre = "Empty Slots Template",
            Estado = EstadoTemplate.Terminado,
            FechaCreacion = DateTime.Now,
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    MaquinaId = 7,
                    FechaRecarga = DateTime.Now,
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "1", ProductoId = null, CantidadInicial = 0, CapacidadSlot = 10, Estado = EstadoSlot.Pendiente },
                        new() { NumeroSlot = "2", ProductoId = null, CantidadInicial = 0, CapacidadSlot = 10, Estado = EstadoSlot.Vacio }
                    }
                }
            }
        });
        await _context.SaveChangesAsync();

        var result = await _service.GetAllListAsync();

        result.Should().HaveCount(1);
        result[0].TotalProducts.Should().Be(0, "no slots have ProductoId != null");
        result[0].PeriodoCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAllListAsync_NoNestedPeriodosOrSlotsInProjection()
    {
        await SeedTemplateWithDataAsync();

        var result = await _service.GetAllListAsync();
        var item = result[0];

        // Verify the DTO type doesn't have Periodos or SnapshotSlots
        var props = typeof(TemplateRecargaListItemDto).GetProperties();
        props.Should().NotContain(p => p.Name == "Periodos");
        props.Should().NotContain(p => p.Name == "SnapshotSlots");

        // Verify the property values are scalar (not collections)
        item.GetType().GetProperty("PeriodoCount")!.PropertyType.Should().Be(typeof(int));
        item.GetType().GetProperty("TotalProducts")!.PropertyType.Should().Be(typeof(int));
    }
}
