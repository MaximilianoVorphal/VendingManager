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

    // ── 2.2: Other machineId → offset -11 ───────────────────────────────────

    [Fact]
    public async Task OtherMachineId_AppliesMinusElevenOffset()
    {
        // Arrange
        _context.Maquinas.Add(new Maquina
        {
            Id = 1,
            IdInternoMaquina = "9999999999",
            Slots = new List<ConfiguracionSlot>
            {
                new() { NumeroSlot = "B2", Producto = new Producto { Id = 2, CostoPromedio = 100 } }
            }
        });
        await _context.SaveChangesAsync();

        var row = new SalesReportRowDto
        {
            MachineId = "9999999999",
            Slot = "B2",
            Price = 500,
            MachineTime = "2026-07-15 10:00:00", // UTC+7 → CLT = -11 offset
            ServerTime = "2026-07-15 00:00:00",
            TrSerialNumber = Guid.NewGuid().ToString()
        };

        // Act
        await _service.ImportarVentasDesdeJson(new List<SalesReportRowDto> { row });

        // Assert
        var venta = await _context.Ventas.FirstAsync();
        // Machine time 10:00 - 11h = 23:00 previous day local
        venta.FechaLocal.Hour.Should().Be(23);
        venta.FechaLocal.Day.Should().Be(14); // day before
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

    // ── 2.6: Breaker DefaultMaxOpenCooldown == 168h (post-refactor) ──────────

    [Fact]
    public void Breaker_DefaultMaxOpenCooldown_Is168Hours()
    {
        // Static field — no instance needed
        PollingCircuitBreaker.DefaultMaxOpenCooldown
            .Should().Be(TimeSpan.FromHours(168));
    }
}
