namespace VendingManager.Tests.Components;

using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;
using VendingManager.Web.Pages.Contabilidad.State;

public class ContabilidadPageStateTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static RendicionFullDto MakeFullDto(
        decimal monto = 1000m,
        decimal devuelto = 0m,
        bool transVerificada = true,
        int compras = 0,
        bool comprasVerificadas = true)
    {
        var totalCompras = compras * 100m;
        var diferencia = monto - totalCompras;

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

        return new RendicionFullDto
        {
            Id = 1,
            Trabajador = "Worker A",
            FechaInicio = DateTime.Today,
            Estado = RendicionEstado.Abierta,
            Transferencias = new List<TransferenciaDto> { transferencia },
            Resumen = new RendicionResumenDto
            {
                RendicionId = 1,
                Transferido = monto,
                TotalCompras = totalCompras,
                TotalGastos = 0m,
                Diferencia = diferencia,
                Devuelto = devuelto
            }
        };
    }

    // ── Devuelto / SaldoADevolver pass-through ─────────────────────────────

    [Fact]
    public void Devuelto_WhenNoRendicionActiva_ReturnsZero()
    {
        var state = new ContabilidadPageState();
        state.Devuelto.Should().Be(0m);
    }

    [Fact]
    public void SaldoADevolver_WhenNoRendicionActiva_ReturnsZero()
    {
        var state = new ContabilidadPageState();
        state.SaldoADevolver.Should().Be(0m);
    }

    [Fact]
    public void Devuelto_ReflectsResumenDevuelto()
    {
        var state = new ContabilidadPageState
        {
            RendicionActivaFull = MakeFullDto(monto: 1000m, devuelto: 300m)
        };
        state.Devuelto.Should().Be(300m);
    }

    [Fact]
    public void SaldoADevolver_EqualsDiferenciaMinusDevuelto()
    {
        var state = new ContabilidadPageState
        {
            RendicionActivaFull = MakeFullDto(monto: 1000m, devuelto: 200m)
        };
        // Diferencia = 1000 - 0 - 0 = 1000; SaldoADevolver = 1000 - 200 = 800
        state.SaldoADevolver.Should().Be(800m);
    }

    // ── CanCuadrar ─────────────────────────────────────────────────────────

    [Fact]
    public void CanCuadrar_WhenNoRendicionActiva_ReturnsFalse()
    {
        var state = new ContabilidadPageState();
        state.CanCuadrar.Should().BeFalse();
    }

    [Fact]
    public void CanCuadrar_WhenAllVerifiedAndSaldoZero_ReturnsTrue()
    {
        // 10 compras × $100 = $1000 = monto → Diferencia computed locally = 0 → SaldoADevolver = 0
        var state = new ContabilidadPageState
        {
            RendicionActivaFull = MakeFullDto(monto: 1000m, devuelto: 0m,
                transVerificada: true, compras: 10, comprasVerificadas: true)
        };
        state.CanCuadrar.Should().BeTrue();
    }

    [Fact]
    public void CanCuadrar_WhenSaldoADevolverGreaterThanZero_ReturnsFalse()
    {
        // Diferencia = 1000 - 0 = 1000; Devuelto = 0 → SaldoADevolver = 1000
        var state = new ContabilidadPageState
        {
            RendicionActivaFull = MakeFullDto(monto: 1000m, devuelto: 0m,
                transVerificada: true, compras: 0)
        };
        state.CanCuadrar.Should().BeFalse();
    }

    [Fact]
    public void CanCuadrar_WhenTransferenciaNotVerified_ReturnsFalse()
    {
        var state = new ContabilidadPageState
        {
            RendicionActivaFull = MakeFullDto(monto: 1000m, devuelto: 1000m,
                transVerificada: false, compras: 0)
        };
        state.CanCuadrar.Should().BeFalse();
    }

    [Fact]
    public void CanCuadrar_WhenCompraNotVerified_ReturnsFalse()
    {
        var state = new ContabilidadPageState
        {
            RendicionActivaFull = MakeFullDto(monto: 1000m, devuelto: 900m,
                transVerificada: true, compras: 1, comprasVerificadas: false)
        };
        // Diferencia = 900, Devuelto = 900 → SaldoADevolver = 0, but compra not verified
        state.RendicionActivaFull!.Resumen.Diferencia = 900m;
        state.CanCuadrar.Should().BeFalse();
    }

    [Fact]
    public void CanCuadrar_WhenNoRendicionActivaFull_ReturnsFalse()
    {
        var state = new ContabilidadPageState
        {
            RendicionActiva = new RendicionDto
            {
                Id = 1, Trabajador = "Test", Estado = RendicionEstado.Abierta
            }
        };
        state.CanCuadrar.Should().BeFalse();
    }

    [Fact]
    public void CanCuadrar_WhenRendicionHasNoTransferencias_ReturnsFalse()
    {
        // Empty rendicion: saldo is 0 and verify checks pass vacuously,
        // but there is nothing to reconcile — must NOT be cuadrable.
        var state = new ContabilidadPageState
        {
            RendicionActivaFull = new RendicionFullDto
            {
                Id = 1,
                Trabajador = "Test",
                FechaInicio = DateTime.Today,
                Estado = RendicionEstado.Abierta,
                Transferencias = new List<TransferenciaDto>(),
                Resumen = new RendicionResumenDto
                {
                    RendicionId = 1,
                    Transferido = 0m,
                    TotalCompras = 0m,
                    TotalGastos = 0m,
                    Diferencia = 0m,
                    Devuelto = 0m
                }
            }
        };
        state.CanCuadrar.Should().BeFalse();
    }

    [Fact]
    public void CanCuadrar_WhenSaldoADevolverNegative_ReturnsFalse()
    {
        // Over-returned: Diferencia 1000, Devuelto 1500 → SaldoADevolver = -500
        var state = new ContabilidadPageState
        {
            RendicionActivaFull = MakeFullDto(monto: 1000m, devuelto: 1500m,
                transVerificada: true, compras: 0)
        };
        state.SaldoADevolver.Should().Be(-500m);
        state.CanCuadrar.Should().BeFalse();
    }
}
