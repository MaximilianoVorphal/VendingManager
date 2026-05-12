namespace VendingManager.Tests.Services;

using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.DTOs;
using VendingManager.Tests.TestData;

/// <summary>
/// Tests for ValidateFechaRecargaChainAsync behavior in TemplateRecargaService.
/// Validates that retrospective and intermediate inserts are allowed, and that
/// exact duplicates are rejected with clear error messaging.
/// </summary>
public class TemplateRecargaService_ValidateChainTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly TemplateRecargaService _service;

    public TemplateRecargaService_ValidateChainTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"ValidateChainTestDb_{Guid.NewGuid()}");
        var mockLogger = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<TemplateRecargaService>>();
        _service = new TemplateRecargaService(_context, mockLogger.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helper: crea un template con un periodo para la máquina dada.
    // -------------------------------------------------------------------------
    private async Task<TemplateRecarga> CreateTemplateWithPeriodAsync(
        int maquinaId,
        DateTime fechaRecarga,
        int periodoId = 1,
        int templateId = 1)
    {
        var maquina = TestDataHelpers.CreateMaquina(id: maquinaId);
        _context.Maquinas.Add(maquina);

        var template = new TemplateRecarga
        {
            Id = templateId,
            Nombre = $"Template {templateId}",
            FechaCreacion = DateTime.Now
        };
        _context.TemplatesRecarga.Add(template);

        var periodo = new PeriodoRecarga
        {
            Id = periodoId,
            TemplateRecargaId = templateId,
            MaquinaId = maquinaId,
            FechaRecarga = fechaRecarga,
            SnapshotSlots = new List<SnapshotSlot>
            {
                new()
                {
                    NumeroSlot = "1",
                    ProductoId = 1,
                    CantidadInicial = 10,
                    CapacidadSlot = 20,
                    Estado = VendingManager.Shared.Enums.EstadoSlot.Lleno
                }
            }
        };
        _context.PeriodosRecarga.Add(periodo);
        await _context.SaveChangesAsync();

        return template;
    }

    // -------------------------------------------------------------------------
    // Helper: crea un periodo directamente en la BD (sin slot) para agregar
    // períodos adicionales a un template existente.
    // -------------------------------------------------------------------------
    private async Task AddPeriodToTemplateAsync(
        int templateId,
        int maquinaId,
        DateTime fechaRecarga,
        int periodoId)
    {
        var periodo = new PeriodoRecarga
        {
            Id = periodoId,
            TemplateRecargaId = templateId,
            MaquinaId = maquinaId,
            FechaRecarga = fechaRecarga,
            SnapshotSlots = new List<SnapshotSlot>
            {
                new()
                {
                    NumeroSlot = "1",
                    ProductoId = 1,
                    CantidadInicial = 10,
                    CapacidadSlot = 20,
                    Estado = VendingManager.Shared.Enums.EstadoSlot.Lleno
                }
            }
        };
        _context.PeriodosRecarga.Add(periodo);
        await _context.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Scenario 1: No prior periods for machine → allowed
    // -------------------------------------------------------------------------
    [Fact]
    public async Task CreateAsync_NoPriorPeriods_Allowed()
    {
        // Arrange: Machine 1 exists but has no periods.
        var maquina = TestDataHelpers.CreateMaquina(id: 1);
        _context.Maquinas.Add(maquina);
        await _context.SaveChangesAsync();

        var dto = new CreateTemplateRecargaDto
        {
            Nombre = "Template nuevo",
            Descripcion = "Sin períodos previos",
            Periodos = new List<CreatePeriodoDto>
            {
                new()
                {
                    MaquinaId = 1,
                    FechaRecarga = new DateTime(2025, 1, 15),
                    SnapshotSlots = new List<CreateSnapshotSlotDto>
                    {
                        new()
                        {
                            NumeroSlot = "1",
                            ProductoId = 1,
                            CantidadInicial = 10,
                            CapacidadSlot = 20,
                            Estado = VendingManager.Shared.Enums.EstadoSlot.Lleno
                        }
                    }
                }
            }
        };

        // Act & Assert: Should NOT throw.
        var result = await _service.CreateAsync(dto);
        result.Should().NotBeNull();
        result.Periodos.Should().HaveCount(1);
    }

    // -------------------------------------------------------------------------
    // Scenario 2: Retrospective insert (earlier than all existing) → allowed
    // -------------------------------------------------------------------------
    [Fact]
    public async Task CreateAsync_RetrospectiveInsert_Allowed()
    {
        // Arrange: Machine 1 has periods on 2025-03-01 and 2025-04-01.
        await CreateTemplateWithPeriodAsync(maquinaId: 1, fechaRecarga: new DateTime(2025, 3, 1), periodoId: 1, templateId: 1);
        await AddPeriodToTemplateAsync(templateId: 1, maquinaId: 1, fechaRecarga: new DateTime(2025, 4, 1), periodoId: 2);

        var dto = new CreateTemplateRecargaDto
        {
            Nombre = "Template retrospectivo",
            Descripcion = "Inserta antes del primero",
            Periodos = new List<CreatePeriodoDto>
            {
                new()
                {
                    MaquinaId = 1,
                    FechaRecarga = new DateTime(2025, 2, 1), // ← antes de todos
                    SnapshotSlots = new List<CreateSnapshotSlotDto>
                    {
                        new()
                        {
                            NumeroSlot = "1",
                            ProductoId = 1,
                            CantidadInicial = 10,
                            CapacidadSlot = 20,
                            Estado = VendingManager.Shared.Enums.EstadoSlot.Lleno
                        }
                    }
                }
            }
        };

        // Act & Assert: Should NOT throw.
        var result = await _service.CreateAsync(dto);
        result.Should().NotBeNull();
        result.Periodos.Should().HaveCount(1);
    }

    // -------------------------------------------------------------------------
    // Scenario 3: Intermediate insert (between two existing) → allowed
    // -------------------------------------------------------------------------
    [Fact]
    public async Task CreateAsync_IntermediateInsert_Allowed()
    {
        // Arrange: Machine 1 has periods on 2025-01-15 and 2025-04-01.
        await CreateTemplateWithPeriodAsync(maquinaId: 1, fechaRecarga: new DateTime(2025, 1, 15), periodoId: 1, templateId: 1);
        await AddPeriodToTemplateAsync(templateId: 1, maquinaId: 1, fechaRecarga: new DateTime(2025, 4, 1), periodoId: 2);

        var dto = new CreateTemplateRecargaDto
        {
            Nombre = "Template intermedio",
            Descripcion = "Inserta entre dos períodos",
            Periodos = new List<CreatePeriodoDto>
            {
                new()
                {
                    MaquinaId = 1,
                    FechaRecarga = new DateTime(2025, 3, 1), // ← entre 2025-01-15 y 2025-04-01
                    SnapshotSlots = new List<CreateSnapshotSlotDto>
                    {
                        new()
                        {
                            NumeroSlot = "1",
                            ProductoId = 1,
                            CantidadInicial = 10,
                            CapacidadSlot = 20,
                            Estado = VendingManager.Shared.Enums.EstadoSlot.Lleno
                        }
                    }
                }
            }
        };

        // Act & Assert: Should NOT throw.
        var result = await _service.CreateAsync(dto);
        result.Should().NotBeNull();
        result.Periodos.Should().HaveCount(1);
    }

    // -------------------------------------------------------------------------
    // Scenario 4: Forward insert (later than all existing) → allowed
    // -------------------------------------------------------------------------
    [Fact]
    public async Task CreateAsync_ForwardInsert_Allowed()
    {
        // Arrange: Machine 1 has periods on 2025-01-15 and 2025-03-01.
        await CreateTemplateWithPeriodAsync(maquinaId: 1, fechaRecarga: new DateTime(2025, 1, 15), periodoId: 1, templateId: 1);
        await AddPeriodToTemplateAsync(templateId: 1, maquinaId: 1, fechaRecarga: new DateTime(2025, 3, 1), periodoId: 2);

        var dto = new CreateTemplateRecargaDto
        {
            Nombre = "Template forward",
            Descripcion = "Inserta después del último",
            Periodos = new List<CreatePeriodoDto>
            {
                new()
                {
                    MaquinaId = 1,
                    FechaRecarga = new DateTime(2025, 4, 1), // ← después de todos
                    SnapshotSlots = new List<CreateSnapshotSlotDto>
                    {
                        new()
                        {
                            NumeroSlot = "1",
                            ProductoId = 1,
                            CantidadInicial = 10,
                            CapacidadSlot = 20,
                            Estado = VendingManager.Shared.Enums.EstadoSlot.Lleno
                        }
                    }
                }
            }
        };

        // Act & Assert: Should NOT throw.
        var result = await _service.CreateAsync(dto);
        result.Should().NotBeNull();
        result.Periodos.Should().HaveCount(1);
    }

    // -------------------------------------------------------------------------
    // Scenario 5: Exact duplicate of existing period → rejected with
    // InvalidOperationException containing machine ID and date
    // -------------------------------------------------------------------------
    [Fact]
    public async Task CreateAsync_ExactDuplicate_ThrowsInvalidOperationException()
    {
        // Arrange: Machine 1 has a period on 2025-03-15.
        await CreateTemplateWithPeriodAsync(maquinaId: 1, fechaRecarga: new DateTime(2025, 3, 15), periodoId: 1, templateId: 1);

        var dto = new CreateTemplateRecargaDto
        {
            Nombre = "Template duplicado",
            Descripcion = "Misma fecha que período existente",
            Periodos = new List<CreatePeriodoDto>
            {
                new()
                {
                    MaquinaId = 1,
                    FechaRecarga = new DateTime(2025, 3, 15), // ← duplicado exacto
                    SnapshotSlots = new List<CreateSnapshotSlotDto>
                    {
                        new()
                        {
                            NumeroSlot = "1",
                            ProductoId = 1,
                            CantidadInicial = 10,
                            CapacidadSlot = 20,
                            Estado = VendingManager.Shared.Enums.EstadoSlot.Lleno
                        }
                    }
                }
            }
        };

        // Act & Assert: Must throw with message containing both machine ID and date.
        var act = async () => await _service.CreateAsync(dto);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*1*")
            .WithMessage("*15/03/2025*");
    }

    // -------------------------------------------------------------------------
    // Scenario 6: Duplicate within excluded IDs (UpdateAsync scenario) → allowed
    // -------------------------------------------------------------------------
    [Fact]
    public async Task UpdateAsync_DuplicateWithExcludeIds_Allowed()
    {
        // Arrange: Template 1 has P1 (Mar 1) and P2 (Apr 1) for Machine 1.
        var maquina = TestDataHelpers.CreateMaquina(id: 1);
        _context.Maquinas.Add(maquina);
        await _context.SaveChangesAsync();

        var template = new TemplateRecarga
        {
            Id = 1,
            Nombre = "Template a reordenar",
            FechaCreacion = DateTime.Now
        };
        _context.TemplatesRecarga.Add(template);

        var p1 = new PeriodoRecarga
        {
            Id = 1,
            TemplateRecargaId = 1,
            MaquinaId = 1,
            FechaRecarga = new DateTime(2025, 3, 1),
            SnapshotSlots = new List<SnapshotSlot>
            {
                new()
                {
                    NumeroSlot = "SLOT-1",
                    ProductoId = 1,
                    CantidadInicial = 10,
                    CapacidadSlot = 20,
                    Estado = VendingManager.Shared.Enums.EstadoSlot.Lleno
                }
            }
        };
        var p2 = new PeriodoRecarga
        {
            Id = 2,
            TemplateRecargaId = 1,
            MaquinaId = 1,
            FechaRecarga = new DateTime(2025, 4, 1),
            SnapshotSlots = new List<SnapshotSlot>
            {
                new()
                {
                    NumeroSlot = "SLOT-2",
                    ProductoId = 1,
                    CantidadInicial = 10,
                    CapacidadSlot = 20,
                    Estado = VendingManager.Shared.Enums.EstadoSlot.Lleno
                }
            }
        };
        _context.PeriodosRecarga.AddRange(p1, p2);
        await _context.SaveChangesAsync();

        // Act: Update P1 to 2025-02-15, excluding [P1, P2] so P1 doesn't conflict with itself.
        var updateDto = new UpdateTemplateRecargaDto
        {
            Nombre = "Template reordered",
            Periodos = new List<CreatePeriodoDto>
            {
                new()
                {
                    MaquinaId = 1,
                    FechaRecarga = new DateTime(2025, 2, 15),
                    SnapshotSlots = new List<CreateSnapshotSlotDto>
                    {
                        new()
                        {
                            NumeroSlot = "SLOT-1",
                            ProductoId = 1,
                            CantidadInicial = 10,
                            CapacidadSlot = 20,
                            Estado = VendingManager.Shared.Enums.EstadoSlot.Lleno
                        }
                    }
                },
                new()
                {
                    MaquinaId = 1,
                    FechaRecarga = new DateTime(2025, 4, 1),
                    SnapshotSlots = new List<CreateSnapshotSlotDto>
                    {
                        new()
                        {
                            NumeroSlot = "SLOT-2",
                            ProductoId = 1,
                            CantidadInicial = 10,
                            CapacidadSlot = 20,
                            Estado = VendingManager.Shared.Enums.EstadoSlot.Lleno
                        }
                    }
                }
            }
        };

        // Act & Assert: Should NOT throw — excludePeriodoIds=[1,2] prevents
        // P1 from matching against itself when updating.
        var result = await _service.UpdateAsync(1, updateDto);
        result.Should().NotBeNull();
        result.Periodos.Should().HaveCount(2);
    }
}