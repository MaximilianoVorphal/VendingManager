namespace VendingManager.Tests.Components;

using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;
using VendingManager.Web.Pages.Contabilidad.State;

/// <summary>
/// Unit tests for ContabilidadPageState computed properties (TASK-12).
/// </summary>
public class ContabilidadPageStateTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static TransferenciaDto MakeTransferencia(bool verificada = true, int compraCount = 0)
    {
        var t = new TransferenciaDto
        {
            Id = Random.Shared.Next(1, 10000),
            Fecha = DateTime.Today,
            Monto = 1000m,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.EnUso,
            Verificada = verificada,
            Compras = Enumerable.Range(1, compraCount)
                .Select(_ => new CompraDto
                {
                    Id = Random.Shared.Next(1, 10000),
                    Proveedor = "Prov",
                    FechaCompra = DateTime.Today,
                    MontoTotal = 200m,
                    Verificada = verificada
                })
                .ToList()
        };
        return t;
    }

    private static AccountingPeriodFullDto MakeFullDto(
        decimal monto = 1000m,
        decimal devuelto = 0m,
        bool transVerificada = true,
        int compras = 0,
        bool comprasVerificadas = true)
    {
        var transferencia = new TransferenciaDto
        {
            Id = 1,
            Fecha = DateTime.Today,
            Monto = monto,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.EnUso,
            Verificada = transVerificada,
            Compras = Enumerable.Range(1, compras)
                .Select(i => new CompraDto
                {
                    Id = i,
                    Proveedor = "Prov",
                    FechaCompra = DateTime.Today,
                    MontoTotal = 100m,
                    Verificada = comprasVerificadas
                })
                .ToList()
        };

        return new AccountingPeriodFullDto
        {
            Id = 1,
            Name = "Test Period",
            FechaInicio = DateTime.Today,
            FechaFin = DateTime.Today.AddMonths(1),
            Estado = AccountingPeriodEstado.Abierto,
            TotalTransferido = monto,
            TotalCompras = compras * 100m,
            TotalGastos = 0m,
            Devuelto = devuelto,
            Transferencias = new List<TransferenciaDto> { transferencia }
        };
    }

    // ── Devuelto / SaldoADevolver pass-through ─────────────────────────────

    [Fact]
    public void Devuelto_WhenNoPeriodoActivo_ReturnsZero()
    {
        var state = new ContabilidadPageState();
        state.Devuelto.Should().Be(0m);
    }

    [Fact]
    public void SaldoADevolver_WhenNoPeriodoActivo_ReturnsZero()
    {
        var state = new ContabilidadPageState();
        state.SaldoADevolver.Should().Be(0m);
    }

    [Fact]
    public void Devuelto_ReflectsPeriodoActivoFullDevuelto()
    {
        var state = new ContabilidadPageState
        {
            PeriodoActivoFull = MakeFullDto(monto: 1000m, devuelto: 300m)
        };
        state.Devuelto.Should().Be(300m);
    }

    [Fact]
    public void SaldoADevolver_EqualsDiferenciaMinusDevuelto()
    {
        var state = new ContabilidadPageState
        {
            PeriodoActivoFull = MakeFullDto(monto: 1000m, devuelto: 200m)
        };
        // Diferencia = 1000 - 0 - 0 = 1000; SaldoADevolver = 1000 - 200 = 800
        state.SaldoADevolver.Should().Be(800m);
    }

    // ── CanCuadrar ─────────────────────────────────────────────────────────

    [Fact]
    public void CanCuadrar_WhenNoPeriodoActivo_ReturnsFalse()
    {
        var state = new ContabilidadPageState();
        state.CanCuadrar.Should().BeFalse();
    }

    [Fact]
    public void CanCuadrar_WhenAllVerifiedAndSaldoZero_ReturnsTrue()
    {
        // Transferida 1000, Compras 1000, Devuelto 0 → Diferencia=0, SaldoADevolver=0
        var state = new ContabilidadPageState
        {
            PeriodoActivoFull = MakeFullDto(monto: 1000m, devuelto: 0m,
                transVerificada: true, compras: 1, comprasVerificadas: true)
        };
        // Override totals to make saldo zero
        state.PeriodoActivoFull!.TotalCompras = 1000m;
        state.CanCuadrar.Should().BeTrue();
    }

    [Fact]
    public void CanCuadrar_WhenSaldoADevolverGreaterThanZero_ReturnsFalse()
    {
        // Transferida 1000, Compras 0, Devuelto 0 → Diferencia=1000, SaldoADevolver=1000
        var state = new ContabilidadPageState
        {
            PeriodoActivoFull = MakeFullDto(monto: 1000m, devuelto: 0m,
                transVerificada: true, compras: 0)
        };
        state.CanCuadrar.Should().BeFalse();
    }

    [Fact]
    public void CanCuadrar_WhenTransferenciaNotVerified_ReturnsFalse()
    {
        var state = new ContabilidadPageState
        {
            PeriodoActivoFull = MakeFullDto(monto: 1000m, devuelto: 1000m,
                transVerificada: false, compras: 0)
        };
        state.CanCuadrar.Should().BeFalse();
    }

    [Fact]
    public void CanCuadrar_WhenCompraNotVerified_ReturnsFalse()
    {
        var state = new ContabilidadPageState
        {
            PeriodoActivoFull = MakeFullDto(monto: 1000m, devuelto: 900m,
                transVerificada: true, compras: 1, comprasVerificadas: false)
        };
        // TotalCompras = 100, Diferencia = 900, Devuelto = 900 → SaldoADevolver = 0
        state.PeriodoActivoFull!.TotalCompras = 100m;
        state.PeriodoActivoFull.Devuelto = 900m;
        state.CanCuadrar.Should().BeFalse();
    }

    [Fact]
    public void CanCuadrar_WhenNoPeriodoActivoFull_ReturnsFalse()
    {
        var state = new ContabilidadPageState
        {
            PeriodoActivo = new AccountingPeriodDto
            {
                Id = 1, Name = "Test", Estado = AccountingPeriodEstado.Abierto
            }
        };
        state.CanCuadrar.Should().BeFalse();
    }
}
