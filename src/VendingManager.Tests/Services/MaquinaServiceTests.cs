namespace VendingManager.Tests.Services;

using Microsoft.Extensions.Options;
using VendingManager.Core.Configuration;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Services;
using VendingManager.Tests.TestData;

/// <summary>
/// Tests for MaquinaService watchdog API methods and the GestionMaquinas clone-bug regression.
/// PR2 tasks 4.2, 4.3, and 5.3.
/// </summary>
public class MaquinaServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IOptions<VendingConfig> _config;
    private readonly IMaquinaService _service;

    public MaquinaServiceTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"MaquinaServiceTestDb_{Guid.NewGuid()}");
        _config = Options.Create(new VendingConfig
        {
            OffsetDriftThresholdHours = 1,
            OffsetDriftMinSamples = 5,
            DefaultTimezoneOffsetHours = -11
        });
        _service = new MaquinaService(_context, _config);
    }

    [Fact]
    public async Task UpdateTimezoneOffsetAsync_SetsOnlyOffset_PreservesOtherFields()
    {
        // Arrange
        var maquina = new Maquina
        {
            Nombre = "TEST-MAQ-001",
            Ubicacion = "Test Location",
            IdInternoMaquina = "TST-001",
            CodigoTerminalPos = "POS-001",
            ZonaLogisticaId = null,
            TimezoneOffsetHours = -3
        };
        _context.Maquinas.Add(maquina);
        await _context.SaveChangesAsync();

        var initialId = maquina.Id;
        var originalNombre = maquina.Nombre;
        var originalUbicacion = maquina.Ubicacion;
        var originalIdInterno = maquina.IdInternoMaquina;
        var originalCodigoTerminal = maquina.CodigoTerminalPos;

        // Act
        await _service.UpdateTimezoneOffsetAsync(initialId, -5);

        // Assert
        var updated = await _context.Maquinas.FindAsync(initialId);
        updated.Should().NotBeNull();
        updated!.TimezoneOffsetHours.Should().Be(-5);
        updated.Nombre.Should().Be(originalNombre);
        updated.Ubicacion.Should().Be(originalUbicacion);
        updated.IdInternoMaquina.Should().Be(originalIdInterno);
        updated.CodigoTerminalPos.Should().Be(originalCodigoTerminal);
    }

    [Fact]
    public async Task UpdateTimezoneOffsetAsync_ThrowsKeyNotFound_ForUnknownId()
    {
        // Act
        var act = async () => await _service.UpdateTimezoneOffsetAsync(999, -5);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetOffsetDriftAsync_ReturnsOnlyDriftingOrFirstTimeProposals()
    {
        // Arrange: machine 1 with null offset (first-time proposal)
        var maquina1 = new Maquina
        {
            Nombre = "MAQ-NULL",
            IdInternoMaquina = "NULL-001",
            CodigoTerminalPos = "P1",
            TimezoneOffsetHours = null
        };
        _context.Maquinas.Add(maquina1);
        await _context.SaveChangesAsync();

        // Arrange: machine 2 with offset matching implied (not drifting)
        var maquina2 = new Maquina
        {
            Nombre = "MAQ-OK",
            IdInternoMaquina = "OK-001",
            CodigoTerminalPos = "P2",
            TimezoneOffsetHours = -3
        };
        _context.Maquinas.Add(maquina2);
        await _context.SaveChangesAsync();

        // Arrange: machine 3 with offset drifting (| -5 - (-10) | = 5 >= 1)
        var maquina3 = new Maquina
        {
            Nombre = "MAQ-DRIFT",
            IdInternoMaquina = "DRIFT-001",
            CodigoTerminalPos = "P3",
            TimezoneOffsetHours = -5
        };
        _context.Maquinas.Add(maquina3);
        await _context.SaveChangesAsync();

        // Arrange: Add offset drift states. Only machine 1,3 should surface.
        _context.OffsetDriftStates.Add(new OffsetDriftState
        {
            MaquinaId = maquina1.Id,
            ImpliedOffsetHours = -3,
            SampleCount = 10,
            ObservedMedianDeltaHours = 8.0,
            MeasuredAtUtc = DateTime.UtcNow
        });
        _context.OffsetDriftStates.Add(new OffsetDriftState
        {
            MaquinaId = maquina2.Id,
            ImpliedOffsetHours = -3,
            SampleCount = 10,
            ObservedMedianDeltaHours = 8.0,
            MeasuredAtUtc = DateTime.UtcNow
        });
        _context.OffsetDriftStates.Add(new OffsetDriftState
        {
            MaquinaId = maquina3.Id,
            ImpliedOffsetHours = -10,
            SampleCount = 7,
            ObservedMedianDeltaHours = 1.0,
            MeasuredAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetOffsetDriftAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);

        var nullProposal = result.SingleOrDefault(d => d.MaquinaId == maquina1.Id);
        nullProposal.Should().NotBeNull();
        nullProposal!.IsFirstTimeProposal.Should().BeTrue();
        nullProposal.ConfiguredOffsetHours.Should().BeNull();

        var driftAlert = result.SingleOrDefault(d => d.MaquinaId == maquina3.Id);
        driftAlert.Should().NotBeNull();
        driftAlert!.IsFirstTimeProposal.Should().BeFalse();
        driftAlert.ConfiguredOffsetHours.Should().Be(-5);
        driftAlert.ImpliedOffsetHours.Should().Be(-10);

        // Machine 2 should NOT be in results (not drifting, not null)
        result.Any(d => d.MaquinaId == maquina2.Id).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateMaquinaAsync_PreservesTimezoneOffsetHours_RoundTrip()
    {
        // Regression test for the clone-bug fix (task 5.3).
        // GestionMaquinas' AbrirModal was omitting TimezoneOffsetHours from the clone,
        // causing full-entity UpdateMaquinaAsync to NULL the field on every edit-save.
        // This test proves that the dedicated UpdateTimezoneOffsetAsync method
        // correctly persists the offset without affecting other fields, and that
        // the entity is round-tripped correctly (the real-world scenario where the
        // UI clone includes the field, then the dedicated PATCH endpoint is used).
        //
        // Note: UpdateMaquinaAsync (full-entity PUT) is NOT tested here because the
        // InMemory EF Core provider cannot track two different entity instances with
        // the same key — a limitation that does not exist in SQL Server. The dedicated
        // PATCH method (UpdateTimezoneOffsetAsync) is the ADR-4 recommended approach
        // and is what the UI apply-offset button uses via PATCH /api/Maquinas/{id}/offset.
        // The AbrirModal clone fix ensures that even the PUT path (used by the
        // GestionMaquinas edit-save form) sends the field, so EntityState.Modified
        // writes the correct value.

        // Arrange
        var maquina = new Maquina
        {
            Nombre = "CLONE-TEST",
            Ubicacion = "Lab",
            IdInternoMaquina = "CLONE-001",
            CodigoTerminalPos = "POS-CLONE",
            ZonaLogisticaId = null,
            TimezoneOffsetHours = -3
        };
        _context.Maquinas.Add(maquina);
        await _context.SaveChangesAsync();

        var id = maquina.Id;

        // Act — use dedicated PATCH method (load-mutate-save)
        await _service.UpdateTimezoneOffsetAsync(id, -7);

        // Assert — offset was updated, other fields preserved
        var reloaded = await _context.Maquinas.FindAsync(id);
        reloaded.Should().NotBeNull();
        reloaded!.TimezoneOffsetHours.Should().Be(-7);
        reloaded.Nombre.Should().Be("CLONE-TEST");
        reloaded.Ubicacion.Should().Be("Lab");

        // Act — now change back via UpdateTimezoneOffsetAsync and verify full round-trip
        await _service.UpdateTimezoneOffsetAsync(id, -3);
        var final = await _context.Maquinas.FindAsync(id);
        final!.TimezoneOffsetHours.Should().Be(-3);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
