namespace VendingManager.Tests.Services;

using Microsoft.EntityFrameworkCore;
using Moq;
using FluentAssertions;
using VendingManager.Core.Entities;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.Enums;
using VendingManager.Tests.TestData;

/// <summary>
/// Tests for TemplateRecargaLifecycleService state transitions.
/// State machine: Pendiente (0) → Terminado (2).
/// Reabrir: Terminado (2) → Pendiente (0), preserves SnapshotSlots.
/// </summary>
public class TemplateRecargaLifecycleServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly TemplateRecargaLifecycleService _service;

    public TemplateRecargaLifecycleServiceTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"LifecycleServiceTestDb_{Guid.NewGuid()}");
        var mockLogger = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<TemplateRecargaLifecycleService>>();
        _service = new TemplateRecargaLifecycleService(_context, mockLogger.Object);
    }

    public void Dispose() => _context.Dispose();

    #region TerminarAsync tests (Pendiente → Terminado)

    /// <summary>
    /// Valid: Pendiente → Terminado transitions correctly.
    /// </summary>
    [Fact]
    public async Task TerminarAsync_Pendiente_SetsTerminado()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        _context.Maquinas.Add(maquina);

        var template = new TemplateRecarga
        {
            Id = 10,
            Nombre = "Template to Terminar",
            Estado = EstadoTemplate.Pendiente,
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 10,
                    MaquinaId = 1,
                    FechaRecarga = DateTime.Now,
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
        var result = await _service.TerminarAsync(template.Id);

        // Assert
        result.Estado.Should().Be(EstadoTemplate.Terminado);
    }

    /// <summary>
    /// Invalid: Terminado → Terminado throws InvalidOperationException.
    /// </summary>
    [Fact]
    public async Task TerminarAsync_Terminado_ThrowsInvalidOperationException()
    {
        // Arrange
        var template = new TemplateRecarga
        {
            Id = 12,
            Nombre = "Already Terminado",
            Estado = EstadoTemplate.Terminado
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.TerminarAsync(template.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Pendiente*");
    }

    /// <summary>
    /// Not found: throws InvalidOperationException with clear message.
    /// </summary>
    [Fact]
    public async Task TerminarAsync_NotFound_ThrowsInvalidOperationException()
    {
        var act = async () => await _service.TerminarAsync(9999);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no encontrado*");
    }

    #endregion

    #region ReabrirAsync tests (Terminado → Pendiente, preserves slots)

    /// <summary>
    /// Valid: Terminado → Pendiente resets state and preserves all SnapshotSlots.
    /// </summary>
    [Fact]
    public async Task ReabrirAsync_Terminado_ResetsToPendiente_PreservesSlots()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        _context.Maquinas.Add(maquina);

        var template = new TemplateRecarga
        {
            Id = 20,
            Nombre = "Reopenable Template",
            Estado = EstadoTemplate.Terminado,
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 20,
                    MaquinaId = 1,
                    FechaRecarga = DateTime.Now,
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 10, Estado = EstadoSlot.Lleno },
                        new() { NumeroSlot = "2", ProductoId = 1, CantidadInicial = 5, Estado = EstadoSlot.Lleno }
                    }
                }
            }
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.ReabrirAsync(template.Id);

        // Assert
        result.Estado.Should().Be(EstadoTemplate.Pendiente);

        // Verify all slots are preserved (not cleared)
        var periodo = result.Periodos.First();
        periodo.SnapshotSlots.Should().HaveCount(2);
        periodo.SnapshotSlots.Should().Contain(s => s.NumeroSlot == "1");
        periodo.SnapshotSlots.Should().Contain(s => s.NumeroSlot == "2");
    }

    /// <summary>
    /// Invalid: Pendiente → Reabrir throws InvalidOperationException (must be Terminado).
    /// </summary>
    [Fact]
    public async Task ReabrirAsync_Pendiente_ThrowsInvalidOperationException()
    {
        // Arrange
        var template = new TemplateRecarga
        {
            Id = 22,
            Nombre = "Pendiente Template",
            Estado = EstadoTemplate.Pendiente
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.ReabrirAsync(template.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Terminado*");
    }

    #endregion

    #region GetLatestTerminadoTemplateSlotsAsync tests

    /// <summary>
    /// Returns SnapshotSlots from the latest Terminado template for the machine.
    /// </summary>
    [Fact]
    public async Task GetLatestTerminadoTemplateSlotsAsync_WithTerminadoTemplate_ReturnsSlots()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        _context.Maquinas.Add(maquina);

        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Product 1");
        _context.Productos.Add(producto);

        var template = new TemplateRecarga
        {
            Id = 40,
            Nombre = "Terminado Template",
            Estado = EstadoTemplate.Terminado,
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 40,
                    MaquinaId = 1,
                    FechaRecarga = DateTime.Now,
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 10, Estado = EstadoSlot.Lleno },
                        new() { NumeroSlot = "2", ProductoId = 1, CantidadInicial = 5, Estado = EstadoSlot.Lleno }
                    }
                }
            }
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetLatestTerminadoTemplateSlotsAsync(maquina.Id);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(s => s.NumeroSlot == "1");
        result.Should().Contain(s => s.NumeroSlot == "2");
    }

    /// <summary>
    /// Returns empty list when no Terminado template exists for the machine.
    /// </summary>
    [Fact]
    public async Task GetLatestTerminadoTemplateSlotsAsync_NoTerminadoTemplate_ReturnsEmpty()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 2, nombre: "Machine 2");
        _context.Maquinas.Add(maquina);

        // Only Pendiente template
        var template = new TemplateRecarga
        {
            Id = 41,
            Nombre = "Pendiente Template",
            Estado = EstadoTemplate.Pendiente,
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 41,
                    MaquinaId = 2,
                    FechaRecarga = DateTime.Now
                }
            }
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetLatestTerminadoTemplateSlotsAsync(maquina.Id);

        // Assert
        result.Should().BeEmpty();
    }

    /// <summary>
    /// Returns only slots from the most recent Terminado template (not all Terminado templates).
    /// </summary>
    [Fact]
    public async Task GetLatestTerminadoTemplateSlotsAsync_MultipleTerminado_ReturnsLatest()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 3, nombre: "Machine 3");
        _context.Maquinas.Add(maquina);

        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Product 1");
        _context.Productos.Add(producto);

        // Older Terminado template
        var olderTemplate = new TemplateRecarga
        {
            Id = 50,
            Nombre = "Old Terminado",
            Estado = EstadoTemplate.Terminado,
            FechaCreacion = DateTime.Now.AddDays(-10),
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 50,
                    MaquinaId = 3,
                    FechaRecarga = DateTime.Now.AddDays(-10),
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 3, Estado = EstadoSlot.Lleno }
                    }
                }
            }
        };
        _context.TemplatesRecarga.Add(olderTemplate);

        // Newer Terminado template
        var newerTemplate = new TemplateRecarga
        {
            Id = 51,
            Nombre = "New Terminado",
            Estado = EstadoTemplate.Terminado,
            FechaCreacion = DateTime.Now,
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 51,
                    MaquinaId = 3,
                    FechaRecarga = DateTime.Now,
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 8, Estado = EstadoSlot.Lleno }
                    }
                }
            }
        };
        _context.TemplatesRecarga.Add(newerTemplate);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetLatestTerminadoTemplateSlotsAsync(maquina.Id);

        // Assert — should return slots from the newer template
        result.Should().HaveCount(1);
        result.First().CantidadInicial.Should().Be(8);
    }

    #endregion

    #region SyncSlotsToConfiguracionAsync tests

    /// <summary>
    /// Syncs SnapshotSlots to ConfiguracionSlots when called directly.
    /// </summary>
    [Fact]
    public async Task SyncSlotsToConfiguracionAsync_WithSlots_SyncsCorrectly()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        _context.Maquinas.Add(maquina);

        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Product 1");
        _context.Productos.Add(producto);

        var template = new TemplateRecarga
        {
            Id = 50,
            Nombre = "Template to Sync",
            Estado = EstadoTemplate.Pendiente,
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 50,
                    MaquinaId = 1,
                    FechaRecarga = DateTime.Now,
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "SLOT-1", ProductoId = 1, CantidadInicial = 8, CapacidadSlot = 10, Estado = EstadoSlot.Lleno }
                    }
                }
            }
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act
        var count = await _service.SyncSlotsToConfiguracionAsync(template.Id);

        // Assert
        count.Should().BeGreaterThan(0);
        var configSlot = await _context.ConfiguracionSlots
            .FirstOrDefaultAsync(c => c.MaquinaId == 1 && c.NumeroSlot == "SLOT-1");
        configSlot.Should().NotBeNull();
        configSlot!.ProductoId.Should().Be(1);
        configSlot.StockActual.Should().Be(8);
    }

    #endregion

    #region ActivarAsync removed tests

    /// <summary>
    /// ActivarAsync no longer exists (intermediate Activo state removed).
    /// </summary>
    [Fact]
    public void ActivarAsync_NoLongerExists()
    {
        _service.GetType().GetMethod("ActivarAsync").Should().BeNull();
    }

    #endregion

    #region GetLatestTerminadoTemplateSlotsAsync present

    /// <summary>
    /// GetLatestTerminadoTemplateSlotsAsync exists and GetLatestActivoTemplateSlotsAsync does not.
    /// </summary>
    [Fact]
    public void GetLatestTerminadoTemplateSlotsAsync_Exists_GetLatestActivoTemplateSlotsAsync_DoesNot()
    {
        _service.GetType().GetMethod("GetLatestTerminadoTemplateSlotsAsync").Should().NotBeNull();
        _service.GetType().GetMethod("GetLatestActivoTemplateSlotsAsync").Should().BeNull();
    }

    #endregion
}