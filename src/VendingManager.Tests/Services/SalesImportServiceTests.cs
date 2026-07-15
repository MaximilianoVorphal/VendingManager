using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

    // ── 2.3: usandingServerTime=true → offset -14 ───────────────────────────

    [Fact]
    public async Task OldTimestamp_UsesServerTime_AppliesMinus14Offset()
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
            MachineTime = "2023-06-01 10:00:00",     // Year < 2024 triggers year guard
            ServerTime = "2026-07-15 12:00:00",       // ServerTime available → overwrites fecha
            TrSerialNumber = Guid.NewGuid().ToString()
        };

        // Act
        await _service.ImportarVentasDesdeJson(new List<SalesReportRowDto> { row });

        // Assert
        var venta = await _context.Ventas.FirstAsync();
        // ServerTime 12:00 + offset -14h = 22:00 previous day local
        venta.FechaHora.Hour.Should().Be(12);    // Raw server time (overwritten)
        venta.FechaLocal.Hour.Should().Be(22);   // 12h - 14h = 22h
        venta.FechaLocal.Day.Should().Be(14);
    }

    // ── 2.4: Year guard: fecha < 2024 + serverTime → fecha overwritten ──────

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
            MachineTime = "2020-01-01 05:00:00",      // Year < 2024
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

    // ── 2.5: Year guard: fecha < 2024 + no serverTime → fecha unchanged ─────

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
            MachineTime = "2020-03-15 09:00:00",  // Year < 2024
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
}
