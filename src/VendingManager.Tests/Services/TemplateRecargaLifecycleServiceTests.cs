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
/// Phase 2: Service Split — verifies valid/invalid transitions and sync behavior.
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

    #region StartLoadingAsync tests

    /// <summary>
    /// Valid: Borrador → EnCarga transitions correctly set FechaCargaInicio.
    /// </summary>
    [Fact]
    public async Task StartLoadingAsync_Borrador_SetsFechaCargaInicioYEnCarga()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        _context.Maquinas.Add(maquina);

        var template = new TemplateRecarga
        {
            Id = 1,
            Nombre = "Test Template",
            Estado = EstadoTemplate.Borrador,
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 1,
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
        var result = await _service.StartLoadingAsync(template.Id);

        // Assert
        result.Estado.Should().Be(EstadoTemplate.EnCarga);
        result.FechaCargaInicio.Should().NotBeNull();
    }

    /// <summary>
    /// Invalid: EnCarga → EnCarga throws InvalidOperationException.
    /// </summary>
    [Fact]
    public async Task StartLoadingAsync_EnCarga_ThrowsInvalidOperationException()
    {
        // Arrange
        var template = new TemplateRecarga
        {
            Id = 2,
            Nombre = "Already Loading",
            Estado = EstadoTemplate.EnCarga,
            FechaCargaInicio = DateTime.Now.AddHours(-1)
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.StartLoadingAsync(template.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*EnCarga*");
    }

    /// <summary>
    /// Invalid: Activo → EnCarga throws InvalidOperationException.
    /// </summary>
    [Fact]
    public async Task StartLoadingAsync_Activo_ThrowsInvalidOperationException()
    {
        // Arrange
        var template = new TemplateRecarga
        {
            Id = 3,
            Nombre = "Already Active",
            Estado = EstadoTemplate.Activo
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.StartLoadingAsync(template.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Activo*");
    }

    /// <summary>
    /// Invalid: Cerrado → EnCarga throws InvalidOperationException.
    /// </summary>
    [Fact]
    public async Task StartLoadingAsync_Cerrado_ThrowsInvalidOperationException()
    {
        // Arrange
        var template = new TemplateRecarga
        {
            Id = 4,
            Nombre = "Closed Template",
            Estado = EstadoTemplate.Cerrado
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.StartLoadingAsync(template.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cerrado*");
    }

    /// <summary>
    /// Not found: throws InvalidOperationException with clear message.
    /// </summary>
    [Fact]
    public async Task StartLoadingAsync_NotFound_ThrowsInvalidOperationException()
    {
        var act = async () => await _service.StartLoadingAsync(9999);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no encontrado*");
    }

    #endregion

    #region FinalizeAsync tests

    /// <summary>
    /// Valid: EnCarga → Activo transitions correctly set FechaCargaFin.
    /// </summary>
    [Fact]
    public async Task FinalizeAsync_EnCarga_SetsFechaCargaFinYActivo()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        _context.Maquinas.Add(maquina);

        var template = new TemplateRecarga
        {
            Id = 10,
            Nombre = "Finalizable Template",
            Estado = EstadoTemplate.EnCarga,
            FechaCargaInicio = DateTime.Now.AddHours(-2),
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
        var result = await _service.FinalizeAsync(template.Id);

        // Assert
        result.Estado.Should().Be(EstadoTemplate.Activo);
        result.FechaCargaFin.Should().NotBeNull();
    }

    /// <summary>
    /// Invalid: Borrador → Activo throws InvalidOperationException.
    /// </summary>
    [Fact]
    public async Task FinalizeAsync_Borrador_ThrowsInvalidOperationException()
    {
        // Arrange
        var template = new TemplateRecarga
        {
            Id = 11,
            Nombre = "Not Started",
            Estado = EstadoTemplate.Borrador
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.FinalizeAsync(template.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Borrador*");
    }

    /// <summary>
    /// Invalid: Activo → Activo throws InvalidOperationException.
    /// </summary>
    [Fact]
    public async Task FinalizeAsync_Activo_ThrowsInvalidOperationException()
    {
        // Arrange
        var template = new TemplateRecarga
        {
            Id = 12,
            Nombre = "Already Active",
            Estado = EstadoTemplate.Activo
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.FinalizeAsync(template.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Activo*");
    }

    #endregion

    #region CloseAsync tests

    /// <summary>
    /// Valid: Activo → Cerrado transitions correctly.
    /// </summary>
    [Fact]
    public async Task CloseAsync_Activo_SetsCerrado()
    {
        // Arrange
        var template = new TemplateRecarga
        {
            Id = 20,
            Nombre = "Closable Template",
            Estado = EstadoTemplate.Activo
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.CloseAsync(template.Id);

        // Assert
        result.Estado.Should().Be(EstadoTemplate.Cerrado);
    }

    /// <summary>
    /// Invalid: Borrador → Cerrado throws InvalidOperationException.
    /// </summary>
    [Fact]
    public async Task CloseAsync_Borrador_ThrowsInvalidOperationException()
    {
        // Arrange
        var template = new TemplateRecarga
        {
            Id = 21,
            Nombre = "Draft Template",
            Estado = EstadoTemplate.Borrador
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.CloseAsync(template.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Borrador*");
    }

    /// <summary>
    /// Invalid: EnCarga → Cerrado throws InvalidOperationException.
    /// </summary>
    [Fact]
    public async Task CloseAsync_EnCarga_ThrowsInvalidOperationException()
    {
        // Arrange
        var template = new TemplateRecarga
        {
            Id = 22,
            Nombre = "Loading Template",
            Estado = EstadoTemplate.EnCarga,
            FechaCargaInicio = DateTime.Now
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.CloseAsync(template.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*EnCarga*");
    }

    #endregion

    #region ResetToDraftAsync tests

    /// <summary>
    /// Valid: Activo → Borrador resets state and clears dates.
    /// </summary>
    [Fact]
    public async Task ResetToDraftAsync_Activo_ResetsToBorrador()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        _context.Maquinas.Add(maquina);

        var template = new TemplateRecarga
        {
            Id = 30,
            Nombre = "Resetable Template",
            Estado = EstadoTemplate.Activo,
            FechaCargaInicio = DateTime.Now.AddHours(-5),
            FechaCargaFin = DateTime.Now.AddHours(-1),
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 30,
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
        var result = await _service.ResetToDraftAsync(template.Id);

        // Assert
        result.Estado.Should().Be(EstadoTemplate.Borrador);
        result.FechaCargaInicio.Should().BeNull();
        result.FechaCargaFin.Should().BeNull();
    }

    /// <summary>
    /// Valid: Cerrado → Borrador resets state.
    /// </summary>
    [Fact]
    public async Task ResetToDraftAsync_Cerrado_ResetsToBorrador()
    {
        // Arrange
        var template = new TemplateRecarga
        {
            Id = 31,
            Nombre = "Closed Template",
            Estado = EstadoTemplate.Cerrado,
            FechaCargaInicio = DateTime.Now.AddHours(-5),
            FechaCargaFin = DateTime.Now.AddHours(-1)
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.ResetToDraftAsync(template.Id);

        // Assert
        result.Estado.Should().Be(EstadoTemplate.Borrador);
    }

    #endregion

    #region GetActiveTemplateSlotsAsync tests

    /// <summary>
    /// Returns SnapshotSlots from the latest Activo template for the machine.
    /// </summary>
    [Fact]
    public async Task GetActiveTemplateSlotsAsync_WithActiveTemplate_ReturnsSlots()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        _context.Maquinas.Add(maquina);

        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Product 1");
        _context.Productos.Add(producto);

        var template = new TemplateRecarga
        {
            Id = 40,
            Nombre = "Active Template",
            Estado = EstadoTemplate.Activo,
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
        var result = await _service.GetActiveTemplateSlotsAsync(maquina.Id);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(s => s.NumeroSlot == "1");
        result.Should().Contain(s => s.NumeroSlot == "2");
    }

    /// <summary>
    /// Returns empty list when no Activo template exists for the machine.
    /// </summary>
    [Fact]
    public async Task GetActiveTemplateSlotsAsync_NoActiveTemplate_ReturnsEmpty()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 2, nombre: "Machine 2");
        _context.Maquinas.Add(maquina);

        // Only Borrador template
        var template = new TemplateRecarga
        {
            Id = 41,
            Nombre = "Draft Template",
            Estado = EstadoTemplate.Borrador,
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
        var result = await _service.GetActiveTemplateSlotsAsync(maquina.Id);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region DI-6: FinalizeAsync with no slots — skip sync + warning

    /// <summary>
    /// DI-6 Edge Case: Activo template with zero SnapshotSlots skips ConfiguracionSlots
    /// sync and logs a warning instead of crashing.
    /// </summary>
    [Fact]
    public async Task FinalizeAsync_NoSlotsConfigured_SkipsSyncWithWarning()
    {
        // Arrange: maquina + producto + template in EnCarga with EMPTY SnapshotSlots list
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine DI-6");
        _context.Maquinas.Add(maquina);

        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Product 1");
        _context.Productos.Add(producto);

        var template = new TemplateRecarga
        {
            Id = 60,
            Nombre = "Template No Slots",
            Estado = EstadoTemplate.EnCarga,
            FechaCargaInicio = DateTime.Now,
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 60,
                    MaquinaId = 1,
                    FechaRecarga = DateTime.Now,
                    SnapshotSlots = new List<SnapshotSlot>()
                    // Zero slots intentionally
                }
            }
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act: finalize should succeed even with no slots
        var result = await _service.FinalizeAsync(template.Id);

        // Assert: transitioned to Activo
        result.Estado.Should().Be(EstadoTemplate.Activo);
        result.FechaCargaFin.Should().NotBeNull();

        // DI-6: no ConfiguracionSlots records should have been created for this maquina
        var configSlots = await _context.ConfiguracionSlots
            .Where(c => c.MaquinaId == 1)
            .ToListAsync();
        configSlots.Should().BeEmpty("DI-6: sync must be skipped when template has zero slots");
    }

    #endregion

    #region DI-7: CloseAsync allows independent inventory updates after template is closed

    /// <summary>
    /// DI-7 Edge Case: After a template transitions to Cerrado, ConfiguracionSlots
    /// retains the last synced state. The template is now historical and
    /// ConfiguracionSlots can be updated independently (out of band).
    /// </summary>
    [Fact]
    public async Task CloseAsync_AllowsIndependentInventoryUpdates()
    {
        // Arrange: maquina + producto + template in Activo
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine DI-7");
        _context.Maquinas.Add(maquina);

        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Product 1");
        _context.Productos.Add(producto);

        var template = new TemplateRecarga
        {
            Id = 70,
            Nombre = "Template to Close",
            Estado = EstadoTemplate.Activo,
            FechaCargaInicio = DateTime.Now.AddHours(-3),
            FechaCargaFin = DateTime.Now.AddHours(-1),
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 70,
                    MaquinaId = 1,
                    FechaRecarga = DateTime.Now.AddHours(-3),
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "SLOT-1", ProductoId = 1, CantidadInicial = 8, CapacidadSlot = 10, Estado = EstadoSlot.Lleno }
                    }
                }
            }
        };
        _context.TemplatesRecarga.Add(template);

        // Pre-populate ConfiguracionSlots to simulate an existing synced cache
        _context.ConfiguracionSlots.Add(new VendingManager.Core.Entities.ConfiguracionSlot
        {
            MaquinaId = 1,
            NumeroSlot = "SLOT-1",
            ProductoId = 1,
            StockActual = 8,
            CapacidadMaxima = 10,
            StockMinimo = 2,
            PrecioVenta = 0
        });

        await _context.SaveChangesAsync();

        // Act: close the template
        var result = await _service.CloseAsync(template.Id);

        // Assert: template is now Cerrado
        result.Estado.Should().Be(EstadoTemplate.Cerrado);

        // DI-7: ConfiguracionSlots still has the last synced state (unchanged after close)
        var configSlot = await _context.ConfiguracionSlots
            .FirstOrDefaultAsync(c => c.MaquinaId == 1 && c.NumeroSlot == "SLOT-1");
        configSlot.Should().NotBeNull();
        configSlot!.ProductoId.Should().Be(1);
        configSlot.StockActual.Should().Be(8);

        // DI-7: We can update ConfiguracionSlots independently after close
        // (simulating an out-of-band inventory adjustment)
        configSlot.StockActual = 5;
        await _context.SaveChangesAsync();

        var updated = await _context.ConfiguracionSlots
            .FirstOrDefaultAsync(c => c.MaquinaId == 1 && c.NumeroSlot == "SLOT-1");
        updated!.StockActual.Should().Be(5, "DI-7: ConfiguracionSlots can be updated independently after template is closed");
    }

    #endregion

    #region SyncSlotsToConfiguracionAsync tests

    /// <summary>
    /// Syncs SnapshotSlots to ConfiguracionSlots on finalize.
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
            Estado = EstadoTemplate.EnCarga,
            FechaCargaInicio = DateTime.Now,
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
}