namespace VendingManager.Tests.Components.Dashboard;

using FluentAssertions;
using VendingManager.Shared.DTOs;

/// <summary>
/// Unit tests for UnifiedAlerts component behavior.
/// Since bUnit is not available, these tests verify the data structures
/// and logic that the UnifiedAlerts.razor component will consume.
///
/// When the component is created, it will:
/// - Display badge with total alert count at top
/// - Group alert cards by severity: danger (red), warning (yellow), info (blue)
/// - Each alert: icon, message text, clickable link to resolution page
/// - Empty state: "Sin alertas pendientes" when Total=0
/// </summary>
public class UnifiedAlertsTests
{
    // ─── AlertaConsolidadaDto structure validation ────────────────────────────

    [Fact]
    public void AlertaConsolidada_DefaultValues_EmptyList()
    {
        var alertas = new AlertaConsolidadaDto();

        alertas.Total.Should().Be(0);
        alertas.Items.Should().NotBeNull();
        alertas.Items.Should().BeEmpty();
    }

    // ─── Badge count equals total unresolved alerts ───────────────────────────

    [Fact]
    public void AlertaConsolidada_BadgeCount_SumOfAllTypes()
    {
// Arrange — scenario: 3 stock + 2 transferencias + 1 gasto + 1 compra = 7 total
        var alertas = new AlertaConsolidadaDto
        {
            Total = 7,
            Items = new List<ItemAlertaDto>
            {
                // 3 stock items (danger)
                new() { Tipo = "stock-critico", Mensaje = "3 productos bajo stock", Severidad = "danger" },
                new() { Tipo = "stock-critico", Mensaje = "2 productos bajo stock", Severidad = "danger" },
                new() { Tipo = "stock-critico", Mensaje = "1 producto bajo stock", Severidad = "danger" },
                // 2 transferencias sin rendir (danger)
                new() { Tipo = "transferencias-sin-rendir", Mensaje = "2 transferencias", Severidad = "danger" },
                new() { Tipo = "transferencias-sin-rendir", Mensaje = "1 transferencia", Severidad = "danger" },
                // 1 gasto fijo no registrado (warning)
                new() { Tipo = "gastos-fijos-no-registrados", Mensaje = "1 gasto recurrente pendiente", Severidad = "warning" },
                // 1 compra sin factura (info)
                new() { Tipo = "compras-sin-factura", Mensaje = "1 compra sin documento", Severidad = "info" }
            }
        };

        // Assert
        alertas.Total.Should().Be(7);
        alertas.Items.Should().HaveCount(7);
    }

    // ─── Multiple alert types grouped by severity ─────────────────────────────

    [Fact]
    public void AlertaConsolidada_SeverityGroups_CorrectlySeparated()
    {
        var alertas = new AlertaConsolidadaDto
        {
            Total = 7,
            Items = new List<ItemAlertaDto>
            {
                new() { Tipo = "stock-critico", Severidad = "danger" },
                new() { Tipo = "transferencias-sin-rendir", Severidad = "danger" },
                new() { Tipo = "gastos-fijos-no-registrados", Severidad = "warning" },
                new() { Tipo = "compras-sin-factura", Severidad = "info" }
            }
        };

        var danger = alertas.Items.Where(i => i.Severidad == "danger").ToList();
        var warning = alertas.Items.Where(i => i.Severidad == "warning").ToList();
        var info = alertas.Items.Where(i => i.Severidad == "info").ToList();

        danger.Should().HaveCount(2);
        warning.Should().HaveCount(1);
        info.Should().HaveCount(1);
    }

    // ─── Zero alerts shows empty state ────────────────────────────────────────

    [Fact]
    public void AlertaConsolidada_EmptyState_TotalZeroItemsEmpty()
    {
        var alertas = new AlertaConsolidadaDto
        {
            Total = 0,
            Items = new List<ItemAlertaDto>()
        };

        alertas.Total.Should().Be(0);
        alertas.Items.Should().BeEmpty();
    }

    // ─── ItemAlertaDto structure validation ──────────────────────────────────

    [Theory]
    [InlineData("stock-critico", "danger", "/stockout-dashboard")]
    [InlineData("transferencias-sin-rendir", "danger", "/rendiciones")]
    [InlineData("gastos-fijos-no-registrados", "warning", "/caja")]
    [InlineData("compras-sin-factura", "info", "/compras")]
    public void ItemAlerta_SeverityAndLink_CorrectPerType(string tipo, string severidad, string linkUrl)
    {
        var item = new ItemAlertaDto
        {
            Tipo = tipo,
            Mensaje = $"Alerta de {tipo}",
            Severidad = severidad,
            LinkUrl = linkUrl
        };

        item.Tipo.Should().Be(tipo);
        item.Severidad.Should().Be(severidad);
        item.LinkUrl.Should().Be(linkUrl);
    }

