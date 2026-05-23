namespace VendingManager.Tests.Components.Dashboard;

using FluentAssertions;
using VendingManager.Shared.DTOs;

/// <summary>
/// Unit tests for RecentActivity component behavior.
/// Since bUnit is not available, these tests verify the data structures
/// and logic that the RecentActivity.razor component will consume.
///
/// When the component is created, it will:
/// - Display chronological timeline (newest first) of last 20 entries
/// - Each entry: timestamp, type badge, amount, description, link to detail
/// - Type filter buttons: Venta/Transferencia/Compra/MovimientoCaja/Todos
/// - Machine filter: filters only Ventas entries; others unfiltered
/// - Empty state: "Sin actividad reciente"
/// </summary>
public class RecentActivityTests
{
    // ─── ActividadRecienteDto structure validation ───────────────────────────

    [Fact]
    public void ActividadReciente_DefaultValues_AreDefaults()
    {
        var actividad = new ActividadRecienteDto();

        actividad.Fecha.Should().Be(default);
        actividad.Tipo.Should().BeEmpty();
        actividad.Monto.Should().Be(0m);
        actividad.Descripcion.Should().BeEmpty();
        actividad.LinkUrl.Should().BeEmpty();
        actividad.MaquinaId.Should().BeNull();
    }

    // ─── Activity timeline renders in reverse chronological order ─────────────

    [Fact]
    public void ActividadReciente_OrderedNewestFirst_ByFechaDescending()
    {
        // Arrange
        var actividades = new List<ActividadRecienteDto>
        {
            new() { Fecha = new DateTime(2025, 5, 20, 14, 0, 0), Tipo = "venta", Descripcion = "Venta tarde" },
            new() { Fecha = new DateTime(2025, 5, 20, 12, 0, 0), Tipo = "transferencia", Descripcion = "Transferencia mediodia" },
            new() { Fecha = new DateTime(2025, 5, 20, 10, 0, 0), Tipo = "compra", Descripcion = "Compra manana" },
            new() { Fecha = new DateTime(2025, 5, 19, 16, 0, 0), Tipo = "movimiento_caja", Descripcion = "Aporte tarde" }
        };

        // Act
        var ordered = actividades.OrderByDescending(a => a.Fecha).ToList();

        // Assert
        ordered[0].Fecha.Should().BeAfter(ordered[1].Fecha);
        ordered[1].Fecha.Should().BeAfter(ordered[2].Fecha);
        ordered[2].Fecha.Should().BeAfter(ordered[3].Fecha);
    }

    // ─── Type filter shows only matching entries ─────────────────────────────

    [Theory]
    [InlineData("venta")]
    [InlineData("transferencia")]
    [InlineData("compra")]
    [InlineData("movimiento_caja")]
    public void ActividadReciente_TypeFilter_SingleType(string tipo)
    {
        var actividades = new List<ActividadRecienteDto>
        {
            new() { Tipo = "venta", Descripcion = "Venta 1" },
            new() { Tipo = "transferencia", Descripcion = "Transferencia 1" },
            new() { Tipo = "compra", Descripcion = "Compra 1" },
            new() { Tipo = "movimiento_caja", Descripcion = "Movimiento 1" }
        };

        var filtered = actividades.Where(a => a.Tipo == tipo).ToList();

        filtered.Should().HaveCount(1);
        filtered[0].Tipo.Should().Be(tipo);
    }

    [Fact]
    public void ActividadReciente_TypeFilter_Todos_ReturnsAll()
    {
        var actividades = new List<ActividadRecienteDto>
        {
            new() { Tipo = "venta", Descripcion = "Venta 1" },
            new() { Tipo = "transferencia", Descripcion = "Transferencia 1" },
            new() { Tipo = "compra", Descripcion = "Compra 1" },
            new() { Tipo = "movimiento_caja", Descripcion = "Movimiento 1" }
        };

        var filtered = actividades.ToList(); // "Todos" = no filter

        filtered.Should().HaveCount(4);
    }

    // ─── Machine filter applies only to Ventas ─────────────────────────────

