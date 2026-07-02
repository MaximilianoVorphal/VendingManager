namespace VendingManager.Tests.Services;

using System.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using VendingManager.Core.Configuration;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
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
    private readonly Mock<IWebHostEnvironment> _mockEnv;
    private readonly IOptions<VendingConfig> _config;
    private readonly IUploadPathProvider _uploadProvider;
    private readonly ContabilidadService _service;

    public ContabilidadServiceTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"ContabilidadServiceTestDb_{Guid.NewGuid()}");

        _mockEnv = new Mock<IWebHostEnvironment>();
        _mockEnv.SetupGet(e => e.WebRootPath).Returns(Path.GetTempPath());
        _mockEnv.SetupGet(e => e.ContentRootPath).Returns(Path.GetTempPath());

        var vendingConfig = new VendingConfig();
        _config = Options.Create(vendingConfig);
        _uploadProvider = new DefaultUploadPathProvider(_mockEnv.Object, _config);

        var periodRepo = new AccountingPeriodRepository(_context);
        _service = new ContabilidadService(_context, periodRepo, _uploadProvider);
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
            PeriodoId = period.Id,
            ComprobanteImagenPath = "/uploads/transferencias/test-comprobante.jpg"
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

    private string CreateTempComprobanteFile(string relativePath)
    {
        var basePath = _uploadProvider.GetUploadBasePath();
        var physicalPath = Path.Combine(basePath, relativePath.TrimStart('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        File.WriteAllText(physicalPath, "fake image content");
        return physicalPath;
    }

    // ── T-01: Cuadre happy path ─────────────────────────────────────────────

    [Fact]
    public async Task EliminarTransferenciaCuadreAsync_CuadreHappyPath_AllComprasUnlinkedPeriodDeleted()
    {
        // Arrange — cuadre with 3 compras + comprobante on disk
        var (transferencia, period, rendicion, compras) = await CreateCuadreWithComprasAsync(3);
        var comprobantePath = CreateTempComprobanteFile(transferencia.ComprobanteImagenPath!);

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

        // Assert — comprobante file gone
        File.Exists(comprobantePath).Should().BeFalse();

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

    // ── T-07: Comprobante file missing on disk ─────────────────────────────

    [Fact]
    public async Task EliminarTransferenciaCuadreAsync_ComprobanteMissingOnDisk_NoError()
    {
        // Arrange — cuadre with comprobante path set but file NOT on disk
        var (transferencia, period, _, compras) = await CreateCuadreWithComprasAsync(1);
        // Don't create the file on disk — it's already missing

        // Act — should NOT throw
        var result = await _service.EliminarTransferenciaCuadreAsync(transferencia.Id);

        // Assert — still succeeds
        result.ComprasUnlinked.Should().Be(1);
        result.PeriodoId.Should().Be(period.Id);

        // Assert — transferencia deleted
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
    public async Task EliminarTransferenciaCuadreAsync_MidTransactionFailure_ExceptionPropagatesAndComprobanteStays()
    {
        // Arrange — use a context that will throw on SaveChangesAsync
        // Note: EF InMemory ignores real transactions. This test verifies that
        // (1) the exception propagates (not swallowed by the service) and
        // (2) the comprobante file is NOT deleted (post-commit, non-transactional).
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
                PeriodoId = period.Id,
                ComprobanteImagenPath = "/uploads/transferencias/rollback-test.jpg"
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

        // Create comprobante file on disk
        var basePath = _uploadProvider.GetUploadBasePath();
        var comprobantePhysicalPath = Path.Combine(basePath, "uploads", "transferencias", "rollback-test.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(comprobantePhysicalPath)!);
        File.WriteAllText(comprobantePhysicalPath, "test content");

        // Create service with throwing context
        using var throwingContext = new ThrowingDbContext(options);
        var throwingPeriodRepo = new AccountingPeriodRepository(throwingContext);
        var throwingService = new ContabilidadService(throwingContext, throwingPeriodRepo, _uploadProvider);

        // Act — should throw DbUpdateException
        var act = () => throwingService.EliminarTransferenciaCuadreAsync(transferenciaId);

        // Assert — exception propagates (not swallowed)
        await act.Should().ThrowAsync<DbUpdateException>();

        // Assert — comprobante file is NOT deleted (file deletion is post-commit)
        File.Exists(comprobantePhysicalPath).Should().BeTrue(
            "comprobante file must survive when transaction fails");

        // Cleanup
        File.Delete(comprobantePhysicalPath);
    }
}