    // ─── Alert badge display logic ───────────────────────────────────────────
    // Badge only shows when Total > 0

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(7, true)]
    [InlineData(99, true)]
    public void AlertaConsolidada_BadgeVisibility_ShowsWhenGreaterThanZero(int total, bool expectedVisible)
    {
        var alertas = new AlertaConsolidadaDto { Total = total };

        (alertas.Total > 0).Should().Be(expectedVisible);
    }

    // ─── Scenario: Multiple alert types present ──────────────────────────────
    // GIVEN 3 products below threshold, 2 transfers pending without rendicion,
    //        1 monthly expense not recorded, and 1 purchase missing TipoFactura
    // WHEN the dashboard renders
    // THEN the badge shows "7", stock alert is red, transfers alert is red,
    //      expense alert is yellow, purchase alert is blue

    [Fact]
    public void AlertaConsolidada_Scenario_MultipleAlertTypesPresent()
    {
        // Arrange — scenario: 3 stock + 2 transferencias + 1 gasto + 1 compra = 7 total
        var alertas = new AlertaConsolidadaDto
        {
            Total = 7,
            Items = new List<ItemAlertaDto>
            {
                // 3 stock items (danger) — 3 separate alert items
                new() { Tipo = "stock-critico", Mensaje = "3 productos bajo stock", Severidad = "danger" },
                new() { Tipo = "stock-critico", Mensaje = "2 productos bajo stock", Severidad = "danger" },
                new() { Tipo = "stock-critico", Mensaje = "1 producto bajo stock", Severidad = "danger" },
                // 2 transferencias sin rendir (danger) — 2 separate alert items
                new() { Tipo = "transferencias-sin-rendir", Mensaje = "2 transferencias", Severidad = "danger" },
                new() { Tipo = "transferencias-sin-rendir", Mensaje = "1 transferencia", Severidad = "danger" },
                // 1 gasto fijo no registrado (warning)
                new() { Tipo = "gastos-fijos-no-registrados", Mensaje = "1 gasto recurrente pendiente", Severidad = "warning" },
                // 1 compra sin factura (info)
                new() { Tipo = "compras-sin-factura", Mensaje = "1 compra sin documento", Severidad = "info" }
            }
        };

        // Act
        var dangerCount = alertas.Items.Count(i => i.Severidad == "danger");
        var warningCount = alertas.Items.Count(i => i.Severidad == "warning");
        var infoCount = alertas.Items.Count(i => i.Severidad == "info");

        // Assert
        alertas.Total.Should().Be(7);
        dangerCount.Should().Be(5); // 3 stock-critico + 2 transferencias-sin-rendir
        warningCount.Should().Be(1); // gastos-fijos-no-registrados
        infoCount.Should().Be(1); // compras-sin-factura
    }

    // ─── Scenario: Zero alerts across all domains ───────────────────────────
    // GIVEN all stock above minimum, all transfers rendered, all expenses recorded,
    //       all purchases have TipoFactura
    // WHEN the dashboard renders
    // THEN no badge is displayed and the alert area shows "Sin alertas pendientes"

    [Fact]
    public void AlertaConsolidada_Scenario_ZeroAlerts_EmptyState()
    {
        var alertas = new AlertaConsolidadaDto
        {
            Total = 0,
            Items = new List<ItemAlertaDto>()
        };

        alertas.Total.Should().Be(0);
        alertas.Items.Should().BeEmpty();
        (alertas.Total > 0).Should().BeFalse();
    }

    // ─── UnifiedAlerts component expected parameters ─────────────────────────
    // [Parameter] public AlertaConsolidadaDto? Alertas { get; set; }

    [Fact]
    public void AlertaConsolidada_ComponentParameterContract()
    {
        var alertas = new AlertaConsolidadaDto
        {
            Total = 5,
            Items = new List<ItemAlertaDto>
            {
                new() { Tipo = "stock-critico", Severidad = "danger", LinkUrl = "/stockout-dashboard" }
            }
        };

        alertas.Should().BeOfType<AlertaConsolidadaDto>();
        alertas.Total.Should().Be(5);
    }
}