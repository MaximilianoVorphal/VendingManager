namespace VendingManager.Tests.Services;

using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data.Repositories;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;
using VendingManager.Tests.TestData;

/// <summary>
/// Tests for GetConciliacionGlobalAsync in ContabilidadService.
/// Strict TDD: RED → GREEN → TRIANGULATE → REFACTOR per scenario.
/// </summary>
public class ContabilidadServiceTests_GetConciliacionGlobal : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ContabilidadService _service;

    public ContabilidadServiceTests_GetConciliacionGlobal()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"GetConc_TestDb_{Guid.NewGuid()}");
        var periodRepo = new AccountingPeriodRepository(_context);
        _service = new ContabilidadService(_context, periodRepo);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds 3 weekly periods for "TESTWORKER" (Jul 6–26) with 2 providers.
    /// Provider "Juan Perez" in W1/W2, provider "Coca-Cola" in W2/W3.
    /// Each period has 1 transferencia with linked compras + gastos via rendicion.
    /// </summary>
    private async Task SeedThreePeriodsAsync()
    {
        var baseDate = new DateTime(2026, 7, 6); // Monday

        for (int w = 0; w < 3; w++)
        {
            var period = new AccountingPeriod
            {
                Name = $"Semana {w + 1}",
                FechaInicio = baseDate.AddDays(w * 7),
                FechaFin = baseDate.AddDays(w * 7 + 6),
                Trabajador = "TESTWORKER",
                Estado = AccountingPeriodEstado.Abierto
            };
            _context.AccountingPeriods.Add(period);
            await _context.SaveChangesAsync();

            // Rendicion for this period
            var rendicion = new Rendicion
            {
                Trabajador = "TESTWORKER",
                FechaInicio = period.FechaInicio,
                Observaciones = $"Rendición S{w + 1}"
            };
            _context.Rendiciones.Add(rendicion);
            await _context.SaveChangesAsync();

            // Transferencia
            var transferencia = new Transferencia
            {
                Fecha = period.FechaInicio,
                Monto = 50000m + (w * 10000m),
                Descripcion = $"Transferencia S{w + 1}",
                Trabajador = "TESTWORKER",
                Estado = TransferenciaEstado.EnUso,
                RendicionId = rendicion.Id,
                PeriodoId = period.Id
            };
            _context.Transferencias.Add(transferencia);
            await _context.SaveChangesAsync();

            // Provider 1: "Juan Perez" in W1 and W2
            if (w == 0 || w == 1)
            {
                var compra = new Compra
                {
                    FechaCompra = period.FechaInicio.AddDays(1),
                    Proveedor = "Juan Perez",
                    NumeroDocumento = $"FAC-JUAN-S{w + 1}",
                    MontoTotal = w == 0 ? 15000m : 20000m,
                    Estado = "Registrada",
                    TipoFactura = "MERCADERIA",
                    Trabajador = "TESTWORKER",
                    TransferenciaId = transferencia.Id,
                    Verificada = w == 0
                };
                _context.Compras.Add(compra);
            }

            // Provider 2: "Coca-Cola" in W2 and W3
            if (w == 1 || w == 2)
            {
                var compra = new Compra
                {
                    FechaCompra = period.FechaInicio.AddDays(2),
                    Proveedor = "Coca-Cola",
                    NumeroDocumento = $"FAC-COCA-S{w + 1}",
                    MontoTotal = 12000m + (w * 1000m),
                    Estado = "Registrada",
                    TipoFactura = "MERCADERIA",
                    Trabajador = "TESTWORKER",
                    TransferenciaId = transferencia.Id,
                    Verificada = false
                };
                _context.Compras.Add(compra);
            }

            // Gastos via rendicion
            if (w == 0 || w == 2)
            {
                var gasto = new MovimientoCaja
                {
                    Fecha = period.FechaInicio.AddDays(3),
                    Descripcion = $"Gasto S{w + 1}",
                    Monto = -3000m,
                    Tipo = "GASTO",
                    Categoria = "GENERAL",
                    Trabajador = "TESTWORKER",
                    RendicionId = rendicion.Id
                };
                _context.MovimientosCaja.Add(gasto);
            }

            await _context.SaveChangesAsync();
        }
    }

    // ── TDD: Scenario 1 (RED) — Happy path ──────────────────────────────────

    [Fact]
    public async Task GetConciliacionGlobalAsync_HappyPath_ReturnsDtoWithExpectedStructure()
    {
        // Arrange
        await SeedThreePeriodsAsync();

        // Act
        var result = await _service.GetConciliacionGlobalAsync("TESTWORKER", default);

        // Assert — DTO structure
        result.Should().NotBeNull();
        result.Semanas.Should().HaveCount(3);
        result.Proveedores.Should().NotBeEmpty();
    }

    // ── TRIANGULATE: Happy path detail verification ─────────────────────────

    [Fact]
    public async Task GetConciliacionGlobalAsync_HappyPath_VerifyProviderGroupingAndTotals()
    {
        // Arrange
        await SeedThreePeriodsAsync();

        // Act
        var result = await _service.GetConciliacionGlobalAsync("TESTWORKER", default);

        // Assert — provider groups
        var juan = result.Proveedores.Should().ContainSingle(p => p.ProveedorSlug == "juanperez").Subject;
        juan.ProveedorNombre.Should().Be("Juan Perez");
        juan.TotalProveedor.Should().Be(35000m); // 15000 (W1) + 20000 (W2)

        var coca = result.Proveedores.Should().ContainSingle(p => p.ProveedorSlug == "cocacola").Subject;
        coca.ProveedorNombre.Should().Be("Coca-Cola");
        coca.TotalProveedor.Should().Be(27000m); // 13000 (W2) + 14000 (W3)

        // Assert — semana columns
        result.Semanas[0].TotalTransferido.Should().Be(50000m);
        result.Semanas[0].TotalCompras.Should().Be(15000m);
        result.Semanas[0].TotalGastos.Should().Be(3000m); // W1 has gasto

        result.Semanas[1].TotalTransferido.Should().Be(60000m);
        result.Semanas[1].TotalCompras.Should().Be(33000m); // 20000 (Juan) + 13000 (Coca)
        result.Semanas[1].TotalGastos.Should().Be(0m); // W2 has no gasto

        result.Semanas[2].TotalTransferido.Should().Be(70000m);
        result.Semanas[2].TotalCompras.Should().Be(14000m); // only Coca-Cola (12000 + 2000 for w=2)
        result.Semanas[2].TotalGastos.Should().Be(3000m); // W3 has gasto

        // Assert — celda estados
        // W1/Juan: compra Verificada=true → Justificado
        juan.Celdas[0].Estado.Should().Be("Justificado");
        juan.Celdas[0].Monto.Should().Be(15000m);

        // W2/Juan: compra Verificada=false → Pendiente
        juan.Celdas[1].Estado.Should().Be("Pendiente");
        juan.Celdas[1].Monto.Should().Be(20000m);

        // W3/Juan: no compras → Vacio
        juan.Celdas[2].Estado.Should().Be("Vacio");
        juan.Celdas[2].Monto.Should().Be(0m);

        // Assert — resumen
        result.Resumen.TotalTransferencias.Should().Be(180000m); // 50000+60000+70000
        result.Resumen.TotalCompras.Should().Be(62000m); // 15000+20000+13000+14000
        result.Resumen.TotalGastos.Should().Be(6000m); // 3000+3000
        result.Resumen.SaldoTotal.Should().Be(112000m); // 180000-62000-6000
        result.Resumen.SemanasTotales.Should().Be(3);
        result.Resumen.SemanasVerificadas.Should().Be(0); // W2 has unverified in Juan

        // Assert — saldo inicial
        result.SaldoInicial.Should().Be(0m);
    }

    // ── TRIANGULATE: Empty worker ────────────────────────────────────────────

    [Fact]
    public async Task GetConciliacionGlobalAsync_EmptyWorker_ReturnsEmptyDto()
    {
        // Act
        var result = await _service.GetConciliacionGlobalAsync("NONEXISTENT", default);

        // Assert — empty DTO, not null, not 404
        result.Should().NotBeNull();
        result.Semanas.Should().BeEmpty();
        result.Proveedores.Should().BeEmpty();
        result.Resumen.TotalTransferencias.Should().Be(0m);
        result.Resumen.TotalCompras.Should().Be(0m);
        result.Resumen.SemanasTotales.Should().Be(0);
        result.SaldoInicial.Should().Be(0m);
    }

    // ── TRIANGULATE: Null PeriodoId filtering ────────────────────────────────

    private async Task SeedWithNullPeriodoTransferenciaAsync()
    {
        var period = new AccountingPeriod
        {
            Name = "Semana 1",
            FechaInicio = new DateTime(2026, 7, 6),
            FechaFin = new DateTime(2026, 7, 12),
            Trabajador = "WORKER_NULL",
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        // Rendicion
        var rendicion = new Rendicion
        {
            Trabajador = "WORKER_NULL",
            FechaInicio = period.FechaInicio
        };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        // Transferencia WITH PeriodoId → should be included
        var transferenciaValida = new Transferencia
        {
            Fecha = period.FechaInicio,
            Monto = 50000m,
            Trabajador = "WORKER_NULL",
            Estado = TransferenciaEstado.EnUso,
            RendicionId = rendicion.Id,
            PeriodoId = period.Id
        };
        _context.Transferencias.Add(transferenciaValida);

        // Transferencia with PeriodoId = null → should be EXCLUDED
        var transferenciaInvalida = new Transferencia
        {
            Fecha = period.FechaInicio,
            Monto = 99999m,
            Trabajador = "WORKER_NULL",
            Estado = TransferenciaEstado.Pendiente,
            RendicionId = rendicion.Id,
            PeriodoId = null // legacy — no period
        };
        _context.Transferencias.Add(transferenciaInvalida);
        await _context.SaveChangesAsync();

        // Compra linked to valid transferencia
        _context.Compras.Add(new Compra
        {
            FechaCompra = period.FechaInicio.AddDays(1),
            Proveedor = "Valid Provider",
            NumeroDocumento = "FAC-VALID",
            MontoTotal = 10000m,
            Estado = "Registrada",
            TipoFactura = "MERCADERIA",
            Trabajador = "WORKER_NULL",
            TransferenciaId = transferenciaValida.Id
        });

        // Compra linked to null-periodo transferencia — should be excluded
        _context.Compras.Add(new Compra
        {
            FechaCompra = period.FechaInicio.AddDays(1),
            Proveedor = "Null Period Provider",
            NumeroDocumento = "FAC-NULL",
            MontoTotal = 5000m,
            Estado = "Registrada",
            TipoFactura = "MERCADERIA",
            Trabajador = "WORKER_NULL",
            TransferenciaId = transferenciaInvalida.Id
        });
        await _context.SaveChangesAsync();

        // Gasto via valid rendicion
        _context.MovimientosCaja.Add(new MovimientoCaja
        {
            Fecha = period.FechaInicio.AddDays(2),
            Descripcion = "Gasto válido",
            Monto = -2000m,
            Tipo = "GASTO",
            Categoria = "GENERAL",
            Trabajador = "WORKER_NULL",
            RendicionId = rendicion.Id
        });
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetConciliacionGlobalAsync_NullPeriodoTransferencias_AreFilteredOut()
    {
        // Arrange
        await SeedWithNullPeriodoTransferenciaAsync();

        // Act
        var result = await _service.GetConciliacionGlobalAsync("WORKER_NULL", default);

        // Assert — only valid periodo data included
        result.Semanas.Should().HaveCount(1);
        result.Semanas[0].TotalTransferido.Should().Be(50000m); // only valid, 99999 excluded
        result.Semanas[0].TotalCompras.Should().Be(10000m); // only valid compra, 5000 excluded
        result.Semanas[0].TotalGastos.Should().Be(2000m);

        result.Proveedores.Should().ContainSingle(p => p.ProveedorSlug == "validprovider");
        result.Proveedores.Should().NotContain(p => p.ProveedorSlug == "nullperiodprovider");

        result.Resumen.TotalTransferencias.Should().Be(50000m);
        result.Resumen.TotalCompras.Should().Be(10000m);
    }

    // ── TRIANGULATE: Slug normalization ──────────────────────────────────────

    [Fact]
    public async Task GetConciliacionGlobalAsync_SlugNormalization_GroupsSimilarNames()
    {
        // Arrange
        var period = new AccountingPeriod
        {
            Name = "Semana 1",
            FechaInicio = new DateTime(2026, 7, 6),
            FechaFin = new DateTime(2026, 7, 12),
            Trabajador = "SLUGWORKER",
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        var rendicion = new Rendicion { Trabajador = "SLUGWORKER", FechaInicio = period.FechaInicio };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var transferencia = new Transferencia
        {
            Fecha = period.FechaInicio,
            Monto = 100000m,
            Trabajador = "SLUGWORKER",
            Estado = TransferenciaEstado.EnUso,
            RendicionId = rendicion.Id,
            PeriodoId = period.Id
        };
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();

        // Same provider with different accent/diacritic forms
        _context.Compras.Add(new Compra
        {
            FechaCompra = period.FechaInicio,
            Proveedor = "Juan Pérez",     // accented e
            MontoTotal = 10000m,
            Estado = "Registrada",
            TransferenciaId = transferencia.Id,
            Trabajador = "SLUGWORKER",
            TipoFactura = "MERCADERIA",
            NumeroDocumento = "FAC-001"
        });
        _context.Compras.Add(new Compra
        {
            FechaCompra = period.FechaInicio,
            Proveedor = "Juan Perez",     // no accent — different string, same slug
            MontoTotal = 15000m,
            Estado = "Registrada",
            TransferenciaId = transferencia.Id,
            Trabajador = "SLUGWORKER",
            TipoFactura = "MERCADERIA",
            NumeroDocumento = "FAC-002"
        });
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetConciliacionGlobalAsync("SLUGWORKER", default);

        // Assert — both compras grouped under same slug "juanperez"
        result.Proveedores.Should().ContainSingle(p => p.ProveedorSlug == "juanperez");
        var juan = result.Proveedores.Single(p => p.ProveedorSlug == "juanperez");
        juan.TotalProveedor.Should().Be(25000m); // 10000 + 15000
        juan.Celdas[0].Comprobantes.Should().HaveCount(2);
    }

    // ── TRIANGULATE: ComprobanteItemDto in cell ──────────────────────────────

    [Fact]
    public async Task GetConciliacionGlobalAsync_CellComprobantes_HaveExpectedItems()
    {
        // Arrange
        await SeedThreePeriodsAsync();

        // Act
        var result = await _service.GetConciliacionGlobalAsync("TESTWORKER", default);

        // Assert — Juan Perez W1: 1 comprobante
        var juan = result.Proveedores.Single(p => p.ProveedorSlug == "juanperez");
        var w1Cell = juan.Celdas[0];
        w1Cell.Comprobantes.Should().HaveCount(1);
        w1Cell.Comprobantes[0].Tipo.Should().Be("Compra");
        w1Cell.Comprobantes[0].NumeroDocumento.Should().Be("FAC-JUAN-S1");
        w1Cell.Comprobantes[0].Monto.Should().Be(15000m);
        w1Cell.Comprobantes[0].Verificada.Should().BeTrue();

        // W3/Juan: Vacio state, no comprobantes
        var w3Cell = juan.Celdas[2];
        w3Cell.Comprobantes.Should().BeEmpty();
        w3Cell.Estado.Should().Be("Vacio");
        w3Cell.Monto.Should().Be(0m);
    }
}
