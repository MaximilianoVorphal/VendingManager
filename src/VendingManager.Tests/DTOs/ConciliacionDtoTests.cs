namespace VendingManager.Tests.DTOs;

using VendingManager.Shared.DTOs;

/// <summary>
/// TASK-06 — Unit tests for the reconciliation projection DTO extensions.
/// Written RED-first per Strict TDD.
/// </summary>
public class ConciliacionDtoTests
{
    // ── AccountingPeriodDto.SaldoADevolver ────────────────────────────────────

    [Fact]
    public void AccountingPeriodDto_NoDevuelto_SaldoADevolverEqualsDiferencia()
    {
        // Diferencia = TotalTransferido - TotalCompras - TotalGastos = 1000 - 300 - 200 = 500
        var dto = new AccountingPeriodDto
        {
            TotalTransferido = 1000m,
            TotalCompras = 300m,
            TotalGastos = 200m,
            Devuelto = 0m
        };

        dto.Diferencia.Should().Be(500m);
        dto.SaldoADevolver.Should().Be(500m);
    }

    [Fact]
    public void AccountingPeriodDto_PartialDevuelto_SaldoADevolverIsReduced()
    {
        var dto = new AccountingPeriodDto
        {
            TotalTransferido = 1000m,
            TotalCompras = 300m,
            TotalGastos = 200m,
            Devuelto = 200m
        };

        dto.Diferencia.Should().Be(500m);
        dto.SaldoADevolver.Should().Be(300m);
    }

    [Fact]
    public void AccountingPeriodDto_FullDevuelto_SaldoADevolverIsZero()
    {
        var dto = new AccountingPeriodDto
        {
            TotalTransferido = 1000m,
            TotalCompras = 300m,
            TotalGastos = 200m,
            Devuelto = 500m
        };

        dto.Diferencia.Should().Be(500m);
        dto.SaldoADevolver.Should().Be(0m);
    }

    [Fact]
    public void AccountingPeriodDto_Diferencia_IsUnaffectedByDevuelto()
    {
        // Ensures that changing Devuelto does NOT change Diferencia (single-source rule)
        var dto = new AccountingPeriodDto
        {
            TotalTransferido = 800m,
            TotalCompras = 200m,
            TotalGastos = 100m,
            Devuelto = 499m
        };

        var diferenciaBeforeChange = dto.Diferencia;
        dto.Devuelto = 0m;
        var diferenciaAfterChange = dto.Diferencia;

        diferenciaBeforeChange.Should().Be(500m);
        diferenciaAfterChange.Should().Be(500m);
    }

    // ── RendicionResumenDto.SaldoADevolver ────────────────────────────────────

    [Fact]
    public void RendicionResumenDto_NoDevuelto_SaldoADevolverEqualsDiferencia()
    {
        var dto = new RendicionResumenDto
        {
            Diferencia = 300m,
            Devuelto = 0m
        };

        dto.SaldoADevolver.Should().Be(300m);
    }

    [Fact]
    public void RendicionResumenDto_PartialDevuelto_SaldoADevolverIsReduced()
    {
        var dto = new RendicionResumenDto
        {
            Diferencia = 300m,
            Devuelto = 100m
        };

        dto.SaldoADevolver.Should().Be(200m);
    }

    [Fact]
    public void RendicionResumenDto_FullDevuelto_SaldoADevolverIsZero()
    {
        var dto = new RendicionResumenDto
        {
            Diferencia = 300m,
            Devuelto = 300m
        };

        dto.SaldoADevolver.Should().Be(0m);
    }

    // ── TransferenciaDto new fields ──────────────────────────────────────────

    [Fact]
    public void TransferenciaDto_HasVerificadaFalseByDefault()
    {
        var dto = new TransferenciaDto();
        dto.Verificada.Should().BeFalse();
    }

    [Fact]
    public void TransferenciaDto_HasComprobanteFalseByDefault()
    {
        var dto = new TransferenciaDto();
        dto.HasComprobante.Should().BeFalse();
        dto.ComprobanteImagenFileName.Should().BeNull();
    }

    // ── CompraDto new fields ──────────────────────────────────────────────────

    [Fact]
    public void CompraDto_HasVerificadaFalseByDefault()
    {
        var dto = new CompraDto();
        dto.Verificada.Should().BeFalse();
    }

    // ── DevolucionDto ─────────────────────────────────────────────────────────

    [Fact]
    public void DevolucionDto_CanBeConstructedWithAllFields()
    {
        var dto = new DevolucionDto
        {
            Id = 1,
            Monto = 250m,
            Fecha = new DateTime(2026, 6, 20),
            Trabajador = "Ana Torres",
            Observaciones = "Devolución parcial"
        };

        dto.Id.Should().Be(1);
        dto.Monto.Should().Be(250m);
        dto.Trabajador.Should().Be("Ana Torres");
        dto.Observaciones.Should().Be("Devolución parcial");
    }

    // ── RegistrarDevolucionRequest ────────────────────────────────────────────

    [Fact]
    public void RegistrarDevolucionRequest_CanBeConstructedWithRequiredFields()
    {
        var req = new RegistrarDevolucionRequest
        {
            PeriodoId = 5,
            Trabajador = "Carlos",
            Monto = 500m,
            Fecha = DateTime.Today
        };

        req.PeriodoId.Should().Be(5);
        req.RendicionId.Should().BeNull();
        req.Trabajador.Should().Be("Carlos");
        req.Monto.Should().Be(500m);
    }
}
