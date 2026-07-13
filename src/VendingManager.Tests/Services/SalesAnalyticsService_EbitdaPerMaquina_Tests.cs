namespace VendingManager.Tests.Services;

using FluentAssertions;
using VendingManager.Core.Entities;
using VendingManager.Infrastructure.Services;

/// <summary>
/// Tests for per-machine EBITDA calculations in SalesAnalyticsService.
///
/// Task 2.2: ContarDiasOperativos — operational days prorated by
///           FechaInstalacion/FechaBaja.
///
/// Math: max(0, (min(FechaBaja ?? fin, fin) - max(FechaInstalacion, inicio)).Days + 1)
/// Round UP: if machine was operational at all during range, count that day.
/// </summary>
public class SalesAnalyticsService_EbitdaPerMaquina_Tests
{
    // =========================================================================
    // TASK 2.2 — ContarDiasOperativos (pure function)
    //
    // Spec scenarios:
    //   Full month (31 days)
    //   Partial installation (17 days)
    //   Partial baja (10 days)
    //   Both-ended (16 days)
    //   Inactive (baja before range → 0 days)
    // =========================================================================

    /// <summary>
    /// Scenario: Full-month — machine installed Jan 1, all of January counts.
    /// Jan 1 to Jan 31 inclusive = 31 days.
    /// </summary>
    [Fact]
    public void ContarDiasOperativos_FullMonth_ReturnsThirtyOne()
    {
        var maquina = new Maquina
        {
            Id = 1,
            Nombre = "Test",
            FechaInstalacion = new DateTime(2025, 1, 1),
            FechaBaja = null
        };
        var inicio = new DateTime(2025, 1, 1);
        var fin = new DateTime(2025, 1, 31);

        int dias = SalesAnalyticsService.ContarDiasOperativos(maquina, inicio, fin);

        dias.Should().Be(31, "Jan 1 to Jan 31 inclusive = 31 days");
    }

    /// <summary>
    /// Scenario: Partial installation — installed Jan 15, counts from that day.
    /// Jan 15 to Jan 31 inclusive = 17 days.
    /// </summary>
    [Fact]
    public void ContarDiasOperativos_PartialInstall_ReturnsSeventeen()
    {
        var maquina = new Maquina
        {
            Id = 2,
            Nombre = "Test",
            FechaInstalacion = new DateTime(2025, 1, 15),
            FechaBaja = null
        };
        var inicio = new DateTime(2025, 1, 1);
        var fin = new DateTime(2025, 1, 31);

        int dias = SalesAnalyticsService.ContarDiasOperativos(maquina, inicio, fin);

        dias.Should().Be(17, "Jan 15 to Jan 31 inclusive = 17 days");
    }

    /// <summary>
    /// Scenario: Partial baja — machine retired Mar 10.
    /// Mar 1 to Mar 10 inclusive = 10 days.
    /// </summary>
    [Fact]
    public void ContarDiasOperativos_PartialBaja_ReturnsTen()
    {
        var maquina = new Maquina
        {
            Id = 3,
            Nombre = "Test",
            FechaInstalacion = new DateTime(2024, 6, 1),
            FechaBaja = new DateTime(2025, 3, 10)
        };
        var inicio = new DateTime(2025, 3, 1);
        var fin = new DateTime(2025, 3, 31);

        int dias = SalesAnalyticsService.ContarDiasOperativos(maquina, inicio, fin);

        dias.Should().Be(10, "Mar 1 to Mar 10 inclusive = 10 days");
    }

    /// <summary>
    /// Scenario: Both-ended — installed Jun 5, retired Jun 20.
    /// Jun 5 to Jun 20 inclusive = 16 days.
    /// </summary>
    [Fact]
    public void ContarDiasOperativos_BothEnded_ReturnsSixteen()
    {
        var maquina = new Maquina
        {
            Id = 4,
            Nombre = "Test",
            FechaInstalacion = new DateTime(2025, 6, 5),
            FechaBaja = new DateTime(2025, 6, 20)
        };
        var inicio = new DateTime(2025, 6, 1);
        var fin = new DateTime(2025, 6, 30);

        int dias = SalesAnalyticsService.ContarDiasOperativos(maquina, inicio, fin);

        dias.Should().Be(16, "Jun 5 to Jun 20 inclusive = 16 days");
    }

    /// <summary>
    /// Scenario: Inactive — baja before range start → 0 operational days.
    /// </summary>
    [Fact]
    public void ContarDiasOperativos_Inactive_BajaBeforeRange_ReturnsZero()
    {
        var maquina = new Maquina
        {
            Id = 5,
            Nombre = "Test",
            FechaInstalacion = new DateTime(2024, 1, 1),
            FechaBaja = new DateTime(2024, 12, 31)
        };
        var inicio = new DateTime(2025, 1, 1);
        var fin = new DateTime(2025, 1, 31);

        int dias = SalesAnalyticsService.ContarDiasOperativos(maquina, inicio, fin);

        dias.Should().Be(0, "baja before range start → zero operational days");
    }

    /// <summary>
    /// Edge: Machine installed after range end → 0 days.
    /// </summary>
    [Fact]
    public void ContarDiasOperativos_InstalledAfterRange_ReturnsZero()
    {
        var maquina = new Maquina
        {
            Id = 6,
            Nombre = "Test",
            FechaInstalacion = new DateTime(2025, 3, 1),
            FechaBaja = null
        };
        var inicio = new DateTime(2025, 1, 1);
        var fin = new DateTime(2025, 1, 31);

        int dias = SalesAnalyticsService.ContarDiasOperativos(maquina, inicio, fin);

        dias.Should().Be(0, "installed after range end → zero days");
    }
}
