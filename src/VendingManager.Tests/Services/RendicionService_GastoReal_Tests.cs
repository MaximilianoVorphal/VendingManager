namespace VendingManager.Tests.Services;

using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using VendingManager.Core.Entities;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.Enums;
using VendingManager.Tests.TestData;

/// <summary>
/// Tests verifying the structural exclusion in RendicionService.GetResumenAsync.
/// Approved behavior change: rendicion totals now exclude DEVOLUCION_RENDICION rows.
/// </summary>
public class RendicionService_GastoReal_Tests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly RendicionService _service;

    public RendicionService_GastoReal_Tests()
    {
        _context = TestDataHelpers.CreateInMemoryContext(
            $"RendicionGastoRealTestDb_{Guid.NewGuid()}");
        _service = new RendicionService(_context);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetResumenAsync_ExcludesStructuralGastos()
    {
        // Arrange: rendicion with 3 gastos — 2 real, 1 structural
        var rendicion = new Rendicion
        {
            FechaInicio = new DateTime(2026, 7, 1),
            FechaFin = new DateTime(2026, 7, 7),
            Trabajador = "Test Worker",
            Estado = RendicionEstado.Abierta,
            Gastos = new List<MovimientoCaja>
            {
                new MovimientoCaja
                {
                    Fecha = new DateTime(2026, 7, 2),
                    Monto = -1000m,
                    Tipo = "GASTO",
                    Categoria = "LOGISTICA",
                    Descripcion = "Bencina"
                },
                new MovimientoCaja
                {
                    Fecha = new DateTime(2026, 7, 3),
                    Monto = -500m,
                    Tipo = "GASTO",
                    Categoria = "DEVOLUCION_RENDICION",
                    Descripcion = "Devolución saldo"
                },
                new MovimientoCaja
                {
                    Fecha = new DateTime(2026, 7, 4),
                    Monto = -200m,
                    Tipo = "GASTO",
                    Categoria = "PEAJES",
                    Descripcion = "Tag"
                }
            }
        };

        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        // Act
        var resumen = await _service.GetResumenAsync(rendicion.Id);

        // Assert: only LOGISTICA (1000) + PEAJES (200) = 1200
        // DEVOLUCION_RENDICION (500) excluded
        resumen.TotalGastos.Should().Be(1200m);
    }

    [Fact]
    public async Task GetResumenAsync_NoStructuralGastos_ReturnsAll()
    {
        var rendicion = new Rendicion
        {
            FechaInicio = new DateTime(2026, 7, 1),
            FechaFin = new DateTime(2026, 7, 7),
            Trabajador = "Test Worker",
            Estado = RendicionEstado.Abierta,
            Gastos = new List<MovimientoCaja>
            {
                new MovimientoCaja
                {
                    Fecha = new DateTime(2026, 7, 2),
                    Monto = -1000m,
                    Tipo = "GASTO",
                    Categoria = "LOGISTICA",
                    Descripcion = "Bencina"
                }
            }
        };

        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var resumen = await _service.GetResumenAsync(rendicion.Id);

        resumen.TotalGastos.Should().Be(1000m);
    }
}
