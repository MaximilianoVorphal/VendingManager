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
}
