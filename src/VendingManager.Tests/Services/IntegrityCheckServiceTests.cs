namespace VendingManager.Tests.Services;

using VendingManager.Infrastructure.Services;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;
using VendingManager.Tests.TestData;

/// <summary>
/// Service-layer TDD tests for Data Integrity Audit (20 tasks).
/// Each test maps to a specific task from the SDD.
/// EF InMemory is seeded per test via unique database names.
/// </summary>
public class IntegrityCheckServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IntegrityCheckService _service;

    public IntegrityCheckServiceTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"IntegrityCheckTestDb_{Guid.NewGuid()}");
        _service = new IntegrityCheckService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Task 5.1: Check #3 over-allocated transfers
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Check3a_FindsOverAllocatedTransfer_EmitsError()
    {
        // Arrange — Transferencia Monto=1000, Compra sum=1100 (exceeds by 100)
        var t = new Transferencia
        {
            Monto = 1000m,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.EnUso
        };
        _context.Transferencias.Add(t);
        await _context.SaveChangesAsync();

        _context.Compras.AddRange(
            new Compra { MontoTotal = 600m, TransferenciaId = t.Id, Proveedor = "Prov A" },
            new Compra { MontoTotal = 500m, TransferenciaId = t.Id, Proveedor = "Prov B" });
        await _context.SaveChangesAsync();

        // Act
        var results = await _service.RunAllChecksAsync(default);

        // Assert — finds the over-allocated transfer
        var check3a = results.FirstOrDefault(r => r.CheckType.StartsWith("3A"));
        check3a.Should().NotBeNull();
        check3a!.Severity.Should().Be(CheckSeverity.Error);
        check3a.DetailEntries.Should().ContainSingle(d =>
            d.TransferenciaId == t.Id &&
            d.Diferencia == -100m && // 1000 - 1100 = -100
            d.Mensaje!.Contains("excede"));
    }

    [Fact]
    public async Task Check3a_SkipsWithinBudgetTransfer_NoEntry()
    {
        // Arrange — Transferencia Monto=1000, Compra sum=1000 (exactly equal)
        var t = new Transferencia
        {
            Monto = 1000m,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.EnUso
        };
        _context.Transferencias.Add(t);
        await _context.SaveChangesAsync();

        _context.Compras.Add(new Compra { MontoTotal = 1000m, TransferenciaId = t.Id, Proveedor = "Prov A" });
        await _context.SaveChangesAsync();

        // Act
        var results = await _service.RunAllChecksAsync(default);

        // Assert — no entries for this transferencia
        var check3a = results.FirstOrDefault(r => r.CheckType.StartsWith("3A"));
        check3a?.DetailEntries.Should().BeNullOrEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Task 5.2: Check #3 orphan Compras (TransferenciaId IS NULL)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Check3b_OrphanCompra_EmitsWarn()
    {
        // Arrange — Compra with null TransferenciaId
        _context.Compras.Add(new Compra
        {
            Proveedor = "Orphan Prov",
            MontoTotal = 500m,
            TransferenciaId = null,
            Trabajador = "Worker B"
        });
        await _context.SaveChangesAsync();

        // Act
        var results = await _service.RunAllChecksAsync(default);

        // Assert
        var check3b = results.FirstOrDefault(r => r.CheckType.StartsWith("3B"));
        check3b.Should().NotBeNull();
        check3b!.Severity.Should().Be(CheckSeverity.Warn);
        check3b.DetailEntries.Should().ContainSingle(d =>
            d.CompraId.HasValue &&
            d.MontoTotal == 500m);
    }

    [Fact]
    public async Task Check3b_SkipsLinkedCompra_NoEntry()
    {
        // Arrange — Compra with valid TransferenciaId
        var t = new Transferencia
        {
            Monto = 500m,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.EnUso
        };
        _context.Transferencias.Add(t);
        await _context.SaveChangesAsync();

        _context.Compras.Add(new Compra
        {
            Proveedor = "Linked Prov",
            MontoTotal = 300m,
            TransferenciaId = t.Id
        });
        await _context.SaveChangesAsync();

        // Act
        var results = await _service.RunAllChecksAsync(default);

        // Assert — no orphan entries
        var check3b = results.FirstOrDefault(r => r.CheckType.StartsWith("3B"));
        check3b?.DetailEntries.Should().BeNullOrEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Task 5.3: Check #3 auto-conciliated without Compras
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Check3c_ConciliadoWithoutCompras_EmitsInfo()
    {
        // Arrange — Transferencia Estado=Conciliado, no Compras
        _context.Transferencias.Add(new Transferencia
        {
            Monto = 1000m,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.Conciliado
        });
        await _context.SaveChangesAsync();

        // Act
        var results = await _service.RunAllChecksAsync(default);

        // Assert
        var check3c = results.FirstOrDefault(r => r.CheckType.StartsWith("3C"));
        check3c.Should().NotBeNull();
        check3c!.Severity.Should().Be(CheckSeverity.Info);
        check3c.DetailEntries.Should().ContainSingle();
    }

    [Fact]
    public async Task Check3c_PendienteWithoutCompras_NoEntry()
    {
        // Arrange — Transferencia Estado=Pendiente, no Compras (should NOT be flagged)
        _context.Transferencias.Add(new Transferencia
        {
            Monto = 500m,
            Trabajador = "Worker B",
            Estado = TransferenciaEstado.Pendiente
        });
        await _context.SaveChangesAsync();

        // Act
        var results = await _service.RunAllChecksAsync(default);

        // Assert — no info entries for Pendiente
        var check3c = results.FirstOrDefault(r => r.CheckType.StartsWith("3C"));
        check3c?.DetailEntries.Should().BeNullOrEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Task 5.4: Check #7 closed Rendicion with non-zero saldo
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Check7a_ClosedRendicionNonZeroSaldo_EmitsError()
    {
        // Arrange — closed rendicion with SaldoADevolver = 1000 - 400 = 600 ≠ 0
        var rendicion = new Rendicion
        {
            Trabajador = "Worker A",
            Estado = RendicionEstado.Cerrada
        };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var t = new Transferencia
        {
            Monto = 1000m,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.Conciliado,
            RendicionId = rendicion.Id
        };
        _context.Transferencias.Add(t);
        await _context.SaveChangesAsync();

        _context.Compras.Add(new Compra
        {
            Proveedor = "Prov A",
            MontoTotal = 400m,
            TransferenciaId = t.Id
        });
        await _context.SaveChangesAsync();

        // Act
        var results = await _service.RunAllChecksAsync(default);

        // Assert
        var check7a = results.FirstOrDefault(r => r.CheckType.StartsWith("7A"));
        check7a.Should().NotBeNull();
        check7a!.Severity.Should().Be(CheckSeverity.Error);
        check7a.DetailEntries.Should().ContainSingle(d =>
            d.RendicionId == rendicion.Id &&
            d.SaldoADevolver == 600m);
    }

    [Fact]
    public async Task Check7a_ClosedRendicionZeroSaldo_NoEntry()
    {
        // Arrange — closed rendicion with SaldoADevolver = 0 (settled)
        var rendicion = new Rendicion
        {
            Trabajador = "Worker B",
            Estado = RendicionEstado.Cerrada
        };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var t = new Transferencia
        {
            Monto = 500m,
            Trabajador = "Worker B",
            Estado = TransferenciaEstado.Conciliado,
            RendicionId = rendicion.Id
        };
        _context.Transferencias.Add(t);
        await _context.SaveChangesAsync();

        _context.Compras.Add(new Compra
        {
            Proveedor = "Prov B",
            MontoTotal = 500m,
            TransferenciaId = t.Id
        });
        await _context.SaveChangesAsync();

        // Act
        var results = await _service.RunAllChecksAsync(default);

        // Assert — no entry for settled rendicion
        var check7a = results.FirstOrDefault(r => r.CheckType.StartsWith("7A"));
        check7a?.DetailEntries.Should().BeNullOrEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Task 5.5: Check #7 cross-linked Transferencia
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Check7b_CrossLinkedTransferencia_EmitsWarn()
    {
        // Arrange — Transferencia with both RendicionId AND PeriodoId non-null
        var rendicion = new Rendicion { Trabajador = "Worker A" };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var period = new AccountingPeriod
        {
            Name = "Test Period",
            FechaInicio = DateTime.Today,
            FechaFin = DateTime.Today.AddMonths(1),
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        _context.Transferencias.Add(new Transferencia
        {
            Monto = 500m,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.EnUso,
            RendicionId = rendicion.Id,
            PeriodoId = period.Id
        });
        await _context.SaveChangesAsync();

        // Act
        var results = await _service.RunAllChecksAsync(default);

        // Assert
        var check7b = results.FirstOrDefault(r => r.CheckType.StartsWith("7B"));
        check7b.Should().NotBeNull();
        check7b!.Severity.Should().Be(CheckSeverity.Warn);
        check7b.DetailEntries.Should().ContainSingle(d =>
            d.TransferenciaId.HasValue &&
            d.Mensaje!.Contains("RendicionId") &&
            d.Mensaje.Contains("PeriodoId"));
    }

    [Fact]
    public async Task Check7b_SingleForeignKey_NoEntry()
    {
        // Arrange — Transferencia with only RendicionId (no PeriodoId)
        var rendicion = new Rendicion { Trabajador = "Worker B" };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        _context.Transferencias.Add(new Transferencia
        {
            Monto = 300m,
            Trabajador = "Worker B",
            Estado = TransferenciaEstado.EnUso,
            RendicionId = rendicion.Id,
            PeriodoId = null
        });
        await _context.SaveChangesAsync();

        // Act
        var results = await _service.RunAllChecksAsync(default);

        // Assert — no cross-link entry
        var check7b = results.FirstOrDefault(r => r.CheckType.StartsWith("7B"));
        check7b?.DetailEntries.Should().BeNullOrEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Task 5.6: Check #7 open Rendicion with negative saldo
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Check7c_OpenRendicionNegativeSaldo_EmitsError()
    {
        // Arrange — open rendicion with negative saldo: 500 - 600 = -100 < 0
        var rendicion = new Rendicion
        {
            Trabajador = "Worker A",
            Estado = RendicionEstado.Abierta
        };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var t = new Transferencia
        {
            Monto = 500m,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.EnUso,
            RendicionId = rendicion.Id
        };
        _context.Transferencias.Add(t);
        await _context.SaveChangesAsync();

        _context.Compras.Add(new Compra
        {
            Proveedor = "Prov A",
            MontoTotal = 600m,
            TransferenciaId = t.Id
        });
        await _context.SaveChangesAsync();

        // Act
        var results = await _service.RunAllChecksAsync(default);

        // Assert
        var check7c = results.FirstOrDefault(r => r.CheckType.StartsWith("7C"));
        check7c.Should().NotBeNull();
        check7c!.Severity.Should().Be(CheckSeverity.Error);
        check7c.DetailEntries.Should().ContainSingle(d =>
            d.RendicionId == rendicion.Id &&
            d.SaldoADevolver == -100m);
    }

    [Fact]
    public async Task Check7c_OpenRendicionPositiveSaldo_NoEntry()
    {
        // Arrange — open rendicion with positive saldo: 1000 - 400 = 600 > 0 (no problem)
        var rendicion = new Rendicion
        {
            Trabajador = "Worker B",
            Estado = RendicionEstado.Abierta
        };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var t = new Transferencia
        {
            Monto = 1000m,
            Trabajador = "Worker B",
            Estado = TransferenciaEstado.EnUso,
            RendicionId = rendicion.Id
        };
        _context.Transferencias.Add(t);
        await _context.SaveChangesAsync();

        _context.Compras.Add(new Compra
        {
            Proveedor = "Prov B",
            MontoTotal = 400m,
            TransferenciaId = t.Id
        });
        await _context.SaveChangesAsync();

        // Act
        var results = await _service.RunAllChecksAsync(default);

        // Assert — no entry for positive saldo
        var check7c = results.FirstOrDefault(r => r.CheckType.StartsWith("7C"));
        check7c?.DetailEntries.Should().BeNullOrEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Task 5.7: Empty database — no data seeded
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EmptyDatabase_ReturnsEmptyList_NotNull()
    {
        // Arrange — no data seeded (empty DB)

        // Act
        var results = await _service.RunAllChecksAsync(default);

        // Assert — not null, empty list
        results.Should().NotBeNull();
        results.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Edge case: Check #7a with Gastos (EsGastoReal filter)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Check7a_ClosedRendicion_ExcludesRetiroCapitalFromGastos()
    {
        // Arrange — rendicion with structural gasto (RETIRO_CAPITAL) excluded from sum
        // Transferencia=1000, Compra=400, RETIRO_CAPITAL=-1000 → should not reduce saldo
        // SaldoADevolver = 1000 - 400 - 0 = 600 (structural gasto excluded)
        var rendicion = new Rendicion
        {
            Trabajador = "Worker A",
            Estado = RendicionEstado.Cerrada
        };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var t = new Transferencia
        {
            Monto = 1000m,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.Conciliado,
            RendicionId = rendicion.Id
        };
        _context.Transferencias.Add(t);
        await _context.SaveChangesAsync();

        _context.Compras.Add(new Compra
        {
            Proveedor = "Prov A",
            MontoTotal = 400m,
            TransferenciaId = t.Id
        });

        _context.MovimientosCaja.Add(new MovimientoCaja
        {
            Fecha = DateTime.Today,
            Descripcion = "Retiro de capital",
            Monto = -1000m,
            Tipo = "RETIRO",
            Categoria = "RETIRO_CAPITAL",
            RendicionId = rendicion.Id
        });
        await _context.SaveChangesAsync();

        // Act
        var results = await _service.RunAllChecksAsync(default);

        // Assert — RETIRO_CAPITAL excluded, saldo = 1000 - 400 = 600
        var check7a = results.FirstOrDefault(r => r.CheckType.StartsWith("7A"));
        check7a.Should().NotBeNull();
        var entry = check7a!.DetailEntries.Should().ContainSingle().Subject;
        entry.SaldoADevolver.Should().Be(600m);
    }
}
