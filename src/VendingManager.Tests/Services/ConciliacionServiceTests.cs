namespace VendingManager.Tests.Services;

using System.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using VendingManager.Core.Configuration;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data.Repositories;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;
using VendingManager.Tests.TestData;

/// <summary>
/// Service-layer TDD tests for slice 2: TASK-07 through TASK-10.
/// Each section is clearly labeled with its task number and behavior number from the design doc.
/// </summary>
public class ConciliacionServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IWebHostEnvironment> _mockEnv;
    private readonly IOptions<VendingConfig> _config;
    private readonly IUploadPathProvider _uploadProvider;
    private readonly ContabilidadService _contabilidadService;
    private readonly TransferenciaService _transferenciaService;
    private readonly RendicionService _rendicionService;

    public ConciliacionServiceTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"ConciliacionServiceTestDb_{Guid.NewGuid()}");

        _mockEnv = new Mock<IWebHostEnvironment>();
        _mockEnv.SetupGet(e => e.WebRootPath).Returns("/tmp/wwwroot");
        _mockEnv.SetupGet(e => e.ContentRootPath).Returns("/tmp");

        var vendingConfig = new VendingConfig();
        _config = Options.Create(vendingConfig);
        _uploadProvider = new DefaultUploadPathProvider(_mockEnv.Object, _config);

        var periodRepo = new AccountingPeriodRepository(_context);
        _contabilidadService = new ContabilidadService(_context, periodRepo);
        _transferenciaService = new TransferenciaService(_context, _uploadProvider);
        _rendicionService = new RendicionService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IFormFile CreateMockFile(string fileName, long sizeBytes, string contentType = "image/jpeg")
    {
        var mock = new Mock<IFormFile>();
        mock.SetupGet(f => f.FileName).Returns(fileName);
        mock.SetupGet(f => f.Length).Returns(sizeBytes);
        mock.SetupGet(f => f.ContentType).Returns(contentType);
        mock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock.Object;
    }

    private async Task<AccountingPeriod> CreateOpenPeriodAsync()
    {
        var period = new AccountingPeriod
        {
            Name = "Test Period",
            FechaInicio = DateTime.Today,
            FechaFin = DateTime.Today.AddMonths(1),
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();
        return period;
    }

    private async Task<Transferencia> CreateTransferenciaForPeriodAsync(int periodoId)
    {
        var t = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 1000m,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.EnUso,
            PeriodoId = periodoId
        };
        _context.Transferencias.Add(t);
        await _context.SaveChangesAsync();
        return t;
    }

    private async Task<Compra> CreateCompraForTransferenciaAsync(int transferenciaId)
    {
        var c = new Compra
        {
            Proveedor = "Prov A",
            FechaCompra = DateTime.Today,
            MontoTotal = 400m,
            TransferenciaId = transferenciaId
        };
        _context.Compras.Add(c);
        await _context.SaveChangesAsync();
        return c;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TASK-07: SaveComprobanteImagenAsync
    // Design behavior 8: validates size/ext, stores /uploads/transferencias/{guid}.ext,
    // replaces previous file, persists path, handles RowVersion concurrency.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SaveComprobanteImagenAsync_NullFile_ThrowsArgumentException()
    {
        // Arrange
        var transferencia = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 500m,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.Pendiente
        };
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _transferenciaService.SaveComprobanteImagenAsync(transferencia.Id, null!));
    }

    [Fact]
    public async Task SaveComprobanteImagenAsync_OversizedFile_ThrowsArgumentException()
    {
        // Arrange
        var transferencia = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 500m,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.Pendiente
        };
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();

        var oversizedFile = CreateMockFile("comprobante.jpg", 6 * 1024 * 1024); // 6MB

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _transferenciaService.SaveComprobanteImagenAsync(transferencia.Id, oversizedFile));
        ex.Message.Should().Contain("5MB");
    }

    [Fact]
    public async Task SaveComprobanteImagenAsync_InvalidExtension_ThrowsArgumentException()
    {
        // Arrange
        var transferencia = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 500m,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.Pendiente
        };
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();

        var badFile = CreateMockFile("virus.exe", 1024, "application/octet-stream");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _transferenciaService.SaveComprobanteImagenAsync(transferencia.Id, badFile));
    }

    [Fact]
    public async Task SaveComprobanteImagenAsync_NotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var validFile = CreateMockFile("comprobante.jpg", 1024);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _transferenciaService.SaveComprobanteImagenAsync(9999, validFile));
    }

    [Fact]
    public async Task SaveComprobanteImagenAsync_ValidFile_ReturnsPathInTransferenciasFolder()
    {
        // Arrange
        var transferencia = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 500m,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.Pendiente
        };
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();

        var validFile = CreateMockFile("comprobante.jpg", 1024);

        // Act
        var path = await _transferenciaService.SaveComprobanteImagenAsync(transferencia.Id, validFile);

        // Assert — path matches /uploads/transferencias/{guid}.jpg convention
        path.Should().StartWith("/uploads/transferencias/");
        path.Should().EndWith(".jpg");

        // Persisted on entity
        var updated = await _context.Transferencias.FindAsync(transferencia.Id);
        updated!.ComprobanteImagenPath.Should().Be(path);
    }

    [Fact]
    public async Task SaveComprobanteImagenAsync_PdfExtension_IsAllowed()
    {
        // Arrange
        var transferencia = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 500m,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.Pendiente
        };
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();

        var pdfFile = CreateMockFile("comprobante.pdf", 1024, "application/pdf");

        // Act
        var path = await _transferenciaService.SaveComprobanteImagenAsync(transferencia.Id, pdfFile);

        // Assert
        path.Should().EndWith(".pdf");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TASK-08: MarcarTransferenciaVerificadaAsync + MarcarCompraVerificadaAsync
    // Design behavior 7: flips Verificada flag; stale RowVersion → DbUpdateConcurrencyException.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MarcarTransferenciaVerificadaAsync_SetsVerificadaTrue()
    {
        // Arrange
        var t = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 500m,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.Pendiente,
            Verificada = false
        };
        _context.Transferencias.Add(t);
        await _context.SaveChangesAsync();

        // Act
        await _contabilidadService.MarcarTransferenciaVerificadaAsync(t.Id, true);

        // Assert
        _context.ChangeTracker.Clear();
        var updated = await _context.Transferencias.FindAsync(t.Id);
        updated!.Verificada.Should().BeTrue();
    }

    [Fact]
    public async Task MarcarTransferenciaVerificadaAsync_SetsVerificadaFalse_Desverify()
    {
        // Arrange
        var t = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 500m,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.EnUso,
            Verificada = true
        };
        _context.Transferencias.Add(t);
        await _context.SaveChangesAsync();

        // Act
        await _contabilidadService.MarcarTransferenciaVerificadaAsync(t.Id, false);

        // Assert
        _context.ChangeTracker.Clear();
        var updated = await _context.Transferencias.FindAsync(t.Id);
        updated!.Verificada.Should().BeFalse();
    }

    [Fact]
    public async Task MarcarTransferenciaVerificadaAsync_NotFound_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _contabilidadService.MarcarTransferenciaVerificadaAsync(9999, true));
    }

    [Fact]
    public async Task MarcarCompraVerificadaAsync_SetsVerificadaTrue()
    {
        // Arrange
        var c = new Compra
        {
            Proveedor = "Prov A",
            FechaCompra = DateTime.Today,
            MontoTotal = 300m,
            Verificada = false
        };
        _context.Compras.Add(c);
        await _context.SaveChangesAsync();

        // Act
        await _contabilidadService.MarcarCompraVerificadaAsync(c.Id, true);

        // Assert
        _context.ChangeTracker.Clear();
        var updated = await _context.Compras.FindAsync(c.Id);
        updated!.Verificada.Should().BeTrue();
    }

    [Fact]
    public async Task MarcarCompraVerificadaAsync_NotFound_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _contabilidadService.MarcarCompraVerificadaAsync(9999, true));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TASK-09: RegistrarDevolucionAsync + atomic MovimientoCaja posting
    // Design behaviors 1-3, 6:
    //   1. Creates Devolucion + one positive MovimientoCaja (APORTE), links MovimientoCajaId.
    //   2. Second Devolucion on same open period → InvalidOperationException (idempotency).
    //   3. Devolucion on closed period → InvalidOperationException.
    //   (extra) Monto <= 0 → InvalidOperationException.
    //   (extra) Neither PeriodoId nor RendicionId → InvalidOperationException.
    //   (extra) Fecha is taken from request, not DateTime.Now.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RegistrarDevolucionAsync_CreatesDevolucionAndPositiveMovimientoCaja()
    {
        // Arrange
        var period = await CreateOpenPeriodAsync();
        await CreateTransferenciaForPeriodAsync(period.Id);
        var request = new RegistrarDevolucionRequest
        {
            PeriodoId = period.Id,
            Trabajador = "Juan",
            Monto = 300m,
            Fecha = new DateTime(2026, 6, 15)
        };

        // Act
        var dto = await _contabilidadService.RegistrarDevolucionAsync(request);

        // Assert — Devolucion created
        dto.Should().NotBeNull();
        dto.Monto.Should().Be(300m);
        dto.PeriodoId.Should().Be(period.Id);

        // Assert — one positive MovimientoCaja linked
        var devolucion = await _context.Devoluciones.FindAsync(dto.Id);
        devolucion!.MovimientoCajaId.Should().NotBeNull();

        var movimiento = await _context.MovimientosCaja.FindAsync(devolucion.MovimientoCajaId!.Value);
        movimiento.Should().NotBeNull();
        movimiento!.Monto.Should().BeGreaterThan(0); // positive = money back into caja
        movimiento.Tipo.Should().Be("APORTE");
        movimiento.Categoria.Should().Be("DEVOLUCION_RENDICION");
    }

    [Fact]
    public async Task RegistrarDevolucionAsync_FechaFromRequest_NotDateTimeNow()
    {
        // Arrange
        var period = await CreateOpenPeriodAsync();
        await CreateTransferenciaForPeriodAsync(period.Id);
        var expectedFecha = new DateTime(2026, 5, 10);
        var request = new RegistrarDevolucionRequest
        {
            PeriodoId = period.Id,
            Trabajador = "Juan",
            Monto = 100m,
            Fecha = expectedFecha
        };

        // Act
        var dto = await _contabilidadService.RegistrarDevolucionAsync(request);

        // Assert — Fecha comes from request
        var devolucion = await _context.Devoluciones.FindAsync(dto.Id);
        devolucion!.Fecha.Should().Be(expectedFecha);
    }

    [Fact]
    public async Task RegistrarDevolucionAsync_SecondDevolucionSamePeriod_ThrowsInvalidOperation()
    {
        // Arrange — first devolucion
        var period = await CreateOpenPeriodAsync();
        await CreateTransferenciaForPeriodAsync(period.Id);
        var first = new RegistrarDevolucionRequest
        {
            PeriodoId = period.Id,
            Trabajador = "Juan",
            Monto = 100m,
            Fecha = DateTime.Today
        };
        await _contabilidadService.RegistrarDevolucionAsync(first);

        // Act & Assert — second devolucion same period
        var second = new RegistrarDevolucionRequest
        {
            PeriodoId = period.Id,
            Trabajador = "Juan",
            Monto = 50m,
            Fecha = DateTime.Today
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _contabilidadService.RegistrarDevolucionAsync(second));
    }

    [Fact]
    public async Task RegistrarDevolucionAsync_ClosedPeriod_ThrowsInvalidOperation()
    {
        // Arrange
        var period = new AccountingPeriod
        {
            Name = "Closed Period",
            FechaInicio = DateTime.Today.AddMonths(-1),
            FechaFin = DateTime.Today,
            Estado = AccountingPeriodEstado.Cerrado
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        var request = new RegistrarDevolucionRequest
        {
            PeriodoId = period.Id,
            Trabajador = "Juan",
            Monto = 100m,
            Fecha = DateTime.Today
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _contabilidadService.RegistrarDevolucionAsync(request));
    }

    [Fact]
    public async Task RegistrarDevolucionAsync_MontoZeroOrNegative_ThrowsInvalidOperation()
    {
        // Arrange
        var period = await CreateOpenPeriodAsync();
        var request = new RegistrarDevolucionRequest
        {
            PeriodoId = period.Id,
            Trabajador = "Juan",
            Monto = 0m,
            Fecha = DateTime.Today
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _contabilidadService.RegistrarDevolucionAsync(request));
    }

    [Fact]
    public async Task RegistrarDevolucionAsync_NeitherPeriodoNorRendicion_ThrowsInvalidOperation()
    {
        // Arrange
        var request = new RegistrarDevolucionRequest
        {
            PeriodoId = null,
            RendicionId = null,
            Trabajador = "Juan",
            Monto = 100m,
            Fecha = DateTime.Today
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _contabilidadService.RegistrarDevolucionAsync(request));
    }

    [Fact]
    public async Task RegistrarDevolucionAsync_ByRendicion_CreatesDevolucionAndMovimientoCaja()
    {
        // Arrange
        var rendicion = new Rendicion
        {
            Trabajador = "Worker B",
            FechaInicio = DateTime.Today,
            Estado = RendicionEstado.Abierta
        };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var rendTransfer = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 1000m,
            Trabajador = "Worker B",
            Estado = TransferenciaEstado.EnUso,
            RendicionId = rendicion.Id
        };
        _context.Transferencias.Add(rendTransfer);
        await _context.SaveChangesAsync();

        var request = new RegistrarDevolucionRequest
        {
            RendicionId = rendicion.Id,
            Trabajador = "Worker B",
            Monto = 200m,
            Fecha = DateTime.Today
        };

        // Act
        var dto = await _contabilidadService.RegistrarDevolucionAsync(request);

        // Assert
        dto.RendicionId.Should().Be(rendicion.Id);
        var devolucion = await _context.Devoluciones.FindAsync(dto.Id);
        devolucion!.MovimientoCajaId.Should().NotBeNull();
    }

    [Fact]
    public async Task RegistrarDevolucionAsync_MontoExceedsSaldo_ThrowsInvalidOperation()
    {
        // Arrange — 1000 transferido, 400 in compras → SaldoADevolver = 600
        var period = await CreateOpenPeriodAsync();
        var t = await CreateTransferenciaForPeriodAsync(period.Id);
        await CreateCompraForTransferenciaAsync(t.Id);

        var request = new RegistrarDevolucionRequest
        {
            PeriodoId = period.Id,
            Trabajador = "Juan",
            Monto = 700m, // exceeds SaldoADevolver of 600
            Fecha = DateTime.Today
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _contabilidadService.RegistrarDevolucionAsync(request));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TASK-10: Devuelto wired into mappers + close-gate preconditions
    // Design behaviors 4, 5, 6:
    //   4. SaldoADevolver == Diferencia - Devuelto; Diferencia unchanged.
    //   5. ClosePeriodoAsync throws when any Transferencia/Compra unverified.
    //   6. ClosePeriodoAsync throws when SaldoADevolver != 0; succeeds when verified AND saldo == 0.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MapToDto_Devuelto_WiredFromDevoluciones()
    {
        // Arrange — period with one Devolucion of 200
        var period = await CreateOpenPeriodAsync();
        var t = await CreateTransferenciaForPeriodAsync(period.Id);

        var devolucion = new Devolucion
        {
            Monto = 200m,
            Fecha = DateTime.Today,
            Trabajador = "Juan",
            PeriodoId = period.Id
        };
        _context.Devoluciones.Add(devolucion);
        await _context.SaveChangesAsync();

        // Clear tracker so Include reloads the entity from the InMemory store with navigation props
        _context.ChangeTracker.Clear();

        // Act
        var dto = await _contabilidadService.GetPeriodoFullAsync(period.Id);

        // Assert
        dto.Should().NotBeNull();
        dto!.Devuelto.Should().Be(200m);
        dto.SaldoADevolver.Should().Be(dto.Diferencia - 200m);
    }

    [Fact]
    public async Task MapToDto_NoDevoluciones_DevueltoIsZero()
    {
        // Arrange
        var period = await CreateOpenPeriodAsync();

        // Act
        var dto = await _contabilidadService.GetPeriodoFullAsync(period.Id);

        // Assert
        dto!.Devuelto.Should().Be(0m);
        dto.SaldoADevolver.Should().Be(dto.Diferencia);
    }

    [Fact]
    public async Task GetPeriodoFullAsync_ExcludesRetiroCapitalFromGastos()
    {
        // Arrange — a rendicion-backed transfer with its RETIRO_CAPITAL counterpart
        // (the funding of the transfer, NOT a real expense), one real gasto, and a compra.
        var period = await CreateOpenPeriodAsync();

        var rendicion = new Rendicion { Trabajador = "Worker A" };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var t = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 1000m,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.EnUso,
            PeriodoId = period.Id,
            RendicionId = rendicion.Id
        };
        _context.Transferencias.Add(t);
        await _context.SaveChangesAsync();

        _context.MovimientosCaja.AddRange(
            new MovimientoCaja
            {
                Fecha = DateTime.Today,
                Descripcion = "Retiro para rendición: transferencia",
                Monto = -1000m,
                Tipo = "RETIRO",
                Categoria = "RETIRO_CAPITAL",
                RendicionId = rendicion.Id
            },
            new MovimientoCaja
            {
                Fecha = DateTime.Today,
                Descripcion = "Bencina",
                Monto = -150m,
                Tipo = "GASTO",
                Categoria = "BENCINA",
                RendicionId = rendicion.Id
            });

        _context.Compras.Add(new Compra
        {
            Proveedor = "Prov A",
            FechaCompra = DateTime.Today,
            MontoTotal = 400m,
            TransferenciaId = t.Id
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Act
        var dto = await _contabilidadService.GetPeriodoFullAsync(period.Id);

        // Assert — the RETIRO_CAPITAL must NOT be counted/listed as a gasto
        dto.Should().NotBeNull();
        dto!.TotalGastos.Should().Be(150m); // only the real gasto, not the 1000 retiro
        dto.Gastos.Should().ContainSingle(g => g.Categoria == "BENCINA");
        dto.Gastos.Should().NotContain(g => g.Categoria == "RETIRO_CAPITAL");
        // Diferencia = 1000 transferido − 400 compras − 150 gastos = 450 (was −550 with double count)
        dto.Diferencia.Should().Be(450m);
    }

    [Fact]
    public async Task ClosePeriodoAsync_UnverifiedTransferencia_ThrowsInvalidOperation()
    {
        // Arrange — period with one Transferencia (Verificada = false) linked to a compra
        var period = await CreateOpenPeriodAsync();
        var t = await CreateTransferenciaForPeriodAsync(period.Id);
        await CreateCompraForTransferenciaAsync(t.Id);

        // Conciliate transfer to pass the existing check, but leave Verificada = false
        t.Estado = TransferenciaEstado.Conciliado;
        _context.Transferencias.Update(t);
        await _context.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _contabilidadService.ClosePeriodoAsync(period.Id));
    }

    [Fact]
    public async Task ClosePeriodoAsync_UnverifiedCompra_ThrowsInvalidOperation()
    {
        // Arrange — period with verified transferencia but unverified compra
        var period = await CreateOpenPeriodAsync();
        var t = await CreateTransferenciaForPeriodAsync(period.Id);
        t.Estado = TransferenciaEstado.Conciliado;
        t.Verificada = true;
        _context.Transferencias.Update(t);

        var compra = await CreateCompraForTransferenciaAsync(t.Id);
        // compra.Verificada is false by default

        await _context.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _contabilidadService.ClosePeriodoAsync(period.Id));
    }

    [Fact]
    public async Task ClosePeriodoAsync_SaldoADevolverNonZero_ThrowsInvalidOperation()
    {
        // Arrange — all verified but no devolucion → SaldoADevolver = Diferencia > 0
        var period = await CreateOpenPeriodAsync();
        var t = await CreateTransferenciaForPeriodAsync(period.Id);
        t.Estado = TransferenciaEstado.Conciliado;
        t.Verificada = true;
        _context.Transferencias.Update(t);

        var compra = await CreateCompraForTransferenciaAsync(t.Id);
        compra.Verificada = true;
        compra.MontoTotal = 600m; // compra < transferencia (1000), leaving SaldoADevolver > 0
        _context.Compras.Update(compra);

        await _context.SaveChangesAsync();

        // Act & Assert — SaldoADevolver = 1000 - 600 = 400 ≠ 0
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _contabilidadService.ClosePeriodoAsync(period.Id));
    }

    [Fact]
    public async Task ClosePeriodoAsync_AllVerifiedAndSaldoZero_Succeeds()
    {
        // Arrange — transfer == compra → Diferencia = 0 → SaldoADevolver = 0
        var period = await CreateOpenPeriodAsync();
        var t = await CreateTransferenciaForPeriodAsync(period.Id);
        t.Estado = TransferenciaEstado.Conciliado;
        t.Verificada = true;
        t.Monto = 400m;
        _context.Transferencias.Update(t);

        var compra = await CreateCompraForTransferenciaAsync(t.Id);
        compra.Verificada = true;
        compra.MontoTotal = 400m;
        _context.Compras.Update(compra);

        await _context.SaveChangesAsync();

        // Act — should succeed (no exception)
        var act = async () => await _contabilidadService.ClosePeriodoAsync(period.Id);
        await act.Should().NotThrowAsync();

        // Assert
        _context.ChangeTracker.Clear();
        var closed = await _context.AccountingPeriods.FindAsync(period.Id);
        closed!.Estado.Should().Be(AccountingPeriodEstado.Cerrado);
    }

    [Fact]
    public async Task RendicionResumenAsync_Devuelto_WiredFromDevoluciones()
    {
        // Arrange
        var rendicion = new Rendicion
        {
            Trabajador = "Worker C",
            FechaInicio = DateTime.Today,
            Estado = RendicionEstado.Abierta
        };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var devolucion = new Devolucion
        {
            Monto = 150m,
            Fecha = DateTime.Today,
            Trabajador = "Worker C",
            RendicionId = rendicion.Id
        };
        _context.Devoluciones.Add(devolucion);
        await _context.SaveChangesAsync();

        // Act
        var resumen = await _rendicionService.GetResumenAsync(rendicion.Id);

        // Assert
        resumen.Devuelto.Should().Be(150m);
        resumen.SaldoADevolver.Should().Be(resumen.Diferencia - 150m);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FIX-1 (CRITICAL-1): RendicionService.CerrarAsync must mirror the three
    // close-gate preconditions from ContabilidadService.ClosePeriodoAsync.
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<Rendicion> CreateOpenRendicionWithTransferenciaAndCompraAsync(
        bool transVerificada = false, bool compraVerificada = false)
    {
        var rendicion = new Rendicion
        {
            Trabajador = "Worker FIX1",
            FechaInicio = DateTime.Today,
            Estado = RendicionEstado.Abierta
        };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var transferencia = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 1000m,
            Trabajador = "Worker FIX1",
            Estado = TransferenciaEstado.Conciliado, // conciliado so existing check passes
            RendicionId = rendicion.Id,
            Verificada = transVerificada
        };
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();

        var compra = new Compra
        {
            Proveedor = "Prov FIX1",
            FechaCompra = DateTime.Today,
            MontoTotal = 1000m, // equal to transferencia → Diferencia = 0
            TransferenciaId = transferencia.Id,
            Verificada = compraVerificada
        };
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync();

        return rendicion;
    }

    [Fact]
    public async Task CerrarAsync_UnverifiedTransferencia_ThrowsInvalidOperation()
    {
        // Arrange — transferencia Verificada=false, compra Verificada=true, SaldoADevolver=0
        var rendicion = await CreateOpenRendicionWithTransferenciaAndCompraAsync(
            transVerificada: false, compraVerificada: true);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _rendicionService.CerrarAsync(rendicion.Id));
        ex.Message.Should().Contain("transferencia");
    }

    [Fact]
    public async Task CerrarAsync_UnverifiedCompra_ThrowsInvalidOperation()
    {
        // Arrange — transferencia Verificada=true, compra Verificada=false, SaldoADevolver=0
        var rendicion = await CreateOpenRendicionWithTransferenciaAndCompraAsync(
            transVerificada: true, compraVerificada: false);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _rendicionService.CerrarAsync(rendicion.Id));
        ex.Message.Should().Contain("compra");
    }

    [Fact]
    public async Task CerrarAsync_SaldoADevolverNonZero_ThrowsInvalidOperation()
    {
        // Arrange — both verified but Monto > MontoTotal → SaldoADevolver > 0
        var rendicion = new Rendicion
        {
            Trabajador = "Worker FIX1B",
            FechaInicio = DateTime.Today,
            Estado = RendicionEstado.Abierta
        };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var transferencia = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 1000m,
            Trabajador = "Worker FIX1B",
            Estado = TransferenciaEstado.Conciliado,
            RendicionId = rendicion.Id,
            Verificada = true
        };
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();

        var compra = new Compra
        {
            Proveedor = "Prov FIX1B",
            FechaCompra = DateTime.Today,
            MontoTotal = 600m, // 1000 - 600 = 400 saldo
            TransferenciaId = transferencia.Id,
            Verificada = true
        };
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync();
        // No devolucion → SaldoADevolver = 400 ≠ 0

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _rendicionService.CerrarAsync(rendicion.Id));
        ex.Message.Should().Contain("saldo");
    }

    [Fact]
    public async Task CerrarAsync_AllVerifiedAndSaldoZero_Succeeds()
    {
        // Arrange — all verified, transferencia == compra (SaldoADevolver = 0)
        var rendicion = await CreateOpenRendicionWithTransferenciaAndCompraAsync(
            transVerificada: true, compraVerificada: true);

        // Act — should not throw
        var act = async () => await _rendicionService.CerrarAsync(rendicion.Id);
        await act.Should().NotThrowAsync();

        // Assert
        _context.ChangeTracker.Clear();
        var closed = await _context.Rendiciones.FindAsync(rendicion.Id);
        closed!.Estado.Should().Be(RendicionEstado.Cerrada);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FIX-2 (WARNING-2): RegistrarDevolucionAsync must reject Monto > SaldoADevolver.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RegistrarDevolucionAsync_MontoExceedsSaldoADevolver_ThrowsInvalidOperation()
    {
        // Arrange — period with Transferencia=500, Compra=200 → Diferencia=300 (SaldoADevolver=300)
        var period = new AccountingPeriod
        {
            Name = "FIX2 Period",
            FechaInicio = DateTime.Today,
            FechaFin = DateTime.Today.AddMonths(1),
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        var t = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 500m,
            Trabajador = "Worker FIX2",
            Estado = TransferenciaEstado.EnUso,
            PeriodoId = period.Id
        };
        _context.Transferencias.Add(t);

        var c = new Compra
        {
            Proveedor = "Prov FIX2",
            FechaCompra = DateTime.Today,
            MontoTotal = 200m,
            TransferenciaId = t.Id
        };
        _context.Compras.Add(c);
        await _context.SaveChangesAsync();

        // SaldoADevolver = 500 - 200 = 300; request Monto = 600 (exceeds)
        var request = new RegistrarDevolucionRequest
        {
            PeriodoId = period.Id,
            Trabajador = "Worker FIX2",
            Monto = 600m, // > 300
            Fecha = DateTime.Today
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _contabilidadService.RegistrarDevolucionAsync(request));
        ex.Message.Should().Contain("saldo");
    }

    [Fact]
    public async Task RegistrarDevolucionAsync_MontoEqualsSaldoADevolver_Succeeds()
    {
        // Arrange — Diferencia = 300, request Monto = 300 (exactly equal → allowed)
        var period = new AccountingPeriod
        {
            Name = "FIX2 Period B",
            FechaInicio = DateTime.Today,
            FechaFin = DateTime.Today.AddMonths(1),
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        var t = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 500m,
            Trabajador = "Worker FIX2B",
            Estado = TransferenciaEstado.EnUso,
            PeriodoId = period.Id
        };
        _context.Transferencias.Add(t);

        var c = new Compra
        {
            Proveedor = "Prov FIX2B",
            FechaCompra = DateTime.Today,
            MontoTotal = 200m,
            TransferenciaId = t.Id
        };
        _context.Compras.Add(c);
        await _context.SaveChangesAsync();

        // SaldoADevolver = 300; request Monto = 300 (exactly at limit)
        var request = new RegistrarDevolucionRequest
        {
            PeriodoId = period.Id,
            Trabajador = "Worker FIX2B",
            Monto = 300m,
            Fecha = DateTime.Today
        };

        // Act — should succeed
        var act = async () => await _contabilidadService.RegistrarDevolucionAsync(request);
        await act.Should().NotThrowAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FIX-3 (WARNING-1): List endpoint SaldoADevolver must reflect Devoluciones.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetPeriodosAsync_SaldoADevolver_ReflectsDevoluciones()
    {
        // Arrange — period with Transferencia=1000, Compra=600 → Diferencia=400.
        // Register a Devolucion of 400 → SaldoADevolver on list should be 0, not 400.
        var period = new AccountingPeriod
        {
            Name = "FIX3 Period",
            FechaInicio = DateTime.Today.AddDays(-5),
            FechaFin = DateTime.Today.AddMonths(1),
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        var t = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 1000m,
            Trabajador = "Worker FIX3",
            Estado = TransferenciaEstado.EnUso,
            PeriodoId = period.Id
        };
        _context.Transferencias.Add(t);

        var devolucion = new Devolucion
        {
            Monto = 400m,
            Fecha = DateTime.Today,
            Trabajador = "Worker FIX3",
            PeriodoId = period.Id
        };
        _context.Devoluciones.Add(devolucion);
        await _context.SaveChangesAsync();

        // Act
        var list = await _contabilidadService.GetPeriodosAsync(
            desde: DateTime.Today.AddDays(-10),
            hasta: DateTime.Today.AddMonths(2));

        // Assert — Devuelto should be 400, not 0
        var dto = list.FirstOrDefault(p => p.Id == period.Id);
        dto.Should().NotBeNull();
        dto!.Devuelto.Should().Be(400m);
        dto.SaldoADevolver.Should().Be(dto.Diferencia - 400m);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DeletePeriodoAsync — unlink, no cascade
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeletePeriodoAsync_DeletesPeriodWithNoLinkedData()
    {
        // Arrange
        var period = new AccountingPeriod
        {
            Name = "DeleteTest Empty",
            FechaInicio = DateTime.Today.AddDays(-10),
            FechaFin = DateTime.Today,
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();
        var periodId = period.Id;

        // Act
        await _contabilidadService.DeletePeriodoAsync(periodId);

        // Assert — period is gone
        _context.ChangeTracker.Clear();
        var deleted = await _context.AccountingPeriods.FindAsync(periodId);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task DeletePeriodoAsync_UnlinksTransferenciasWithoutDeleting()
    {
        // Arrange — period with linked transferencias
        var period = new AccountingPeriod
        {
            Name = "DeleteTest Trans",
            FechaInicio = DateTime.Today.AddDays(-10),
            FechaFin = DateTime.Today,
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        var transferencia1 = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 500m,
            Trabajador = "Worker A",
            Estado = TransferenciaEstado.EnUso,
            PeriodoId = period.Id
        };
        var transferencia2 = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 300m,
            Trabajador = "Worker B",
            Estado = TransferenciaEstado.Pendiente,
            PeriodoId = period.Id
        };
        _context.Transferencias.AddRange(transferencia1, transferencia2);
        await _context.SaveChangesAsync();
        var t1Id = transferencia1.Id;
        var t2Id = transferencia2.Id;

        // Act
        await _contabilidadService.DeletePeriodoAsync(period.Id);

        // Assert — transferencias still exist but are unlinked
        _context.ChangeTracker.Clear();
        var t1 = await _context.Transferencias.FindAsync(t1Id);
        var t2 = await _context.Transferencias.FindAsync(t2Id);
        t1.Should().NotBeNull();
        t1!.PeriodoId.Should().BeNull();
        t2.Should().NotBeNull();
        t2!.PeriodoId.Should().BeNull();

        // Period is gone
        var deleted = await _context.AccountingPeriods.FindAsync(period.Id);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task DeletePeriodoAsync_UnlinksDevolucionesWithoutDeletingOrTouchingMovimientoCaja()
    {
        // Arrange — period with linked devoluciones that have MovimientoCajaId
        var period = new AccountingPeriod
        {
            Name = "DeleteTest Dev",
            FechaInicio = DateTime.Today.AddDays(-10),
            FechaFin = DateTime.Today,
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        // Create MovimientoCaja first (simulating the APORTE posted when devolucion was created)
        var movimiento = new MovimientoCaja
        {
            Fecha = DateTime.Today,
            Descripcion = "Devolucion test",
            Monto = 200m,
            Tipo = "APORTE",
            Categoria = "DEVOLUCION_RENDICION"
        };
        _context.MovimientosCaja.Add(movimiento);
        await _context.SaveChangesAsync();
        var movId = movimiento.Id;

        var devolucion = new Devolucion
        {
            Monto = 200m,
            Fecha = DateTime.Today,
            Trabajador = "Worker C",
            PeriodoId = period.Id,
            MovimientoCajaId = movId
        };
        _context.Devoluciones.Add(devolucion);
        await _context.SaveChangesAsync();
        var devId = devolucion.Id;

        // Act
        await _contabilidadService.DeletePeriodoAsync(period.Id);

        // Assert — devolucion still exists but is unlinked from period
        _context.ChangeTracker.Clear();
        var dev = await _context.Devoluciones.FindAsync(devId);
        dev.Should().NotBeNull();
        dev!.PeriodoId.Should().BeNull();

        // MovimientoCajaId is NOT touched
        dev.MovimientoCajaId.Should().Be(movId);

        // MovimientoCaja still exists
        var mov = await _context.MovimientosCaja.FindAsync(movId);
        mov.Should().NotBeNull();

        // Period is gone
        var deleted = await _context.AccountingPeriods.FindAsync(period.Id);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task DeletePeriodoAsync_ThrowsKeyNotFoundException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _contabilidadService.DeletePeriodoAsync(9999));
    }

    [Fact]
    public async Task DeletePeriodoAsync_DoesNotTouchMovimientoCajaCount()
    {
        // Arrange
        var period = new AccountingPeriod
        {
            Name = "DeleteTest MC",
            FechaInicio = DateTime.Today.AddDays(-10),
            FechaFin = DateTime.Today,
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        var transferencia = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 500m,
            Trabajador = "Worker D",
            Estado = TransferenciaEstado.EnUso,
            PeriodoId = period.Id,
            MovimientoCajaId = 1
        };
        _context.Transferencias.Add(transferencia);

        // Unrelated MovimientoCaja (should never be deleted)
        var mc = new MovimientoCaja
        {
            Fecha = DateTime.Today,
            Descripcion = "Unrelated movement",
            Monto = 1000m,
            Tipo = "INGRESO",
            Categoria = "VENTAS"
        };
        _context.MovimientosCaja.Add(mc);
        await _context.SaveChangesAsync();

        var initialCount = await _context.MovimientosCaja.CountAsync();

        // Act
        await _contabilidadService.DeletePeriodoAsync(period.Id);

        // Assert — MovimientoCaja count unchanged
        var finalCount = await _context.MovimientosCaja.CountAsync();
        finalCount.Should().Be(initialCount);
    }
}
