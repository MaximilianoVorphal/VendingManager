using Microsoft.Extensions.Options;
using VendingManager.Core.Configuration;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Services;
using VendingManager.Tests.TestData;

namespace VendingManager.Tests.Services;

/// <summary>
/// Integration tests for auto slot creation when creating a machine with a MaquinaTipo.
/// </summary>
public class MaquinaServiceSlotCreationTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IOptions<VendingConfig> _config;
    private readonly IMaquinaService _service;

    public MaquinaServiceSlotCreationTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"MaquinaSlotCreation_{Guid.NewGuid()}");
        _config = Options.Create(new VendingConfig
        {
            OffsetDriftThresholdHours = 1,
            OffsetDriftMinSamples = 5,
            DefaultTimezoneOffsetHours = -11
        });
        _service = new MaquinaService(_context, _config);
    }

    [Fact]
    public async Task CreateMaquinaAsync_CafeSnack_Creates44Slots()
    {
        // Arrange
        var maquina = new Maquina
        {
            Nombre = "CAFE-SNACK-MAQ",
            Ubicacion = "Test",
            IdInternoMaquina = "CS-001",
            CodigoTerminalPos = "POS-CS",
            MaquinaTipo = (int)MaquinaTipo.CafeSnack
        };

        // Act
        var result = await _service.CreateMaquinaAsync(maquina);

        // Assert
        result.Id.Should().BeGreaterThan(0);
        var slots = await _context.ConfiguracionSlots
            .Where(s => s.MaquinaId == result.Id)
            .ToListAsync();

        slots.Should().HaveCount(44);
        slots.Should().AllSatisfy(s =>
        {
            s.CapacidadMaxima.Should().Be(10);
            s.StockMinimo.Should().Be(2);
            s.PrecioVenta.Should().Be(0);
            s.MaquinaId.Should().Be(result.Id);
        });

        // Verify all slot numbers (unordered) match the expected template
        var expectedCoffee = Enumerable.Range(1, 14).Select(n => n.ToString());
        var expectedSnack = Enumerable.Range(101, 59)
            .Where(n => n % 2 == 1)
            .Select(n => n.ToString());
        var expectedAll = expectedCoffee.Concat(expectedSnack);
        slots.Select(s => s.NumeroSlot).Should().BeEquivalentTo(expectedAll);
    }

    [Fact]
    public async Task CreateMaquinaAsync_Snack_Creates56Slots()
    {
        // Arrange
        var maquina = new Maquina
        {
            Nombre = "SNACK-MAQ",
            Ubicacion = "Test",
            IdInternoMaquina = "SN-001",
            CodigoTerminalPos = "POS-SN",
            MaquinaTipo = (int)MaquinaTipo.Snack
        };

        // Act
        var result = await _service.CreateMaquinaAsync(maquina);

        // Assert
        var slots = await _context.ConfiguracionSlots
            .Where(s => s.MaquinaId == result.Id)
            .ToListAsync();

        slots.Should().HaveCount(56);
        slots.Should().AllSatisfy(s => s.CapacidadMaxima.Should().Be(10));
    }

    [Fact]
    public async Task CreateMaquinaAsync_NullTipo_CreatesZeroSlots()
    {
        // Arrange
        var maquina = new Maquina
        {
            Nombre = "NO-TYPE-MAQ",
            Ubicacion = "Test",
            IdInternoMaquina = "NT-001",
            CodigoTerminalPos = "POS-NT",
            MaquinaTipo = null
        };

        // Act
        var result = await _service.CreateMaquinaAsync(maquina);

        // Assert
        var slots = await _context.ConfiguracionSlots
            .Where(s => s.MaquinaId == result.Id)
            .ToListAsync();

        slots.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateMaquinaAsync_WithPreExistingSlots_SkipsAutoCreation()
    {
        // Arrange
        var maquina = new Maquina
        {
            Nombre = "PRE-SLOTS-MAQ",
            Ubicacion = "Test",
            IdInternoMaquina = "PS-001",
            CodigoTerminalPos = "POS-PS",
            MaquinaTipo = (int)MaquinaTipo.CafeSnack,
            Slots = new List<ConfiguracionSlot>
            {
                new()
                {
                    NumeroSlot = "CUSTOM-1",
                    CapacidadMaxima = 10,
                    StockMinimo = 2,
                    PrecioVenta = 500
                }
            }
        };

        // Act
        var result = await _service.CreateMaquinaAsync(maquina);

        // Assert — only the pre-existing slot should remain
        var slots = await _context.ConfiguracionSlots
            .Where(s => s.MaquinaId == result.Id)
            .ToListAsync();

        slots.Should().HaveCount(1);
        slots[0].NumeroSlot.Should().Be("CUSTOM-1");
    }

    [Fact]
    public async Task UpdateMaquinaAsync_DoesNotDuplicateSlots()
    {
        // Arrange — create a machine with CafeSnack type
        var maquina = new Maquina
        {
            Nombre = "UPDATE-TEST",
            Ubicacion = "Test",
            IdInternoMaquina = "UT-001",
            CodigoTerminalPos = "POS-UT",
            MaquinaTipo = (int)MaquinaTipo.CafeSnack
        };
        var created = await _service.CreateMaquinaAsync(maquina);

        // Verify 44 slots were created
        var slotsBefore = await _context.ConfiguracionSlots
            .Where(s => s.MaquinaId == created.Id)
            .ToListAsync();
        slotsBefore.Should().HaveCount(44);

        // Act — update the machine (change name, keep tipo)
        created.Nombre = "UPDATED-NAME";
        await _service.UpdateMaquinaAsync(created.Id, created);

        // Assert — slot count unchanged
        var slotsAfter = await _context.ConfiguracionSlots
            .Where(s => s.MaquinaId == created.Id)
            .ToListAsync();

        slotsAfter.Should().HaveCount(44);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
