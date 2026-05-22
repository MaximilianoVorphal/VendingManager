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
/// State machine: Pendiente (0) → Activo (1) → Terminado (2).
/// Reabrir: Terminado (2) → Pendiente (0).
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

    #region ActivarAsync tests (Pendiente → Activo)

    /// <summary>
    /// Valid: Pendiente → Activo transitions correctly.
    /// </summary>
    [Fact]
    public async Task ActivarAsync_Pendiente_SetsActivo()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        _context.Maquinas.Add(maquina);

        var template = new TemplateRecarga
        {
            Id = 1,
            Nombre = "Test Template",
            Estado = EstadoTemplate.Pendiente,
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
        var result = await _service.ActivarAsync(template.Id);

        // Assert
        result.Estado.Should().Be(EstadoTemplate.Activo);
    }

    /// <summary>
    /// Invalid: Activo → Activo throws InvalidOperationException.
    /// </summary>
    [Fact]
    public async Task ActivarAsync_Activo_ThrowsInvalidOperationException()
    {
        // Arrange
        var template = new TemplateRecarga
        {
            Id = 2,
            Nombre = "Already Activo",
            Estado = EstadoTemplate.Activo
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.ActivarAsync(template.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Activo*");
    }

    /// <summary>
    /// Invalid: Terminado → Activo throws InvalidOperationException.
    /// </summary>
    [Fact]
    public async Task ActivarAsync_Terminado_ThrowsInvalidOperationException()
    {
        // Arrange
        var template = new TemplateRecarga
        {
            Id = 3,
            Nombre = "Already Terminado",
            Estado = EstadoTemplate.Terminado
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.ActivarAsync(template.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Pendiente*");
    }

    /// <summary>
    /// Not found: throws InvalidOperationException with clear message.
    /// </summary>
    [Fact]
    public async Task ActivarAsync_NotFound_ThrowsInvalidOperationException()
    {
        var act = async () => await _service.ActivarAsync(9999);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no encontrado*");
    }

    #endregion

    #region TerminarAsync tests (Activo → Terminado)

    /// <summary>
    /// Valid: Activo → Terminado transitions correctly.
    /// </summary>
    [Fact]
    public async Task TerminarAsync_Activo_SetsTerminado()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        _context.Maquinas.Add(maquina);

        var template = new TemplateRecarga
        {
            Id = 10,
            Nombre = "Template to Terminar",
            Estado = EstadoTemplate.Activo,
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
    /// Invalid: Pendiente → Terminado throws InvalidOperationException.
    /// </summary>
    [Fact]
    public async Task TerminarAsync_Pendiente_ThrowsInvalidOperationException()
    {
        // Arrange
        var template = new TemplateRecarga
        {
            Id = 11,
            Nombre = "Pendiente Template",
            Estado = EstadoTemplate.Pendiente
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.TerminarAsync(template.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Activo*");
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
            .WithMessage("*Activo*");
    }

    #endregion

    #region ReabrirAsync tests (Terminado → Pendiente)

    /// <summary>
    /// Valid: Terminado → Pendiente resets state and clears slots.
    /// </summary>
    [Fact]
    public async Task ReabrirAsync_Terminado_ResetsToPendiente()
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
                        new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 10, Estado = EstadoSlot.Lleno }
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
    }

    /// <summary>
    /// Invalid: Activo → Reabrir throws InvalidOperationException (must be Terminado).
    /// </summary>
    [Fact]
    public async Task ReabrirAsync_Activo_ThrowsInvalidOperationException()
    {
        // Arrange
        var template = new TemplateRecarga
        {
            Id = 21,
            Nombre = "Activo Template",
            Estado = EstadoTemplate.Activo
        };
        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.ReabrirAsync(template.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Terminado*");
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

    #region GetLatestActivoTemplateSlotsAsync tests

    /// <summary>
    /// Returns SnapshotSlots from the latest Activo template for the machine.
    /// </summary>
    [Fact]
    public async Task GetLatestActivoTemplateSlotsAsync_WithActivoTemplate_ReturnsSlots()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine 1");
        _context.Maquinas.Add(maquina);

        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Product 1");
        _context.Productos.Add(producto);

        var template = new TemplateRecarga
        {
            Id = 40,
            Nombre = "Activo Template",
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
        var result = await _service.GetLatestActivoTemplateSlotsAsync(maquina.Id);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(s => s.NumeroSlot == "1");
        result.Should().Contain(s => s.NumeroSlot == "2");
    }

    /// <summary>
    /// Returns empty list when no Activo template exists for the machine.
    /// </summary>
    [Fact]
    public async Task GetLatestActivoTemplateSlotsAsync_NoActivoTemplate_ReturnsEmpty()
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
        var result = await _service.GetLatestActivoTemplateSlotsAsync(maquina.Id);

        // Assert
        result.Should().BeEmpty();
    }

    /// <summary>
    /// Returns only slots from the most recent Activo template (not all Activo templates).
    /// </summary>
    [Fact]
    public async Task GetLatestActivoTemplateSlotsAsync_MultipleActivo_ReturnsLatest()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 3, nombre: "Machine 3");
        _context.Maquinas.Add(maquina);

        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Product 1");
        _context.Productos.Add(producto);

        // Older Activo template
        var olderTemplate = new TemplateRecarga
        {
            Id = 50,
            Nombre = "Old Activo",
            Estado = EstadoTemplate.Activo,
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

        // Newer Activo template
        var newerTemplate = new TemplateRecarga
        {
            Id = 51,
            Nombre = "New Activo",
            Estado = EstadoTemplate.Activo,
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
        var result = await _service.GetLatestActivoTemplateSlotsAsync(maquina.Id);

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

    #region StartLoadingAsync removed tests

    /// <summary>
    /// StartLoadingAsync no longer exists (EnCarga state removed).
    /// </summary>
    [Fact]
    public void StartLoadingAsync_NoLongerExists()
    {
        _service.GetType().GetMethod("StartLoadingAsync").Should().BeNull();
    }

    #endregion

    #region FinalizeAsync removed tests

    /// <summary>
    /// FinalizeAsync no longer exists (old Activo state now handled differently).
    /// </summary>
    [Fact]
    public void FinalizeAsync_NoLongerExists()
    {
        _service.GetType().GetMethod("FinalizeAsync").Should().BeNull();
    }

    #endregion

    #region GetLatestTerminadoTemplateSlotsAsync renamed tests

    /// <summary>
    /// GetLatestTerminadoTemplateSlotsAsync renamed to GetLatestActivoTemplateSlotsAsync.
    /// </summary>
    [Fact]
    public void GetLatestTerminadoTemplateSlotsAsync_Renamed()
    {
        _service.GetType().GetMethod("GetLatestTerminadoTemplateSlotsAsync").Should().BeNull();
        _service.GetType().GetMethod("GetLatestActivoTemplateSlotsAsync").Should().NotBeNull();
    }

    #endregion
}