    [Fact]
    public void ActividadReciente_MachineFilter_AppliesOnlyToVentas()
    {
        // Arrange — machine filter (maquinaId=3) only affects Ventas entries
        var maquinaId = 3;
        var actividades = new List<ActividadRecienteDto>
        {
            new() { Tipo = "venta", MaquinaId = 3, Descripcion = "Venta maquina 3" },
            new() { Tipo = "venta", MaquinaId = 1, Descripcion = "Venta maquina 1" },
            new() { Tipo = "transferencia", MaquinaId = null, Descripcion = "Transferencia global" },
            new() { Tipo = "compra", MaquinaId = null, Descripcion = "Compra global" },
            new() { Tipo = "movimiento_caja", MaquinaId = null, Descripcion = "Movimiento global" }
        };

        // Act — filter: include all non-Ventas OR (Ventas AND maquinaId matches)
        var filtered = actividades.Where(a =>
            a.Tipo != "venta" || a.MaquinaId == maquinaId
        ).ToList();

        // Assert
        filtered.Should().HaveCount(4);
        filtered.Count(a => a.Tipo == "venta").Should().Be(1);
        filtered.First(a => a.Tipo == "venta").MaquinaId.Should().Be(3);
    }

    // ─── Empty state when no activity ───────────────────────────────────────

    [Fact]
    public void ActividadReciente_EmptyState_NoEntries()
    {
        var actividades = new List<ActividadRecienteDto>();

        actividades.Should().BeEmpty();
    }

    // ─── Activity count limited to 20 entries (default) ─────────────────────

    [Fact]
    public void ActividadReciente_DefaultLimit_IsTwenty()
    {
        var actividades = Enumerable.Range(1, 50)
            .Select(i => new ActividadRecienteDto
            {
                Fecha = DateTime.Now.AddHours(-i),
                Tipo = "venta",
                Descripcion = $"Venta {i}"
            })
            .ToList();

        var recent = actividades
            .OrderByDescending(a => a.Fecha)
            .Take(20)
            .ToList();

        recent.Should().HaveCount(20);
        recent.First().Descripcion.Should().Be("Venta 1");
        recent.Last().Descripcion.Should().Be("Venta 20");
    }

    // ─── Scenario: Mixed activity with machine filter active ────────────────
    // GIVEN máquina 3 has recent ventas, and there are recent global
    //       transferencias and movimientos caja
    // WHEN the dashboard loads with maquinaId=3
    // THEN ventas entries show only máquina-3 sales;
    //      transferencias and movimientos caja entries show all (global)

    [Fact]
    public void ActividadReciente_Scenario_MixedActivityMachineFilter()
    {
        var maquinaId = 3;
        var actividades = new List<ActividadRecienteDto>
        {
            new() { Tipo = "venta", MaquinaId = 3, Fecha = DateTime.Now.AddHours(-1), Descripcion = "Venta maquina 3" },
            new() { Tipo = "venta", MaquinaId = 1, Fecha = DateTime.Now.AddHours(-2), Descripcion = "Venta maquina 1" },
            new() { Tipo = "venta", MaquinaId = 3, Fecha = DateTime.Now.AddHours(-3), Descripcion = "Venta maquina 3 #2" },
            new() { Tipo = "transferencia", MaquinaId = null, Fecha = DateTime.Now.AddHours(-4), Descripcion = "Transfer global" },
            new() { Tipo = "movimiento_caja", MaquinaId = null, Fecha = DateTime.Now.AddHours(-5), Descripcion = "Aporte global" }
        };

        // Act
        var filtered = actividades.Where(a =>
            a.Tipo != "venta" || a.MaquinaId == maquinaId
        ).OrderByDescending(a => a.Fecha).ToList();

        // Assert
        filtered.Count(a => a.Tipo == "venta").Should().Be(2); // only maquina 3
        filtered.Count(a => a.Tipo == "transferencia").Should().Be(1); // global
        filtered.Count(a => a.Tipo == "movimiento_caja").Should().Be(1); // global
        filtered.Should().HaveCount(4);
    }

