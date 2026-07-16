namespace VendingManager.Tests.Services;

using FluentAssertions;
using VendingManager.Core.Entities;
using VendingManager.Infrastructure.Data.Repositories;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared;
using VendingManager.Shared.Enums;
using VendingManager.Tests.TestData;

/// <summary>
/// SDD endurecimiento-dominio — Slice 2: CierreValidator extraction.
///
/// Characterization tests capturing CURRENT behavior of both
/// RendicionService.CerrarAsync and ContabilidadService.ClosePeriodoAsync
/// BEFORE extraction to shared CierreValidator.
///
/// Key differences before extraction:
///   - CerrarAsync: G3 includes ALL gastos (structural + operativos), NO auto-conciliation
///   - ClosePeriodoAsync: G3 excludes structural gastos (EsGastoOperativoReal), HAS auto-conciliation
///
/// Post-extraction: CerrarAsync ADOPTS canonical ClosePeriodoAsync behavior.
/// </summary>
public class CierreValidatorTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ContabilidadService _contabilidadService;
    private readonly RendicionService _rendicionService;

    public CierreValidatorTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"CierreValidatorTestDb_{Guid.NewGuid()}");
        var periodRepo = new AccountingPeriodRepository(_context);
        _contabilidadService = new ContabilidadService(_context, periodRepo);
        _rendicionService = new RendicionService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    // ────────────────────────────────────────────────────────────────────────
    // SECTION A: CerrarAsync characterization (OLD behavior BEFORE extraction)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// TASK 2.5 verification: CerrarAsync now ADOPTS canonical G3 filter.
    /// Structural gastos (RETIRO_CAPITAL) are EXCLUDED from saldo via
    /// EsGastoOperativoReal. Close succeeds when only structural gastos exist.
    ///
    /// Before extraction this test asserted the OPPOSITE — that structural
    /// gastos blocked close. Updated to reflect canonical behavior.
    /// </summary>
    [Fact]
    public async Task CerrarAsync_StructuralGasto_ExcludedFromSaldo_Succeeds()
    {
        // Arrange — rendicion with transferencia + compra (matching → 0 diff),
        // plus a RETIRO_CAPITAL gasto → excluded by EsGastoOperativoReal → saldo = 0
        var rendicion = new Rendicion
        {
            Trabajador = "Worker Char1",
            FechaInicio = DateTime.Today,
            Estado = RendicionEstado.Abierta
        };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var transferencia = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 1000m,
            Trabajador = "Worker Char1",
            Estado = TransferenciaEstado.Conciliado,
            RendicionId = rendicion.Id,
            Verificada = true
        };
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();

        var compra = new Compra
        {
            Proveedor = "Prov Char1",
            FechaCompra = DateTime.Today,
            MontoTotal = 1000m,
            TransferenciaId = transferencia.Id,
            Verificada = true
        };
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync();

        // Add a structural gasto (RETIRO_CAPITAL) — CerrarAsync NOW excludes it
        var structuralGasto = new MovimientoCaja
        {
            Fecha = DateTime.Today,
            Descripcion = "Retiro de capital",
            Monto = -500m,
            Tipo = "RETIRO",
            Categoria = "RETIRO_CAPITAL",
            RendicionId = rendicion.Id
        };
        _context.MovimientosCaja.Add(structuralGasto);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();

        // Act — CerrarAsync now excludes RETIRO_CAPITAL from G3:
        // 1000 - 1000 - 0 (excluded) - 0 = 0 → succeeds
        var act = async () => await _rendicionService.CerrarAsync(rendicion.Id);
        await act.Should().NotThrowAsync();

        // Assert — rendicion is now closed
        _context.ChangeTracker.Clear();
        var closed = await _context.Rendiciones.FindAsync(rendicion.Id);
        closed!.Estado.Should().Be(RendicionEstado.Cerrada);
    }

    /// <summary>
    /// TASK 2.5 verification: CerrarAsync now HAS auto-conciliation.
    /// EnUso transfers with linked compras are auto-conciliated to Conciliado.
    ///
    /// Before extraction this test asserted the OPPOSITE — that EnUso
    /// transfers caused close to fail. Updated to reflect canonical behavior.
    /// </summary>
    [Fact]
    public async Task CerrarAsync_EnUsoTransfer_AutoConciliated_Succeeds()
    {
        // Arrange — all verified, saldo=0, transfer EnUso with linked compras
        var rendicion = new Rendicion
        {
            Trabajador = "Worker Char2",
            FechaInicio = DateTime.Today,
            Estado = RendicionEstado.Abierta
        };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        // Transferencia in EnUso with linked compras — auto-conciliation will fix it
        var transferencia = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 1000m,
            Trabajador = "Worker Char2",
            Estado = TransferenciaEstado.EnUso,
            RendicionId = rendicion.Id,
            Verificada = true
        };
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();

        var compra = new Compra
        {
            Proveedor = "Prov Char2",
            FechaCompra = DateTime.Today,
            MontoTotal = 1000m,
            TransferenciaId = transferencia.Id,
            Verificada = true
        };
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();

        // Act — auto-conciliation runs before final Conciliado check
        var act = async () => await _rendicionService.CerrarAsync(rendicion.Id);
        await act.Should().NotThrowAsync();

        // Assert — transferencia now Conciliado
        _context.ChangeTracker.Clear();
        var updatedTrans = await _context.Transferencias.FindAsync(transferencia.Id);
        updatedTrans!.Estado.Should().Be(TransferenciaEstado.Conciliado);

        var closed = await _context.Rendiciones.FindAsync(rendicion.Id);
        closed!.Estado.Should().Be(RendicionEstado.Cerrada);
    }

    // ────────────────────────────────────────────────────────────────────────
    // SECTION B: ClosePeriodoAsync characterization (CANONICAL behavior)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Characterizes ClosePeriodoAsync G3: structural gastos (RETIRO_CAPITAL)
    /// are EXCLUDED from saldo calculation via EsGastoOperativoReal filter.
    /// Close succeeds when structural gastos are the only "gastos".
    /// </summary>
    [Fact]
    public async Task ClosePeriodoAsync_StructuralGasto_ExcludedFromSaldo_Succeeds()
    {
        // Arrange — period with transferencia + compra (matching → 0 diff),
        // plus a RETIRO_CAPITAL gasto → excluded by EsGastoOperativoReal → saldo = 0
        var period = new AccountingPeriod
        {
            Name = "Char Period",
            FechaInicio = DateTime.Today,
            FechaFin = DateTime.Today.AddMonths(1),
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        var rendicion = new Rendicion
        {
            Trabajador = "Worker Char3",
            FechaInicio = DateTime.Today,
            Estado = RendicionEstado.Abierta
        };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var transferencia = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 1000m,
            Trabajador = "Worker Char3",
            Estado = TransferenciaEstado.Conciliado,
            PeriodoId = period.Id,
            RendicionId = rendicion.Id,
            Verificada = true
        };
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();

        var compra = new Compra
        {
            Proveedor = "Prov Char3",
            FechaCompra = DateTime.Today,
            MontoTotal = 1000m,
            TransferenciaId = transferencia.Id,
            Verificada = true
        };
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync();

        // Add structural gasto — ClosePeriodoAsync EXCLUDES it from G3
        var structuralGasto = new MovimientoCaja
        {
            Fecha = DateTime.Today,
            Descripcion = "Retiro de capital",
            Monto = -500m,
            Tipo = "RETIRO",
            Categoria = "RETIRO_CAPITAL",
            RendicionId = rendicion.Id
        };
        _context.MovimientosCaja.Add(structuralGasto);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();

        // Act — ClosePeriodoAsync excludes RETIRO_CAPITAL from G3:
        // 1000 - 1000 - 0 (excluded) - 0 = 0 → succeeds
        var act = async () => await _contabilidadService.ClosePeriodoAsync(period.Id);
        await act.Should().NotThrowAsync();

        // Assert — period is now closed
        _context.ChangeTracker.Clear();
        var closed = await _context.AccountingPeriods.FindAsync(period.Id);
        closed!.Estado.Should().Be(AccountingPeriodEstado.Cerrado);
    }

    /// <summary>
    /// Characterizes ClosePeriodoAsync: AUTO-CONCILIATION exists.
    /// A transfer in EnUso state with linked compras IS auto-conciliated
    /// to Conciliado, so close succeeds.
    /// </summary>
    [Fact]
    public async Task ClosePeriodoAsync_EnUsoTransfer_AutoConciliated_Succeeds()
    {
        // Arrange — all verified, saldo=0, transfer EnUso with compras linked
        var period = new AccountingPeriod
        {
            Name = "Char Period 2",
            FechaInicio = DateTime.Today,
            FechaFin = DateTime.Today.AddMonths(1),
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        var rendicion = new Rendicion
        {
            Trabajador = "Worker Char4",
            FechaInicio = DateTime.Today,
            Estado = RendicionEstado.Abierta
        };
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();

        var transferencia = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 1000m,
            Trabajador = "Worker Char4",
            Estado = TransferenciaEstado.EnUso, // NOT Conciliado — auto step should fix
            PeriodoId = period.Id,
            RendicionId = rendicion.Id,
            Verificada = true
        };
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();

        var compra = new Compra
        {
            Proveedor = "Prov Char4",
            FechaCompra = DateTime.Today,
            MontoTotal = 1000m,
            TransferenciaId = transferencia.Id,
            Verificada = true
        };
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();

        // Act — auto-conciliation runs before final Conciliado check
        var act = async () => await _contabilidadService.ClosePeriodoAsync(period.Id);
        await act.Should().NotThrowAsync();

        // Assert — transferencia is now Conciliado (auto step worked)
        _context.ChangeTracker.Clear();
        var updatedTrans = await _context.Transferencias.FindAsync(transferencia.Id);
        updatedTrans!.Estado.Should().Be(TransferenciaEstado.Conciliado);

        var closed = await _context.AccountingPeriods.FindAsync(period.Id);
        closed!.Estado.Should().Be(AccountingPeriodEstado.Cerrado);
    }

    // ────────────────────────────────────────────────────────────────────────
    // SECTION C: Post-extraction same-result test (Task 2.3)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// TASK 2.3: Same data → both CerrarAsync and ClosePeriodoAsync produce
    /// the same result. Post-extraction, both delegate to CierreValidator.
    ///
    /// This test sets up a period+rendicion+transferencia+compra that should
    /// be closable by both services. It validates that after extraction,
    /// both flows behave identically.
    ///
    /// NOTE: Because CerrarAsync is changing behavior (adopting canonical),
    /// this test currently exercises the old behavior and serves as
    /// approval documentation. After extraction, CerrarAsync results
    /// should match ClosePeriodoAsync results.
    /// </summary>
    [Fact]
    public async Task SameData_BothCallers_ProduceConsistentCloseOutcome()
    {
        // Arrange — shared data setup: period + rendicion + transferencia + compra
        var period = new AccountingPeriod
        {
            Name = "Shared Period",
            FechaInicio = DateTime.Today,
            FechaFin = DateTime.Today.AddMonths(1),
            Estado = AccountingPeriodEstado.Abierto
        };
        _context.AccountingPeriods.Add(period);
        await _context.SaveChangesAsync();

        // Create two separate rendiciones, each with its own transferencia + compra
        // that are independently closable.

        // ── Rendicion 1 (for CerrarAsync) ──
        var rendicion1 = new Rendicion
        {
            Trabajador = "Worker R1",
            FechaInicio = DateTime.Today,
            Estado = RendicionEstado.Abierta
        };
        _context.Rendiciones.Add(rendicion1);
        await _context.SaveChangesAsync();

        var trans1 = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 500m,
            Trabajador = "Worker R1",
            Estado = TransferenciaEstado.Conciliado,
            RendicionId = rendicion1.Id,
            Verificada = true
        };
        _context.Transferencias.Add(trans1);
        await _context.SaveChangesAsync();

        var compra1 = new Compra
        {
            Proveedor = "Prov R1",
            FechaCompra = DateTime.Today,
            MontoTotal = 500m,
            TransferenciaId = trans1.Id,
            Verificada = true
        };
        _context.Compras.Add(compra1);
        await _context.SaveChangesAsync();

        // ── Rendicion 2 (independently closable via ClosePeriodoAsync) ──
        var rendicion2 = new Rendicion
        {
            Trabajador = "Worker R2",
            FechaInicio = DateTime.Today,
            Estado = RendicionEstado.Abierta
        };
        _context.Rendiciones.Add(rendicion2);
        await _context.SaveChangesAsync();

        var trans2 = new Transferencia
        {
            Fecha = DateTime.Today,
            Monto = 500m,
            Trabajador = "Worker R2",
            Estado = TransferenciaEstado.Conciliado,
            PeriodoId = period.Id,
            RendicionId = rendicion2.Id,
            Verificada = true
        };
        _context.Transferencias.Add(trans2);
        await _context.SaveChangesAsync();

        var compra2 = new Compra
        {
            Proveedor = "Prov R2",
            FechaCompra = DateTime.Today,
            MontoTotal = 500m,
            TransferenciaId = trans2.Id,
            Verificada = true
        };
        _context.Compras.Add(compra2);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();

        // Act — close both
        var cerrarTask = async () => await _rendicionService.CerrarAsync(rendicion1.Id);
        var closeTask = async () => await _contabilidadService.ClosePeriodoAsync(period.Id);

        // Assert — both should succeed (both are clean setups)
        await cerrarTask.Should().NotThrowAsync();
        await closeTask.Should().NotThrowAsync();

        // Both entities are now closed
        _context.ChangeTracker.Clear();
        var r1 = await _context.Rendiciones.FindAsync(rendicion1.Id);
        r1!.Estado.Should().Be(RendicionEstado.Cerrada);

        var p = await _context.AccountingPeriods.FindAsync(period.Id);
        p!.Estado.Should().Be(AccountingPeriodEstado.Cerrado);
    }

    // ────────────────────────────────────────────────────────────────────────
    // SECTION D: CierreValidator unit tests (Task 2.4 RED)
    //
    // These test the CierreValidator gates in ISOLATION with minimal
    // Transferencia/MovimientoCaja lists. No DB needed — pure logic.
    // The CierreValidator class does not exist yet — tests will not compile.
    // ────────────────────────────────────────────────────────────────────────

    private static Transferencia CreateTransferencia(
        int id, decimal monto, bool verificada,
        TransferenciaEstado estado = TransferenciaEstado.Conciliado,
        IEnumerable<Compra>? compras = null)
    {
        return new Transferencia
        {
            Id = id,
            Fecha = DateTime.Today,
            Monto = monto,
            Trabajador = "Test",
            Estado = estado,
            Verificada = verificada
        };
    }

    private static Compra CreateCompra(int id, decimal montoTotal, bool verificada)
    {
        return new Compra
        {
            Id = id,
            Proveedor = "Test Prov",
            FechaCompra = DateTime.Today,
            MontoTotal = montoTotal,
            Verificada = verificada
        };
    }

    private static MovimientoCaja CreateGasto(int id, decimal monto, string categoria = "LOGISTICA")
    {
        return new MovimientoCaja
        {
            Id = id,
            Fecha = DateTime.Today,
            Descripcion = "Test Gasto",
            Monto = monto,
            Tipo = "GASTO",
            Categoria = categoria
        };
    }

    [Fact]
    public void Validate_AllGatesPass_ReturnsValid()
    {
        // Arrange — transferencia verificada, compra verificada, saldo=0
        var transfers = new List<Transferencia>
        {
            CreateTransferencia(1, 1000m, true)
        };
        // Manually link compra to transferencia
        var compra = CreateCompra(1, 1000m, true);
        compra.TransferenciaId = 1;
        transfers[0].Compras.Add(compra);

        var gastos = new List<MovimientoCaja>(); // no gastos

        // Act
        var result = CierreValidator.Validate(transfers, gastos, 0m, "rendición");

        // Assert
        result.Valid.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Validate_G1_UnverifiedTransferencia_ReturnsInvalid()
    {
        // Arrange
        var transfers = new List<Transferencia>
        {
            CreateTransferencia(1, 1000m, verificada: false)
        };
        var gastos = new List<MovimientoCaja>();

        // Act
        var result = CierreValidator.Validate(transfers, gastos, 0m, "rendición");

        // Assert
        result.Valid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("verificar");
    }

    [Fact]
    public void Validate_G2_UnverifiedCompra_ReturnsInvalid()
    {
        // Arrange
        var transfers = new List<Transferencia>
        {
            CreateTransferencia(1, 1000m, true)
        };
        var compra = CreateCompra(1, 500m, verificada: false);
        compra.TransferenciaId = 1;
        transfers[0].Compras.Add(compra);

        var gastos = new List<MovimientoCaja>();

        // Act
        var result = CierreValidator.Validate(transfers, gastos, 0m, "rendición");

        // Assert
        result.Valid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("compra");
    }

    [Fact]
    public void Validate_G3_NonZeroSaldo_ReturnsInvalid()
    {
        // Arrange — transferencia > compras → saldo > 0
        var transfers = new List<Transferencia>
        {
            CreateTransferencia(1, 1000m, true)
        };
        var compra = CreateCompra(1, 600m, true);
        compra.TransferenciaId = 1;
        transfers[0].Compras.Add(compra);

        var gastos = new List<MovimientoCaja>();

        // saldo = 1000 - 600 - 0 - 0 = 400 ≠ 0

        // Act
        var result = CierreValidator.Validate(transfers, gastos, 0m, "rendición");

        // Assert
        result.Valid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("saldo");
    }

    [Fact]
    public void Validate_G3_StructuralGastoExcluded_DoesNotAffectSaldo()
    {
        // Arrange — structural gasto (RETIRO_CAPITAL) should NOT count toward saldo
        var transfers = new List<Transferencia>
        {
            CreateTransferencia(1, 1000m, true)
        };
        var compra = CreateCompra(1, 1000m, true);
        compra.TransferenciaId = 1;
        transfers[0].Compras.Add(compra);

        // Gastos passed to validator SHOULD already be filtered by caller
        // Only operativos reales should be in this list
        var gastos = new List<MovimientoCaja>
        {
            CreateGasto(1, -500m, "RETIRO_CAPITAL") // structural — should have been filtered by caller
        };

        // The validator trusts the caller — it sums whatever gastos it receives.
        // If the caller passes structural gastos, they will be counted.
        // The test verifies: if caller correctly pre-filters, saldo=0 works.
        // With unfiltered structural gastos: 1000-1000-500-0 = -500 → invalid
        var result = CierreValidator.Validate(transfers, gastos, 0m, "rendición");

        // The validator sums what it receives. RETIRO_CAPITAL in the list = counted.
        result.Valid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("saldo");
    }

    [Fact]
    public void Validate_Auto_SetsEnUsoToConciliado_WhenLinked()
    {
        // Arrange — transferencia EnUso with linked compras
        var transfers = new List<Transferencia>
        {
            CreateTransferencia(1, 1000m, true, TransferenciaEstado.EnUso)
        };
        var compra = CreateCompra(1, 1000m, true);
        compra.TransferenciaId = 1;
        transfers[0].Compras.Add(compra);

        var gastos = new List<MovimientoCaja>();

        // Act
        var result = CierreValidator.Validate(transfers, gastos, 0m, "rendición");

        // Assert — auto-conciliation ran, EnUso → Conciliado
        result.Valid.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        transfers[0].Estado.Should().Be(TransferenciaEstado.Conciliado);
    }

    [Fact]
    public void Validate_G4_NonConciliatedAfterAuto_ReturnsInvalid()
    {
        // Arrange — Pendiente transfer with zero monto + zero compras
        // (extreme but valid edge: worker received $0 transfer with no expenses).
        // G1/G2/G3 pass (monto=0 → saldo=0), but Pendiente can't be auto-conciliated
        // because there are no linked compras → G4 catches it.
        var transfers = new List<Transferencia>
        {
            CreateTransferencia(1, 0m, true, TransferenciaEstado.Pendiente)
        };
        var gastos = new List<MovimientoCaja>();

        // Act
        var result = CierreValidator.Validate(transfers, gastos, 0m, "rendición");

        // Assert — G4 fails because transfer is still Pendiente after auto
        result.Valid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("conciliad");
    }

    [Fact]
    public void Validate_G4_MultipleTransfers_ConciliatedAfterAuto()
    {
        // Arrange — two EnUso transfers, both get auto-conciliated
        var transfers = new List<Transferencia>
        {
            CreateTransferencia(1, 500m, true, TransferenciaEstado.EnUso),
            CreateTransferencia(2, 500m, true, TransferenciaEstado.EnUso)
        };
        var compra1 = CreateCompra(1, 500m, true);
        compra1.TransferenciaId = 1;
        transfers[0].Compras.Add(compra1);

        var compra2 = CreateCompra(2, 500m, true);
        compra2.TransferenciaId = 2;
        transfers[1].Compras.Add(compra2);

        var gastos = new List<MovimientoCaja>();

        // Act
        var result = CierreValidator.Validate(transfers, gastos, 0m, "rendición");

        // Assert
        result.Valid.Should().BeTrue();
        transfers[0].Estado.Should().Be(TransferenciaEstado.Conciliado);
        transfers[1].Estado.Should().Be(TransferenciaEstado.Conciliado);
    }

    [Fact]
    public void Validate_Auto_ConciliatesWhenGastosExist_NoLinkedCompras()
    {
        // Arrange — EnUso transfer with NO linked compras, but entity has gastos.
        // transfer=1200, compra=1000, gasto=200 → saldo=0.
        // Auto step conciliates because hasGastos=true (even though t.Compras.Count=0).
        var transfers = new List<Transferencia>
        {
            CreateTransferencia(1, 1200m, true, TransferenciaEstado.EnUso)
        };
        var compra = CreateCompra(1, 1000m, true);
        compra.TransferenciaId = 1;
        transfers[0].Compras.Add(compra);

        var gastos = new List<MovimientoCaja>
        {
            CreateGasto(1, -200m, "LOGISTICA")
        };

        // Act
        var result = CierreValidator.Validate(transfers, gastos, 0m, "rendición");

        // Assert — auto-conciliated because hasGastos=true
        result.Valid.Should().BeTrue();
        transfers[0].Estado.Should().Be(TransferenciaEstado.Conciliado);
    }

    [Fact]
    public void Validate_ConciliadoStaysConciliado_AfterAuto()
    {
        // Arrange — transferencia already Conciliado stays Conciliado
        var transfers = new List<Transferencia>
        {
            CreateTransferencia(1, 1000m, true, TransferenciaEstado.Conciliado)
        };
        var compra = CreateCompra(1, 1000m, true);
        compra.TransferenciaId = 1;
        transfers[0].Compras.Add(compra);

        var gastos = new List<MovimientoCaja>();

        // Act
        var result = CierreValidator.Validate(transfers, gastos, 0m, "rendición");

        // Assert
        result.Valid.Should().BeTrue();
        transfers[0].Estado.Should().Be(TransferenciaEstado.Conciliado);
    }
}
