namespace VendingManager.Tests.Services;

using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using VendingManager.Core.Entities;
using VendingManager.Infrastructure.Services;
using VendingManager.Tests.TestData;

/// <summary>
/// Task 2.1: GastoRecurrenteService.AplicarGastoAsync must propagate
/// GastoRecurrente.MaquinaId to the created MovimientoCaja.
///
/// Spec scenarios:
///   - GastoRecurrente with MaquinaId=7 → MovimientoCaja.MaquinaId=7
///   - GastoRecurrente with MaquinaId=null → MovimientoCaja.MaquinaId=null
/// </summary>
public class GastoRecurrenteServiceEbitdaTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly GastoRecurrenteService _service;

    public GastoRecurrenteServiceEbitdaTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext(
            $"GastoRecurrenteEbitda_{Guid.NewGuid()}");
        _service = new GastoRecurrenteService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>
    /// Spec: GastoRecurrente per-machine propagates MaquinaId.
    /// GIVEN GastoRecurrente with MaquinaId=7
    /// WHEN AplicarGastoAsync is called
    /// THEN the created MovimientoCaja SHALL have MaquinaId=7
    /// </summary>
    [Fact]
    public async Task AplicarGastoAsync_PropagatesMaquinaId()
    {
        // Arrange
        var gasto = new GastoRecurrente
        {
            Id = 99,
            Descripcion = "Internet M-007",
            MontoEstimado = 30_000m,
            Categoria = "INTERNET",
            Activo = true,
            MaquinaId = 7
        };
        _context.GastosRecurrentes.Add(gasto);
        await _context.SaveChangesAsync();

        // Act
        await _service.AplicarGastoAsync(99, 6, 2026);

        // Assert
        var movimiento = await _context.MovimientosCaja
            .FirstOrDefaultAsync(m => m.GastoRecurrenteId == 99);
        movimiento.Should().NotBeNull();
        movimiento!.MaquinaId.Should().Be(7,
            "MaquinaId should propagate from GastoRecurrente");
        movimiento.Monto.Should().Be(-30_000m);
        movimiento.Descripcion.Should().Be("Internet M-007");
        movimiento.Categoria.Should().Be("INTERNET");
    }

    /// <summary>
    /// Spec: GastoRecurrente fleet-level leaves MaquinaId null.
    /// GIVEN GastoRecurrente with MaquinaId=NULL
    /// WHEN AplicarGastoAsync is called
    /// THEN created MovimientoCaja SHALL have MaquinaId=NULL
    /// </summary>
    [Fact]
    public async Task AplicarGastoAsync_NullMaquinaId()
    {
        // Arrange
        var gasto = new GastoRecurrente
        {
            Id = 100,
            Descripcion = "Admin general",
            MontoEstimado = 50_000m,
            Categoria = "INFRA",
            Activo = true,
            MaquinaId = null
        };
        _context.GastosRecurrentes.Add(gasto);
        await _context.SaveChangesAsync();

        // Act
        await _service.AplicarGastoAsync(100, 6, 2026);

        // Assert
        var movimiento = await _context.MovimientosCaja
            .FirstOrDefaultAsync(m => m.GastoRecurrenteId == 100);
        movimiento.Should().NotBeNull();
        movimiento!.MaquinaId.Should().BeNull(
            "MaquinaId should remain null for fleet-level gasto");
        movimiento.Monto.Should().Be(-50_000m);
    }
}
