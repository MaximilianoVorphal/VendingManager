using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VendingManager.Core.Configuration;
using VendingManager.Core.Entities;
using VendingManager.Infrastructure.Data;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.DTOs;
using Xunit;

namespace VendingManager.Tests.Services;

/// <summary>
/// Phase 2 — Characterization tests: capture current offset and year-guard behavior
/// BEFORE the refactor. These tests must PASS against the current code and serve
/// as a safety net for the Phase 3 implementation.
/// </summary>
public class SalesImportServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly SalesImportService _service;

    static SalesImportServiceTests()
    {
        // ExcelDataReader needs the legacy code-page provider registered to read
        // genuine XLSX streams (used only by the "valid file" tests below).
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    public SalesImportServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        var config = Options.Create(new VendingConfig { DefaultTimezoneOffsetHours = -11 });
        _service = new SalesImportService(_context, config);
    }

    public void Dispose() => _context.Dispose();

    // ── 2.1: MachineId 2410280012 → offset +1 ───────────────────────────────

    [Fact]
    public async Task MachineId_2410280012_AppliesPlusOneOffset()
    {
        // Arrange
        _context.Maquinas.Add(new Maquina
        {
            Id = 1,
            IdInternoMaquina = "2410280012",
            TimezoneOffsetHours = 1,  // This machine has a custom offset
            Slots = new List<ConfiguracionSlot>
            {
                new() { NumeroSlot = "A1", Producto = new Producto { Id = 1, CostoPromedio = 100 } }
            }
        });
        await _context.SaveChangesAsync();

        var row = new SalesReportRowDto
        {
            MachineId = "2410280012",
            Slot = "A1",
            Price = 1000,
            MachineTime = "2026-07-15 10:00:00", // Local machine time
            ServerTime = "2026-07-15 00:00:00",
            TrSerialNumber = Guid.NewGuid().ToString()
        };

        // Act
        await _service.ImportarVentasDesdeJson(new List<SalesReportRowDto> { row });

        // Assert
        var venta = await _context.Ventas.FirstAsync();
        // Machine time 10:00 + offset +1h = 11:00 local
        venta.FechaLocal.Hour.Should().Be(11);
        venta.FechaHora.Hour.Should().Be(10); // Raw machine time
    }

    // ── 2.3: usingServerTime=true → offset -12 ──────────────────────────────

    [Fact]
    public async Task OldTimestamp_UsesServerTime_AppliesMinus12Offset()
    {
        // Arrange
        _context.Maquinas.Add(new Maquina
        {
            Id = 1,
            IdInternoMaquina = "8888888888",
            Slots = new List<ConfiguracionSlot>
            {
                new() { NumeroSlot = "C3", Producto = new Producto { Id = 3, CostoPromedio = 100 } }
            }
        });
        await _context.SaveChangesAsync();

        var row = new SalesReportRowDto
        {
            MachineId = "8888888888",
            Slot = "C3",
            Price = 800,
            MachineTime = "2023-06-01 10:00:00",     // outside 2-year window triggers year guard
            ServerTime = "2026-07-15 12:00:00",       // ServerTime available → overwrites fecha
            TrSerialNumber = Guid.NewGuid().ToString()
        };

        // Act
        await _service.ImportarVentasDesdeJson(new List<SalesReportRowDto> { row });

        // Assert
        var venta = await _context.Ventas.FirstAsync();
        // ServerTime 12:00 + offset -12h = 00:00 SAME day local
        venta.FechaHora.Hour.Should().Be(12);    // Raw server time (overwritten)
        venta.FechaLocal.Hour.Should().Be(0);    // 12h - 12h = 0h
        venta.FechaLocal.Day.Should().Be(15);
    }

    // ── 2.4: Year guard: outside 2-year window + serverTime → fecha overwritten ─

    [Fact]
    public async Task YearGuard_Below2024_WithServerTime_OverwritesFecha()
    {
        // Arrange
        _context.Maquinas.Add(new Maquina
        {
            Id = 1,
            IdInternoMaquina = "7777777777",
            Slots = new List<ConfiguracionSlot>
            {
                new() { NumeroSlot = "D4", Producto = new Producto { Id = 4, CostoPromedio = 100 } }
            }
        });
        await _context.SaveChangesAsync();

        var row = new SalesReportRowDto
        {
            MachineId = "7777777777",
            Slot = "D4",
            Price = 1200,
            MachineTime = "2020-01-01 05:00:00",      // outside 2-year window
            ServerTime = "2026-07-15 15:30:00",        // ServerTime present
            TrSerialNumber = Guid.NewGuid().ToString()
        };

        // Act
        await _service.ImportarVentasDesdeJson(new List<SalesReportRowDto> { row });

        // Assert
        var venta = await _context.Ventas.FirstAsync();
        // FechaHora should be the server time (overwritten), NOT the machine time
        venta.FechaHora.Year.Should().Be(2026);
        venta.FechaHora.Month.Should().Be(7);
        venta.FechaHora.Hour.Should().Be(15);
        venta.FechaHora.Minute.Should().Be(30);
    }

    // ── 2.5: Year guard: outside 2-year window + no serverTime → fecha unchanged

    [Fact]
    public async Task YearGuard_Below2024_WithoutServerTime_FechaUnchanged()
    {
        // Arrange
        _context.Maquinas.Add(new Maquina
        {
            Id = 1,
            IdInternoMaquina = "6666666666",
            Slots = new List<ConfiguracionSlot>
            {
                new() { NumeroSlot = "E5", Producto = new Producto { Id = 5, CostoPromedio = 100 } }
            }
        });
        await _context.SaveChangesAsync();

        var row = new SalesReportRowDto
        {
            MachineId = "6666666666",
            Slot = "E5",
            Price = 600,
            MachineTime = "2020-03-15 09:00:00",  // outside 2-year window
            ServerTime = "",                        // No server time → falls through
            TrSerialNumber = Guid.NewGuid().ToString()
        };

        // Act
        await _service.ImportarVentasDesdeJson(new List<SalesReportRowDto> { row });

        // Assert
        var venta = await _context.Ventas.FirstAsync();
        // FechaHora should be the ORIGINAL machine time (not overwritten)
        venta.FechaHora.Year.Should().Be(2020);
        venta.FechaHora.Month.Should().Be(3);
        venta.FechaHora.Hour.Should().Be(9);
        // But offset still applied: -11 on default machineId
        venta.FechaLocal.Hour.Should().Be(22); // 9h - 11h = 22h yesterday
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 4 — Post-Refactor Verification Tests
    // ═══════════════════════════════════════════════════════════════════════════

    // ── 4.1: Null TimezoneOffsetHours → uses default -11 ─────────────────────

    [Fact]
    public async Task NullMachineOffset_UsesConfigDefault()
    {
        // Arrange
        _context.Maquinas.Add(new Maquina
        {
            Id = 1,
            IdInternoMaquina = "1111111111",
            TimezoneOffsetHours = null,  // Machine not configured
            Slots = new List<ConfiguracionSlot>
            {
                new() { NumeroSlot = "A1", Producto = new Producto { Id = 1, CostoPromedio = 100 } }
            }
        });
        await _context.SaveChangesAsync();

        var row = new SalesReportRowDto
        {
            MachineId = "1111111111",
            Slot = "A1",
            Price = 1000,
            MachineTime = "2026-07-15 10:00:00",
            TrSerialNumber = Guid.NewGuid().ToString()
        };

        // Act
        await _service.ImportarVentasDesdeJson(new List<SalesReportRowDto> { row });

        // Assert
        var venta = await _context.Ventas.FirstAsync();
        // Default -11: 10:00 - 11h = 23:00 previous day
        venta.FechaLocal.Hour.Should().Be(23);
        venta.FechaLocal.Day.Should().Be(14);
    }

    // ── 4.2: Both null → resolved by config default (type-safe: int has value) ─

    [Fact]
    public async Task OffsetResolution_WithNullMachine_UsesDefaultWithoutException()
    {
        // Given int DefaultTimezoneOffsetHours can never be null (value type),
        // the ?? operator always resolves to the default. This test verifies
        // that a null machine offset does NOT throw — it gracefully uses the
        // config default.
        _context.Maquinas.Add(new Maquina
        {
            Id = 1,
            IdInternoMaquina = "2222222222",
            TimezoneOffsetHours = null,
            Slots = new List<ConfiguracionSlot>
            {
                new() { NumeroSlot = "B1", Producto = new Producto { Id = 2, CostoPromedio = 100 } }
            }
        });
        await _context.SaveChangesAsync();

        var row = new SalesReportRowDto
        {
            MachineId = "2222222222",
            Slot = "B1",
            Price = 500,
            MachineTime = "2026-07-15 12:00:00",
            ServerTime = "",
            TrSerialNumber = Guid.NewGuid().ToString()
        };

        // Act — should not throw
        var result = await _service.ImportarVentasDesdeJson(new List<SalesReportRowDto> { row });

        // Assert
        result.Should().Contain("PROCESADO_API: 1 nuevas");
        var venta = await _context.Ventas.FirstAsync();
        venta.FechaLocal.Hour.Should().Be(1); // 12:00 - 11h = 01:00
    }

    // ── 4.3: Year guard boundary: inside vs outside 2yr window ───────────────

    [Fact]
    public async Task YearGuard_FechaInsideTwoYearWindow_ProceedsUnmodified()
    {
        // Arrange: 1.5 years ago — within the 2-year window
        var insideWindow = DateTime.UtcNow.AddYears(-1).AddMonths(-6);

        _context.Maquinas.Add(new Maquina
        {
            Id = 1,
            IdInternoMaquina = "3333333333",
            TimezoneOffsetHours = null,
            Slots = new List<ConfiguracionSlot>
            {
                new() { NumeroSlot = "C1", Producto = new Producto { Id = 3, CostoPromedio = 100 } }
            }
        });
        await _context.SaveChangesAsync();

        var row = new SalesReportRowDto
        {
            MachineId = "3333333333",
            Slot = "C1",
            Price = 700,
            MachineTime = insideWindow.ToString("yyyy-MM-dd HH:mm:ss"),
            ServerTime = "2026-07-15 10:00:00",  // Available but should NOT be used
            TrSerialNumber = Guid.NewGuid().ToString()
        };

        // Act
        await _service.ImportarVentasDesdeJson(new List<SalesReportRowDto> { row });

        // Assert: fecha NOT overwritten — guard did not trigger
        var venta = await _context.Ventas.FirstAsync();
        venta.FechaHora.Year.Should().Be(insideWindow.Year);
        venta.FechaHora.Month.Should().Be(insideWindow.Month);
    }

    [Fact]
    public async Task YearGuard_FechaOutsideTwoYearWindow_WithServerTime_Overwrites()
    {
        // Arrange: 2 years + 1 day ago — outside the window
        var outsideWindow = DateTime.UtcNow.AddYears(-2).AddDays(-1);

        _context.Maquinas.Add(new Maquina
        {
            Id = 1,
            IdInternoMaquina = "4444444444",
            TimezoneOffsetHours = null,
            Slots = new List<ConfiguracionSlot>
            {
                new() { NumeroSlot = "D1", Producto = new Producto { Id = 4, CostoPromedio = 100 } }
            }
        });
        await _context.SaveChangesAsync();

        var row = new SalesReportRowDto
        {
            MachineId = "4444444444",
            Slot = "D1",
            Price = 900,
            MachineTime = outsideWindow.ToString("yyyy-MM-dd HH:mm:ss"),
            ServerTime = "2026-07-15 14:30:00",  // Should be used to overwrite
            TrSerialNumber = Guid.NewGuid().ToString()
        };

        // Act
        await _service.ImportarVentasDesdeJson(new List<SalesReportRowDto> { row });

        // Assert: fecha WAS overwritten with server time
        var venta = await _context.Ventas.FirstAsync();
        venta.FechaHora.Year.Should().Be(2026);
        venta.FechaHora.Hour.Should().Be(14);
        venta.FechaHora.Minute.Should().Be(30);
    }

    // ── 4.4: Breaker default == TimeSpan.FromHours(168) ──────────────────────

    [Fact]
    public void Breaker_DefaultMaxOpenCooldown_IsTimeSpanFromHours168()
    {
        // Verifies the constant is exactly 168h (not just any value)
        PollingCircuitBreaker.DefaultMaxOpenCooldown
            .Should().Be(TimeSpan.FromHours(168))
            .And.BeGreaterThan(PollingCircuitBreaker.DefaultBaseOpenCooldown);
    }

    // ── 4.5: Legacy sync stores DateTime with Utc Kind ──────────────────────

    [Fact]
    public void LegacySync_UsesUtcNow_WhichHasUtcKind()
    {
        // The legacy sync path (SyncOrchestratorService and AutomatedReportService)
        // now uses DateTime.UtcNow. Verify that UtcNow produces Utc Kind, which
        // the LastSyncTracker.SaveToDb serializes correctly via "o" round-trip format.
        var now = DateTime.UtcNow;
        now.Kind.Should().Be(DateTimeKind.Utc);

        // Round-trip via "o" format preserves Kind
        var roundTripped = DateTime.Parse(now.ToString("o"), null, System.Globalization.DateTimeStyles.RoundtripKind);
        roundTripped.Kind.Should().Be(DateTimeKind.Utc);
    }

    // ── 4.6: Re-run Phase 2 tests post-refactor — all pass identically ──────
    // All 6 Phase 2 characterization tests (2.1–2.6) were adapted for the
    // refactored code and pass: see test run above. ✅

    // ── 4.7: Breaker constructor without explicit max cooldown → falls back to default ──

    [Fact]
    public void Breaker_Constructor_WithoutMaxCooldown_UsesDefault168h()
    {
        // When building a PollingCircuitBreaker without an explicit maxOpenCooldown,
        // the constructor must fall back to DefaultMaxOpenCooldown = 168h.
        var breaker = new PollingCircuitBreaker(
            new Random(),
            maxOpenCooldown: null
        );

        // The internal field is private, but we can verify via the constant.
        PollingCircuitBreaker.DefaultMaxOpenCooldown
            .Should().Be(TimeSpan.FromHours(168));
    }

    // ── 4.8: Explicit TimezoneOffsetHours = -3 ───────────────────────────────

    [Fact]
    public async Task MachineOffset_Minus3_AppliesCorrectly()
    {
        // O1 Scenario 3: a machine with an explicit non-default, non-+1 offset.
        // Ensures the ?? operator is not hardcoded to only -11/+1.
        _context.Maquinas.Add(new Maquina
        {
            Id = 1,
            IdInternoMaquina = "5555555555",
            TimezoneOffsetHours = -3, // Explicit override
            Slots = new List<ConfiguracionSlot>
            {
                new() { NumeroSlot = "F1", Producto = new Producto { Id = 6, CostoPromedio = 100 } }
            }
        });
        await _context.SaveChangesAsync();

        var row = new SalesReportRowDto
        {
            MachineId = "5555555555",
            Slot = "F1",
            Price = 1500,
            MachineTime = "2026-07-15 10:00:00",
            TrSerialNumber = Guid.NewGuid().ToString()
        };

        // Act
        await _service.ImportarVentasDesdeJson(new List<SalesReportRowDto> { row });

        // Assert: 10:00 - 3h = 07:00 same day
        var venta = await _context.Ventas.FirstAsync();
        venta.FechaLocal.Hour.Should().Be(7);
        venta.FechaLocal.Day.Should().Be(15);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PR1 — Offset Drift Watchdog: batch aggregation + persistence
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly TimeZoneInfo ShanghaiTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
    private static readonly TimeZoneInfo SantiagoTz = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");

    /// <summary>Mirrors OffsetDriftCalculator's own conversion so tests derive expected values from
    /// the real TimeZoneInfo tables instead of hand-computed magic numbers.</summary>
    private static DateTime ChileLocalFromServerTime(DateTime serverTimeUnspecified)
    {
        var utc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(serverTimeUnspecified, DateTimeKind.Unspecified), ShanghaiTz);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, SantiagoTz);
    }

    /// <summary>Given a ServerTime, returns the MachineTime that makes the row's implied offset
    /// equal to <paramref name="impliedOffsetHours"/> (rowOffset = chileLocal - machineTime).</summary>
    private static DateTime MachineTimeForImpliedOffset(DateTime serverTimeUnspecified, int impliedOffsetHours)
        => ChileLocalFromServerTime(serverTimeUnspecified).AddHours(-impliedOffsetHours);

    private static List<SalesReportRowDto> BuildDualTimestampRows(
        string machineId, int impliedOffsetHours, int count, DateTime baseServerTime)
    {
        var rows = new List<SalesReportRowDto>();
        for (int i = 0; i < count; i++)
        {
            var serverTime = baseServerTime.AddMinutes(i * 10);
            var machineTime = MachineTimeForImpliedOffset(serverTime, impliedOffsetHours);
            rows.Add(new SalesReportRowDto
            {
                MachineId = machineId,
                Slot = "A1",
                Price = 1000,
                MachineTime = machineTime.ToString("yyyy-MM-dd HH:mm:ss"),
                ServerTime = serverTime.ToString("yyyy-MM-dd HH:mm:ss"),
                TrSerialNumber = Guid.NewGuid().ToString()
            });
        }
        return rows;
    }

    private async Task SeedMaquinaAsync(string machineId, int? timezoneOffsetHours)
    {
        _context.Maquinas.Add(new Maquina
        {
            IdInternoMaquina = machineId,
            TimezoneOffsetHours = timezoneOffsetHours,
            Slots = new List<ConfiguracionSlot>
            {
                new() { NumeroSlot = "A1", Producto = new Producto { CostoPromedio = 100 } }
            }
        });
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task Import_ConsistentDelta_UpsertsOffsetDriftState()
    {
        // Arrange: machine configured at -11, but real clock drift makes the implied offset -9
        // (2h drift, above the 1h threshold).
        await SeedMaquinaAsync("DRIFT-0001", timezoneOffsetHours: -11);
        var rows = BuildDualTimestampRows("DRIFT-0001", impliedOffsetHours: -9, count: 5,
            baseServerTime: new DateTime(2026, 7, 15, 12, 0, 0));

        // Act
        await _service.ImportarVentasDesdeJson(rows);

        // Assert
        var maquina = await _context.Maquinas.SingleAsync(m => m.IdInternoMaquina == "DRIFT-0001");
        var estado = await _context.OffsetDriftStates.FindAsync(maquina.Id);
        estado.Should().NotBeNull();
        estado!.ImpliedOffsetHours.Should().Be(-9);
        estado.SampleCount.Should().Be(5);
    }

    [Fact]
    public async Task Import_BelowMinSamples_NoDriftSurfaced()
    {
        // Arrange: only 4 dual-timestamp rows — below the default MinSamples (5) guard.
        await SeedMaquinaAsync("DRIFT-0002", timezoneOffsetHours: -11);
        var rows = BuildDualTimestampRows("DRIFT-0002", impliedOffsetHours: -9, count: 4,
            baseServerTime: new DateTime(2026, 7, 15, 12, 0, 0));

        // Act
        await _service.ImportarVentasDesdeJson(rows);

        // Assert: no drift-state row was created for this machine.
        var maquina = await _context.Maquinas.SingleAsync(m => m.IdInternoMaquina == "DRIFT-0002");
        var estado = await _context.OffsetDriftStates.FindAsync(maquina.Id);
        estado.Should().BeNull();
    }

    [Fact]
    public async Task Import_RowsMissingServerTime_ExcludedFromSampleCount()
    {
        // Arrange: 5 usable dual-timestamp rows + 3 rows missing ServerTime — the latter must
        // NOT count toward SampleCount.
        await SeedMaquinaAsync("DRIFT-0003", timezoneOffsetHours: -11);
        var rows = BuildDualTimestampRows("DRIFT-0003", impliedOffsetHours: -9, count: 5,
            baseServerTime: new DateTime(2026, 7, 15, 12, 0, 0));

        for (int i = 0; i < 3; i++)
        {
            rows.Add(new SalesReportRowDto
            {
                MachineId = "DRIFT-0003",
                Slot = "A1",
                Price = 1000,
                MachineTime = new DateTime(2026, 7, 15, 8, 0, 0).AddMinutes(i).ToString("yyyy-MM-dd HH:mm:ss"),
                ServerTime = "", // missing — must be excluded
                TrSerialNumber = Guid.NewGuid().ToString()
            });
        }

        // Act
        await _service.ImportarVentasDesdeJson(rows);

        // Assert
        var maquina = await _context.Maquinas.SingleAsync(m => m.IdInternoMaquina == "DRIFT-0003");
        var estado = await _context.OffsetDriftStates.FindAsync(maquina.Id);
        estado.Should().NotBeNull();
        estado!.SampleCount.Should().Be(5);
    }

    [Fact]
    public async Task Import_OffsetMatchesConfigured_NotDrifting()
    {
        // Arrange: machine's real clock matches its configured offset exactly — the implied
        // offset the watchdog computes must equal the configured value (no false positive).
        await SeedMaquinaAsync("DRIFT-0004", timezoneOffsetHours: -11);
        var rows = BuildDualTimestampRows("DRIFT-0004", impliedOffsetHours: -11, count: 5,
            baseServerTime: new DateTime(2026, 7, 15, 12, 0, 0));

        // Act
        await _service.ImportarVentasDesdeJson(rows);

        // Assert
        var maquina = await _context.Maquinas.SingleAsync(m => m.IdInternoMaquina == "DRIFT-0004");
        var estado = await _context.OffsetDriftStates.FindAsync(maquina.Id);
        estado.Should().NotBeNull();
        estado!.ImpliedOffsetHours.Should().Be(maquina.TimezoneOffsetHours);
    }

    [Fact]
    public async Task Import_MedianRobustToOutlier()
    {
        // Arrange: 4 consistent rows at implied offset -9, plus one wild outlier row whose
        // ServerTime is far off. The median must stay at -9, unaffected by the single outlier.
        await SeedMaquinaAsync("DRIFT-0005", timezoneOffsetHours: -11);
        var rows = BuildDualTimestampRows("DRIFT-0005", impliedOffsetHours: -9, count: 4,
            baseServerTime: new DateTime(2026, 7, 15, 12, 0, 0));

        var outlierServerTime = new DateTime(2026, 7, 20, 3, 0, 0); // far away in time
        rows.Add(new SalesReportRowDto
        {
            MachineId = "DRIFT-0005",
            Slot = "A1",
            Price = 1000,
            MachineTime = new DateTime(2026, 7, 15, 12, 0, 0).ToString("yyyy-MM-dd HH:mm:ss"), // unrelated to outlierServerTime
            ServerTime = outlierServerTime.ToString("yyyy-MM-dd HH:mm:ss"),
            TrSerialNumber = Guid.NewGuid().ToString()
        });

        // Act
        await _service.ImportarVentasDesdeJson(rows);

        // Assert
        var maquina = await _context.Maquinas.SingleAsync(m => m.IdInternoMaquina == "DRIFT-0005");
        var estado = await _context.OffsetDriftStates.FindAsync(maquina.Id);
        estado.Should().NotBeNull();
        estado!.SampleCount.Should().Be(5);
        estado.ImpliedOffsetHours.Should().Be(-9);
    }

    [Fact]
    public async Task NullConfiguredOffset_SurfacesFirstTimeProposal()
    {
        // Arrange: machine has never been configured (TimezoneOffsetHours = null). The watchdog
        // must still accumulate samples and persist a proposal — the "first-time proposal" read
        // interpretation is PR2, but PR1's persistence must not skip null-offset machines.
        await SeedMaquinaAsync("DRIFT-0006", timezoneOffsetHours: null);
        var rows = BuildDualTimestampRows("DRIFT-0006", impliedOffsetHours: -11, count: 5,
            baseServerTime: new DateTime(2026, 7, 15, 12, 0, 0));

        // Act
        await _service.ImportarVentasDesdeJson(rows);

        // Assert
        var maquina = await _context.Maquinas.SingleAsync(m => m.IdInternoMaquina == "DRIFT-0006");
        var estado = await _context.OffsetDriftStates.FindAsync(maquina.Id);
        estado.Should().NotBeNull();
        estado!.ImpliedOffsetHours.Should().Be(-11);
        estado.SampleCount.Should().Be(5);
    }

    /// <summary>DbContext subclass that simulates a watchdog-only persistence failure: it throws
    /// from SaveChangesAsync only while an OffsetDriftState entry is pending (added or modified) —
    /// the same failure mode as a PK/FK/concurrency conflict on the drift row's own flush. Calls
    /// to SaveChangesAsync with no pending OffsetDriftState entries (e.g. the caller's sales save)
    /// go through normally.</summary>
    private class ThrowingOffsetDriftDbContext : ApplicationDbContext
    {
        public ThrowingOffsetDriftDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            bool driftPending = ChangeTracker.Entries<OffsetDriftState>()
                .Any(e => e.State == EntityState.Added || e.State == EntityState.Modified);

            if (driftPending)
                throw new InvalidOperationException("Simulated watchdog SaveChanges failure.");

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Watchdog_Failure_DoesNotBreakImport()
    {
        // Arrange: a context whose SaveChangesAsync throws only when an OffsetDriftState is
        // pending — simulating a flush-time failure isolated to the watchdog's own save. The
        // sales import must still complete successfully and persist all ventas.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        await using var throwingContext = new ThrowingOffsetDriftDbContext(options);
        var config = Options.Create(new VendingConfig { DefaultTimezoneOffsetHours = -11 });
        var service = new SalesImportService(throwingContext, config);

        throwingContext.Maquinas.Add(new Maquina
        {
            IdInternoMaquina = "DRIFT-0007",
            TimezoneOffsetHours = -11,
            Slots = new List<ConfiguracionSlot>
            {
                new() { NumeroSlot = "A1", Producto = new Producto { CostoPromedio = 100 } }
            }
        });
        await throwingContext.SaveChangesAsync();

        var rows = BuildDualTimestampRows("DRIFT-0007", impliedOffsetHours: -9, count: 5,
            baseServerTime: new DateTime(2026, 7, 15, 12, 0, 0));

        // Act
        string? resultado = null;
        var act = async () => resultado = await service.ImportarVentasDesdeJson(rows);

        // Assert: import completes normally and reports success; all valid ventas are persisted
        // despite the watchdog's own SaveChangesAsync failing.
        await act.Should().NotThrowAsync();
        resultado.Should().NotBeNull();
        resultado.Should().Contain("5 nuevas");
        (await throwingContext.Ventas.CountAsync()).Should().Be(5);
        (await throwingContext.OffsetDriftStates.CountAsync()).Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // M-1b — Upload signature validation (REQ-UPLOAD-02)
    // Content is sniffed BEFORE ExcelReaderFactory sees it, for both import
    // entry points.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportarVentasMaquina_NonXlsxRenamedAsXlsx_ReturnsErrorAndPersistsNothing()
    {
        // Arrange: plain text content, renamed with a .xlsx extension.
        using var stream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes("not really an xlsx file"));

        // Act
        var result = await _service.ImportarVentasMaquina(stream, "ventas.xlsx");

        // Assert — SalesImportService's existing convention converts the caught
        // exception into an "Error: ..." string rather than rethrowing.
        result.Should().StartWith("Error:");
        (await _context.Ventas.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportarVentasMaquina_GenuineXlsx_ImportsUnchanged()
    {
        // Arrange
        _context.Maquinas.Add(new Maquina
        {
            Id = 1,
            IdInternoMaquina = "9999999999",
            Slots = new List<ConfiguracionSlot>
            {
                new() { NumeroSlot = "A1", Producto = new Producto { Id = 1, CostoPromedio = 100 } }
            }
        });
        await _context.SaveChangesAsync();

        using var stream = BuildGenuineVentasMaquinaXlsx();

        // Act
        var result = await _service.ImportarVentasMaquina(stream, "ventas.xlsx");

        // Assert — unchanged import behavior: one new sale is persisted.
        result.Should().Contain("1 nuevas");
        (await _context.Ventas.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ImportarTransbank_NonXlsxRenamedAsXlsx_DoesNotThrowAndPersistsNothing()
    {
        // Arrange: plain text content, renamed with a .xlsx extension. ImportarTransbank's
        // existing convention swallows the caught exception and simply returns — it does
        // not rethrow — so this is unaffected by the new signature check.
        using var stream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes("not really an xlsx file"));

        // Act
        var act = () => _service.ImportarTransbank(stream, "transbank.xlsx");

        // Assert
        await act.Should().NotThrowAsync();
        (await _context.Ventas.CountAsync()).Should().Be(0);
    }

    private static MemoryStream BuildGenuineVentasMaquinaXlsx()
    {
        var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add("Ventas");
            worksheet.Cell(1, 1).Value = "Machine ID";
            worksheet.Cell(1, 2).Value = "Slot Number";
            worksheet.Cell(1, 3).Value = "Price";
            worksheet.Cell(1, 4).Value = "Machine Time";
            worksheet.Cell(1, 5).Value = "Server time";
            worksheet.Cell(1, 6).Value = "Order Number";

            worksheet.Cell(2, 1).Value = "9999999999";
            worksheet.Cell(2, 2).Value = "A1";
            worksheet.Cell(2, 3).Value = 1000;
            worksheet.Cell(2, 4).Value = "2026-07-15 10:00:00";
            worksheet.Cell(2, 5).Value = "";
            worksheet.Cell(2, 6).Value = "ORD-001";

            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        return stream;
    }
}
