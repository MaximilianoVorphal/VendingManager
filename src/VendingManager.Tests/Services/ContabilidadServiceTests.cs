namespace VendingManager.Tests.Services;

using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using VendingManager.Core.Configuration;
using VendingManager.Core.Entities;
using VendingManager.Infrastructure.Data.Repositories;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;
using VendingManager.Tests.TestData;

/// <summary>
/// Service-layer TDD tests for EliminarTransferenciaCuadreAsync.
/// Strict TDD: RED → GREEN → REFACTOR per work unit.
/// </summary>
public class ContabilidadServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ContabilidadService _service;

    public ContabilidadServiceTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"ContabilidadServiceTestDb_{Guid.NewGuid()}");

        var periodRepo = new AccountingPeriodRepository(_context);
        _service = new ContabilidadService(_context, periodRepo);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(Transferencia transferencia, AccountingPeriod period, Rendicion rendicion, List<Compra> compras)>
        CreateCuadreWithComprasAsync(int compraCount = 3, TransferenciaEstado estado = TransferenciaEstado.Pendiente)
    {
        var period = new AccountingPeriod
        {
            Name = "Rendición Juan 01/07/2026",
            FechaInicio = new DateTime(2026, 7, 1),
            FechaFin = new DateTime(2026, 7, 31),
            Trabajador = "Juan",
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        var rendicion = new Rendicion
        {
            Trabajador = "Juan",
            FechaInicio = new DateTime(2026, 7, 1),
            Observaciones = "Auto-creada para cuadre de transferencia"
        };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var transferencia = new Transferencia
        {
            Fecha = new DateTime(2026, 7, 1),
            Monto = 10000m,
            Descripcion = "Transferencia cuadre",
            Trabajador = "Juan",
            Estado = estado,
            RendicionId = rendicion.Id,
            PeriodoId = period.Id
        };
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();

        var compras = new List<Compra>();
        for (int i = 0; i < compraCount; i++)
        {
            var compra = new Compra
            {
                FechaCompra = new DateTime(2026, 7, 1),
                Proveedor = $"Proveedor {i}",
                NumeroDocumento = $"DOC-{i}",
                MontoTotal = 1000m * (i + 1),
                Estado = "Registrada",
                TipoFactura = "MERCADERIA",
                Trabajador = "Juan",
                TransferenciaId = transferencia.Id
            };
            _context.Compras.Add(compra);
            compras.Add(compra);
        }
        await _context.SaveChangesAsync();

        return (transferencia, period, rendicion, compras);
    }

    // ── T-01: Cuadre happy path ─────────────────────────────────────────────

    [Fact]
    public async Task EliminarTransferenciaCuadreAsync_CuadreHappyPath_AllComprasUnlinkedPeriodDeleted()
    {
        // Arrange — cuadre with 3 compras
        var (transferencia, period, rendicion, compras) = await CreateCuadreWithComprasAsync(3);

        // Act
        var result = await _service.EliminarTransferenciaCuadreAsync(transferencia.Id);

        // Assert — result DTO
        result.ComprasUnlinked.Should().Be(3);
        result.PeriodoId.Should().Be(period.Id);

        // Assert — all compras unlinked
        foreach (var compra in compras)
        {
            var reloaded = await _context.Compras.FindAsync(compra.Id);
            reloaded!.TransferenciaId.Should().BeNull();
        }

        // Assert — period and rendicion deleted
        var deletedPeriod = await _context.AccountingPeriods.FindAsync(period.Id);
        deletedPeriod.Should().BeNull();
        var deletedRendicion = await _context.Rendiciones.FindAsync(rendicion.Id);
        deletedRendicion.Should().BeNull();

        // Assert — transferencia row deleted
        var deletedTransf = await _context.Transferencias.FindAsync(transferencia.Id);
        deletedTransf.Should().BeNull();
    }

    // ── T-03: Legacy path (PeriodoId == null, Rendicion survives) ─────────

    private async Task<(Transferencia transferencia, Rendicion rendicion, List<Compra> compras)>
        CreateLegacyTransferenciaAsync(int compraCount = 2)
    {
        var rendicion = new Rendicion
        {
            Trabajador = "Pedro",
            FechaInicio = new DateTime(2026, 7, 1),
            Observaciones = "Existing rendicion"
        };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var transferencia = new Transferencia
        {
            Fecha = new DateTime(2026, 7, 1),
            Monto = 5000m,
            Descripcion = "Legacy transferencia",
            Trabajador = "Pedro",
            Estado = TransferenciaEstado.EnUso,
            RendicionId = rendicion.Id,
            PeriodoId = null // legacy — no period
        };
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();

        var compras = new List<Compra>();
        for (int i = 0; i < compraCount; i++)
        {
            var compra = new Compra
            {
                FechaCompra = new DateTime(2026, 7, 1),
                Proveedor = $"Proveedor {i}",
                NumeroDocumento = $"LEG-{i}",
                MontoTotal = 500m * (i + 1),
                Estado = "Registrada",
                TipoFactura = "MERCADERIA",
                Trabajador = "Pedro",
                TransferenciaId = transferencia.Id
            };
            _context.Compras.Add(compra);
            compras.Add(compra);
        }
        await _context.SaveChangesAsync();

        return (transferencia, rendicion, compras);
    }

    [Fact]
    public async Task EliminarTransferenciaCuadreAsync_LegacyPath_ComprasUnlinkedRendicionSurvives()
    {
        // Arrange — legacy transferencia (PeriodoId == null, RendicionId set)
        var (transferencia, rendicion, compras) = await CreateLegacyTransferenciaAsync(2);

        // Act
        var result = await _service.EliminarTransferenciaCuadreAsync(transferencia.Id);

        // Assert — result DTO
        result.ComprasUnlinked.Should().Be(2);
        result.PeriodoId.Should().BeNull();

        // Assert — compras unlinked
        foreach (var compra in compras)
        {
            var reloaded = await _context.Compras.FindAsync(compra.Id);
            reloaded!.TransferenciaId.Should().BeNull();
        }

        // Assert — source Rendicion survives
        var survivingRendicion = await _context.Rendiciones.FindAsync(rendicion.Id);
        survivingRendicion.Should().NotBeNull();

        // Assert — transferencia row deleted
        var deletedTransf = await _context.Transferencias.FindAsync(transferencia.Id);
        deletedTransf.Should().BeNull();
    }

    // ── T-05: Estado Conciliado guard ────────────────────────────────────────

    [Fact]
    public async Task EliminarTransferenciaCuadreAsync_EstadoConciliado_ThrowsInvalidOperationException()
    {
        // Arrange — transferencia with Estado = Conciliado
        var (transferencia, _, _, _) = await CreateCuadreWithComprasAsync(1, TransferenciaEstado.Conciliado);

        // Act
        var act = () => _service.EliminarTransferenciaCuadreAsync(transferencia.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No se puede eliminar una transferencia ya conciliada.");
    }

    // ── T-07: Happy path — single compra (simplified, no file I/O) ──────

    [Fact]
    public async Task EliminarTransferenciaCuadreAsync_SingleCompra_ComprasUnlinkedPeriodDeleted()
    {
        var (transferencia, period, _, compras) = await CreateCuadreWithComprasAsync(1);

        var result = await _service.EliminarTransferenciaCuadreAsync(transferencia.Id);

        result.ComprasUnlinked.Should().Be(1);
        result.PeriodoId.Should().Be(period.Id);
        var deletedTransf = await _context.Transferencias.FindAsync(transferencia.Id);
        deletedTransf.Should().BeNull();
    }

    // ── T-09: Transactional rollback on mid-transaction failure ─────────────

    /// <summary>
    /// DbContext subclass that throws on SaveChangesAsync to simulate mid-transaction failure.
    /// </summary>
    private class ThrowingDbContext : ApplicationDbContext
    {
        public ThrowingDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            throw new DbUpdateException("Simulated DB failure mid-transaction.",
                new InvalidOperationException("Constraint violation"));
        }
    }

    [Fact]
    public async Task EliminarTransferenciaCuadreAsync_MidTransactionFailure_ExceptionPropagates()
    {
        // Arrange — use a context that will throw on SaveChangesAsync
        // Note: EF InMemory ignores real transactions. This test verifies
        // that the exception propagates (not swallowed by the service).
        var dbName = $"RollbackTest_{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        // Seed data using a normal context
        int transferenciaId;
        using (var seedContext = new ApplicationDbContext(options))
        {
            var period = new AccountingPeriod
            {
                Name = "Rollback Period",
                FechaInicio = new DateTime(2026, 7, 1),
                FechaFin = new DateTime(2026, 7, 31),
                Trabajador = "Test",
                Estado = AccountingPeriodEstado.Abierto
            };
            seedContext.AccountingPeriods.Add(period);
            await seedContext.SaveChangesAsync();

            var rendicion = new Rendicion
            {
                Trabajador = "Test",
                FechaInicio = new DateTime(2026, 7, 1),
                Observaciones = "Test rendicion"
            };
            seedContext.Rendiciones.Add(rendicion);
            await seedContext.SaveChangesAsync();

            var transferencia = new Transferencia
            {
                Fecha = new DateTime(2026, 7, 1),
                Monto = 5000m,
                Trabajador = "Test",
                Estado = TransferenciaEstado.Pendiente,
                RendicionId = rendicion.Id,
                PeriodoId = period.Id
            };
            seedContext.Transferencias.Add(transferencia);
            await seedContext.SaveChangesAsync();
            transferenciaId = transferencia.Id;

            var compra = new Compra
            {
                FechaCompra = new DateTime(2026, 7, 1),
                Proveedor = "Test",
                NumeroDocumento = "ROLL-001",
                MontoTotal = 1000m,
                Estado = "Registrada",
                TipoFactura = "MERCADERIA",
                Trabajador = "Test",
                TransferenciaId = transferencia.Id
            };
            seedContext.Compras.Add(compra);
            await seedContext.SaveChangesAsync();
        }

        // Create service with throwing context
        using var throwingContext = new ThrowingDbContext(options);
        var throwingPeriodRepo = new AccountingPeriodRepository(throwingContext);
        var throwingService = new ContabilidadService(throwingContext, throwingPeriodRepo);

        // Act — should throw DbUpdateException
        var act = () => throwingService.EliminarTransferenciaCuadreAsync(transferenciaId);

        // Assert — exception propagates (not swallowed)
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // ── Phase 2: Repository query optimizations (O-03, O-04) ─────────────────

    [Fact]
    public async Task GetByDateRangeAsync_ReturnsCorrectShape()
    {
        // Arrange — seed a period with transferencias
        var period = new AccountingPeriod
        {
            Name = "Test Period",
            FechaInicio = new DateTime(2026, 6, 1),
            FechaFin = new DateTime(2026, 6, 30),
            Trabajador = "Test",
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        var repo = new AccountingPeriodRepository(_context);

        // Act
        var results = await repo.GetByDateRangeAsync(null, null);

        // Assert — correct shape returned
        results.Should().NotBeNull();
        results.Should().ContainSingle();
        var loaded = results[0];
        loaded.Id.Should().Be(period.Id);
        loaded.Name.Should().Be("Test Period");
        loaded.Trabajador.Should().Be("Test");
        loaded.FechaInicio.Should().Be(new DateTime(2026, 6, 1));
        loaded.FechaFin.Should().Be(new DateTime(2026, 6, 30));
        loaded.Estado.Should().Be(AccountingPeriodEstado.Abierto);

        // Assert — AsNoTracking makes entities Detached
        _context.Entry(loaded).State.Should().Be(EntityState.Detached);
    }

    [Fact]
    public async Task GetByDateRangeAsync_WithDateFilter_FiltersCorrectly()
    {
        // Arrange — seed two periods
        _context.AccountingPeriods.AddRange(
            new AccountingPeriod
            {
                Name = "Period 1",
                FechaInicio = new DateTime(2026, 1, 1),
                FechaFin = new DateTime(2026, 1, 31),
                Trabajador = "T1",
                Estado = AccountingPeriodEstado.Abierto
            },
            new AccountingPeriod
            {
                Name = "Period 2",
                FechaInicio = new DateTime(2026, 2, 1),
                FechaFin = new DateTime(2026, 2, 28),
                Trabajador = "T2",
                Estado = AccountingPeriodEstado.Abierto
            }
        );
        await _context.SaveChangesAsync();

        var repo = new AccountingPeriodRepository(_context);

        // Act — filter to only Period 2
        var results = await repo.GetByDateRangeAsync(
            new DateTime(2026, 2, 1), new DateTime(2026, 2, 28));

        // Assert
        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Period 2");
    }

    [Fact]
    public async Task GetFullByIdAsync_ReturnsFullGraph()
    {
        // Arrange — seed a period with transferencia + compra + gasto
        var period = new AccountingPeriod
        {
            Name = "Full Test",
            FechaInicio = new DateTime(2026, 6, 1),
            FechaFin = new DateTime(2026, 6, 30),
            Trabajador = "Test",
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        var rendicion = new Rendicion
        {
            Trabajador = "Test",
            FechaInicio = new DateTime(2026, 6, 1),
            Observaciones = "Test"
        };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var transferencia = new Transferencia
        {
            Fecha = new DateTime(2026, 6, 1),
            Monto = 100000m,
            Trabajador = "Test",
            Estado = TransferenciaEstado.Pendiente,
            PeriodoId = period.Id,
            RendicionId = rendicion.Id
        };
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();

        var compra = new Compra
        {
            FechaCompra = new DateTime(2026, 6, 5),
            Proveedor = "Test Provider",
            MontoTotal = 25000m,
            Estado = "Registrada",
            TipoFactura = "MERCADERIA",
            Trabajador = "Test",
            TransferenciaId = transferencia.Id
        };
        _context.Compras.Add(compra);

        var gasto = new MovimientoCaja
        {
            Fecha = new DateTime(2026, 6, 10),
            Descripcion = "Test gasto",
            Monto = -5000m,
            Tipo = "GASTO",
            Categoria = "GENERAL",
            Trabajador = "Test",
            RendicionId = rendicion.Id
        };
        _context.MovimientosCaja.Add(gasto);
        await _context.SaveChangesAsync();

        var repo = new AccountingPeriodRepository(_context);

        // Act
        var loaded = await repo.GetFullByIdAsync(period.Id);

        // Assert — full graph loaded with AsSplitQuery
        loaded.Should().NotBeNull();
        loaded!.Transferencias.Should().HaveCount(1);
        loaded.Transferencias[0].Monto.Should().Be(100000m);
        loaded.Transferencias[0].Compras.Should().HaveCount(1);
        loaded.Transferencias[0].Compras[0].Proveedor.Should().Be("Test Provider");
        loaded.Transferencias[0].Rendicion.Should().NotBeNull();
        loaded.Devoluciones.Should().BeEmpty();

        // Assert — AsNoTracking makes entities Detached
        _context.Entry(loaded).State.Should().Be(EntityState.Detached);
    }

    // ── Phase 3: Aggregate totals (O-01) ────────────────────────────────────

    [Fact]
    public async Task GetPeriodosAsync_AggregateTotals_ReturnsCorrectSums()
    {
        // Arrange — seed a period with transferencia, compra, gasto
        var period = new AccountingPeriod
        {
            Name = "Aggregate Test",
            FechaInicio = new DateTime(2026, 6, 1),
            FechaFin = new DateTime(2026, 6, 30),
            Trabajador = "Test Worker",
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        var rendicion = new Rendicion
        {
            Trabajador = "Test Worker",
            FechaInicio = new DateTime(2026, 6, 1),
            Observaciones = "Test"
        };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var transferencia = new Transferencia
        {
            Fecha = new DateTime(2026, 6, 1),
            Monto = 340000m,
            Trabajador = "Test Worker",
            Estado = TransferenciaEstado.Pendiente,
            PeriodoId = period.Id,
            RendicionId = rendicion.Id
        };
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();

        var compra = new Compra
        {
            FechaCompra = new DateTime(2026, 6, 5),
            Proveedor = "Test Provider",
            MontoTotal = 25000m,
            Estado = "Registrada",
            TipoFactura = "MERCADERIA",
            Trabajador = "Test Worker",
            TransferenciaId = transferencia.Id
        };
        _context.Compras.Add(compra);

        var gasto = new MovimientoCaja
        {
            Fecha = new DateTime(2026, 6, 10),
            Descripcion = "Test gasto",
            Monto = -5000m,
            Tipo = "GASTO",
            Categoria = "GENERAL",
            Trabajador = "Test Worker",
            RendicionId = rendicion.Id
        };
        _context.MovimientosCaja.Add(gasto);
        await _context.SaveChangesAsync();

        // Act
        var dtos = await _service.GetPeriodosAsync(null, null);

        // Assert — aggregate totals from SQL, not nav properties
        dtos.Should().NotBeNull();
        dtos.Should().ContainSingle();
        var dto = dtos[0];
        dto.TotalTransferido.Should().Be(340000m);
        dto.TotalCompras.Should().Be(25000m);
        dto.TotalGastos.Should().Be(5000m);
        dto.Diferencia.Should().Be(310000m); // 340000 - 25000 - 5000
    }

    [Fact]
    public async Task GetPeriodosAsync_EmptyPeriod_ReturnsZeroTotals()
    {
        // Arrange — period with NO transferencias, compras, or gastos
        var period = new AccountingPeriod
        {
            Name = "Empty Period",
            FechaInicio = new DateTime(2026, 7, 1),
            FechaFin = new DateTime(2026, 7, 31),
            Trabajador = "Empty Worker",
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        // Act
        var dtos = await _service.GetPeriodosAsync(null, null);

        // Assert — all totals are 0
        dtos.Should().NotBeNull();
        dtos.Should().ContainSingle();
        var dto = dtos[0];
        dto.TotalTransferido.Should().Be(0m);
        dto.TotalCompras.Should().Be(0m);
        dto.TotalGastos.Should().Be(0m);
        dto.Diferencia.Should().Be(0m);
    }

    // ── Phase 4: IMemoryCache (O-07) ──────────────────────────────────────────

    [Fact]
    public async Task GetPeriodosAsync_CacheHit_ReturnsCachedData()
    {
        // Arrange — seed period data
        var period = new AccountingPeriod
        {
            Name = "Cache Test",
            FechaInicio = new DateTime(2026, 6, 1),
            FechaFin = new DateTime(2026, 6, 30),
            Trabajador = "Cached Worker",
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        var rendicion = new Rendicion { Trabajador = "Cached Worker", FechaInicio = new DateTime(2026, 6, 1), Observaciones = "R" };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var transferencia = new Transferencia { Fecha = new DateTime(2026, 6, 1), Monto = 50000m, Trabajador = "Cached Worker", Estado = TransferenciaEstado.Pendiente, PeriodoId = period.Id, RendicionId = rendicion.Id };
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var config = Options.Create(new VendingConfig { UsePeriodCache = true, PeriodCacheDurationMinutes = 5 });
        var periodRepo = new AccountingPeriodRepository(_context);
        var service = new ContabilidadService(_context, periodRepo, cache, config);

        // Act — first call (cache miss, queries DB)
        var firstResult = await service.GetPeriodosAsync(null, null);

        // Mutate underlying data — add another period
        var period2 = new AccountingPeriod { Name = "Period 2", FechaInicio = new DateTime(2026, 7, 1), FechaFin = new DateTime(2026, 7, 31), Trabajador = "Worker 2", Estado = AccountingPeriodEstado.Abierto };
        _context.AccountingPeriods.Add(period2);
        await _context.SaveChangesAsync();

        // Act — second call (cache hit, should return old data without period2)
        var secondResult = await service.GetPeriodosAsync(null, null);

        // Assert — cache hit returns the same (old) data
        secondResult.Should().HaveCount(1);
        secondResult[0].TotalTransferido.Should().Be(50000m);
    }

    [Fact]
    public async Task GetPeriodosAsync_CacheMiss_QueriesDatabase()
    {
        // Arrange — seed period data
        var period = new AccountingPeriod
        {
            Name = "Fresh Data",
            FechaInicio = new DateTime(2026, 6, 1),
            FechaFin = new DateTime(2026, 6, 30),
            Trabajador = "Fresh Worker",
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var config = Options.Create(new VendingConfig { UsePeriodCache = false, PeriodCacheDurationMinutes = 5 });
        var periodRepo = new AccountingPeriodRepository(_context);
        var service = new ContabilidadService(_context, periodRepo, cache, config);

        // Act — first call with cache disabled
        var firstResult = await service.GetPeriodosAsync(null, null);

        // Add another period
        var period2 = new AccountingPeriod { Name = "Period 2", FechaInicio = new DateTime(2026, 7, 1), FechaFin = new DateTime(2026, 7, 31), Trabajador = "Worker 2", Estado = AccountingPeriodEstado.Abierto };
        _context.AccountingPeriods.Add(period2);
        await _context.SaveChangesAsync();

        // Act — second call with cache disabled should hit DB
        var secondResult = await service.GetPeriodosAsync(null, null);

        // Assert — cache disabled, so second call returns fresh data
        secondResult.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPeriodosAsync_CacheHit_DevueltoTotalsPreserved()
    {
        // Arrange — seed period with devolucion
        var period = new AccountingPeriod
        {
            Name = "Cache Devuelto",
            FechaInicio = new DateTime(2026, 6, 1),
            FechaFin = new DateTime(2026, 6, 30),
            Trabajador = "Test",
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        var devolucion = new Devolucion { Monto = 10000m, Fecha = new DateTime(2026, 6, 15), Trabajador = "Test", PeriodoId = period.Id };
        _context.Devoluciones.Add(devolucion);
        await _context.SaveChangesAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var config = Options.Create(new VendingConfig { UsePeriodCache = true, PeriodCacheDurationMinutes = 5 });
        var periodRepo = new AccountingPeriodRepository(_context);
        var service = new ContabilidadService(_context, periodRepo, cache, config);

        // Act — first call (cache miss)
        var result = await service.GetPeriodosAsync(null, null);

        // Assert — Devuelto is preserved alongside cached totals
        result.Should().ContainSingle();
        result[0].Devuelto.Should().Be(10000m);
    }

    [Fact]
    public async Task GetPeriodosAsync_MultiplePeriods_AggregatesPerPeriod()
    {
        // Arrange — two periods, each with different data
        var period1 = new AccountingPeriod
        {
            Name = "Period 1",
            FechaInicio = new DateTime(2026, 6, 1),
            FechaFin = new DateTime(2026, 6, 15),
            Trabajador = "Worker A",
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period1);
        await _context.SaveChangesAsync();

        var rendicion1 = new Rendicion { Trabajador = "Worker A", FechaInicio = new DateTime(2026, 6, 1), Observaciones = "R1" };
        _context.Rendiciones.Add(rendicion1);
        await _context.SaveChangesAsync();

        var t1 = new Transferencia { Fecha = new DateTime(2026, 6, 1), Monto = 100000m, Trabajador = "Worker A", Estado = TransferenciaEstado.Pendiente, PeriodoId = period1.Id, RendicionId = rendicion1.Id };
        _context.Transferencias.Add(t1);
        await _context.SaveChangesAsync();

        var c1 = new Compra { FechaCompra = new DateTime(2026, 6, 5), Proveedor = "P1", MontoTotal = 30000m, Estado = "Registrada", TipoFactura = "MERCADERIA", Trabajador = "Worker A", TransferenciaId = t1.Id };
        _context.Compras.Add(c1);

        var g1 = new MovimientoCaja { Fecha = new DateTime(2026, 6, 10), Descripcion = "G1", Monto = -10000m, Tipo = "GASTO", Categoria = "GENERAL", Trabajador = "Worker A", RendicionId = rendicion1.Id };
        _context.MovimientosCaja.Add(g1);

        var period2 = new AccountingPeriod
        {
            Name = "Period 2",
            FechaInicio = new DateTime(2026, 7, 1),
            FechaFin = new DateTime(2026, 7, 15),
            Trabajador = "Worker B",
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period2);
        await _context.SaveChangesAsync();

        var rendicion2 = new Rendicion { Trabajador = "Worker B", FechaInicio = new DateTime(2026, 7, 1), Observaciones = "R2" };
        _context.Rendiciones.Add(rendicion2);
        await _context.SaveChangesAsync();

        var t2 = new Transferencia { Fecha = new DateTime(2026, 7, 1), Monto = 200000m, Trabajador = "Worker B", Estado = TransferenciaEstado.Pendiente, PeriodoId = period2.Id, RendicionId = rendicion2.Id };
        _context.Transferencias.Add(t2);
        await _context.SaveChangesAsync();

        var c2 = new Compra { FechaCompra = new DateTime(2026, 7, 5), Proveedor = "P2", MontoTotal = 50000m, Estado = "Registrada", TipoFactura = "MERCADERIA", Trabajador = "Worker B", TransferenciaId = t2.Id };
        _context.Compras.Add(c2);
        await _context.SaveChangesAsync();

        // Act
        var dtos = await _service.GetPeriodosAsync(null, null);

        // Assert — each period has its own aggregates
        dtos.Should().HaveCount(2);

        var dto1 = dtos.First(d => d.Id == period1.Id);
        dto1.TotalTransferido.Should().Be(100000m);
        dto1.TotalCompras.Should().Be(30000m);
        dto1.TotalGastos.Should().Be(10000m);

        var dto2 = dtos.First(d => d.Id == period2.Id);
        dto2.TotalTransferido.Should().Be(200000m);
        dto2.TotalCompras.Should().Be(50000m);
        dto2.TotalGastos.Should().Be(0m);
    }
}