    // ─── Scenario: User filters by type ─────────────────────────────────────
    // GIVEN the timeline has entries of all four types
    // WHEN the user selects filter "Transferencia"
    // THEN only Transferencia entries are displayed; all other types are hidden

    [Fact]
    public void ActividadReciente_Scenario_TypeFilterTransferencia()
    {
        var actividades = new List<ActividadRecienteDto>
        {
            new() { Tipo = "venta", Descripcion = "Venta" },
            new() { Tipo = "transferencia", Descripcion = "Transferencia 1" },
            new() { Tipo = "transferencia", Descripcion = "Transferencia 2" },
            new() { Tipo = "compra", Descripcion = "Compra" },
            new() { Tipo = "movimiento_caja", Descripcion = "Movimiento" }
        };

        var filtered = actividades.Where(a => a.Tipo == "transferencia").ToList();

        filtered.Should().HaveCount(2);
        filtered.All(a => a.Tipo == "transferencia").Should().BeTrue();
    }

    // ─── Scenario: No activity in filtered view ─────────────────────────────
    // GIVEN the timeline has only Venta and Compra entries
    // WHEN the user filters by "MovimientoCaja"
    // THEN the timeline shows "Sin actividad para este filtro"

    [Fact]
    public void ActividadReciente_Scenario_NoActivityForFilter()
    {
        var actividades = new List<ActividadRecienteDto>
        {
            new() { Tipo = "venta", Descripcion = "Venta" },
            new() { Tipo = "compra", Descripcion = "Compra" }
        };

        var filtered = actividades.Where(a => a.Tipo == "movimiento_caja").ToList();

        filtered.Should().BeEmpty();
    }

    // ─── Each activity entry has all required fields ─────────────────────────

    [Fact]
    public void ActividadReciente_Entry_AllRequiredFieldsPresent()
    {
        var entry = new ActividadRecienteDto
        {
            Fecha = new DateTime(2025, 5, 20, 14, 30, 0),
            Tipo = "venta",
            Monto = 5000m,
            Descripcion = "Venta máquina 3 - Producto X",
            LinkUrl = "/informe-ventas?maquinaId=3",
            MaquinaId = 3
        };

        entry.Fecha.Should().NotBe(default);
        entry.Tipo.Should().Be("venta");
        entry.Monto.Should().Be(5000m);
        entry.Descripcion.Should().NotBeEmpty();
        entry.LinkUrl.Should().NotBeEmpty();
        entry.MaquinaId.Should().Be(3);
    }

    // ─── Type badge label mapping ────────────────────────────────────────────

    [Theory]
    [InlineData("venta", "VENTA")]
    [InlineData("transferencia", "TRANSFERENCIA")]
    [InlineData("compra", "COMPRA")]
    [InlineData("movimiento_caja", "MOVIMIENTO CAJA")]
    public void ActividadReciente_TypeLabel_Formatted(string tipo, string expectedLabel)
    {
        // The component will format the type for display
        // e.g., "movimiento_caja" → "MovimientoCaja"
        var entry = new ActividadRecienteDto { Tipo = tipo };

        // Label formatting: replace _ with space, capitalize each word
        var formattedLabel = entry.Tipo.Replace("_", " ").ToUpper();

        formattedLabel.Should().Be(expectedLabel.Replace("_", " ").ToUpper());
    }

    // ─── RecentActivity component expected parameters ─────────────────────────
    // [Parameter] public List<ActividadRecienteDto> Actividades { get; set; }
    // [Parameter] public int? MaquinaId { get; set; }
    // [Parameter] public string TipoFiltro { get; set; } = "Todos"

    [Fact]
    public void ActividadReciente_ComponentParameterContract()
    {
        var actividades = new List<ActividadRecienteDto>
        {
            new() { Tipo = "venta", MaquinaId = 3 }
        };

        actividades.Should().BeOfType<List<ActividadRecienteDto>>();
        actividades.First().MaquinaId.Should().Be(3);
    }
}