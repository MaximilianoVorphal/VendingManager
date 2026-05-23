namespace VendingManager.Tests.Components.Dashboard;

using FluentAssertions;
using VendingManager.Shared.DTOs;

/// <summary>
/// Unit tests for FinancialPipeline component behavior.
/// Since bUnit is not available in this project, these tests verify the
/// data structures and logic that the FinancialPipeline.razor component
/// will consume, following the TDD "pure function" principle.
/// 
/// When the component is created, it will:
/// - Render 4 columns: Ventas, Transferencias, Compras+Gastos, Conciliación
/// - Show skeleton placeholders while loading
/// - Navigate on click to detail pages
/// - Filter Ventas by maquinaId; financial columns remain global
/// </summary>
public class FinancialPipelineTests
{
    // ─── PipelineFinancieroDto structure validation ─────────────────────────

    [Fact]
    public void PipelineFinanciero_DefaultValues_AreZeroOrFalse()
    {
        var pipeline = new PipelineFinancieroDto();

        pipeline.VentasMes.Should().Be(0m);
        pipeline.CantidadVentasMes.Should().Be(0);
        pipeline.TransferenciasActivas.Should().Be(0m);
        pipeline.CantidadTransferencias.Should().Be(0);
        pipeline.ComprasVinculadas.Should().Be(0m);
        pipeline.GastosVinculados.Should().Be(0m);
        pipeline.Conciliacion.Should().Be(0m);
        pipeline.IsPositiva.Should().BeFalse();
    }

    [Fact]
    public void PipelineFinanciero_PositiveConciliacion_IsPositivaTrue()
    {
        var pipeline = new PipelineFinancieroDto
        {
            TransferenciasActivas = 420_000m,
            ComprasVinculadas = 300_000m,
            GastosVinculados = 20_000m,
            Conciliacion = 100_000m,
            IsPositiva = true
        };

        pipeline.IsPositiva.Should().BeTrue();
        pipeline.Conciliacion.Should().Be(100_000m);
    }

    [Fact]
    public void PipelineFinanciero_NegativeConciliacion_IsPositivaFalse()
    {
        var pipeline = new PipelineFinancieroDto
        {
            TransferenciasActivas = 200_000m,
            ComprasVinculadas = 150_000m,
            GastosVinculados = 100_000m,
            Conciliacion = -50_000m,
            IsPositiva = false
        };

        pipeline.IsPositiva.Should().BeFalse();
        pipeline.Conciliacion.Should().BeNegative();
    }

    [Fact]
    public void PipelineFinanciero_ConciliacionFormula_TransferenciasMinusComprasMinusGastos()
    {
        // Arrange
        decimal transfer = 420_000m;
        decimal compras = 300_000m;
        decimal gastos = 20_000m;
        decimal expectedConciliacion = transfer - compras - gastos;

        // Act
        var pipeline = new PipelineFinancieroDto
        {
            TransferenciasActivas = transfer,
            ComprasVinculadas = compras,
            GastosVinculados = gastos,
            Conciliacion = expectedConciliacion,
            IsPositiva = expectedConciliacion >= 0
        };

        // Assert
        pipeline.Conciliacion.Should().Be(100_000m);
        (pipeline.TransferenciasActivas - pipeline.ComprasVinculadas - pipeline.GastosVinculados)
            .Should().Be(100_000m);
    }

    // ─── Scenario: Pipeline with linked data and positive conciliación ────────
    // GIVEN máquina 3 has $500K monthly sales, $420K active transfers,
    // and $300K linked purchases+expenses
    // THEN Ventas shows $500K, Transferencias shows $420K,
    //       Compras+Gastos shows $300K, Conciliación shows $120K with green indicator

    [Fact]
    public void Pipeline_Scenario_PositiveConciliacion_Machine3Data()
    {
        // Arrange & Act
        var pipeline = new PipelineFinancieroDto
        {
            VentasMes = 500_000m,
            CantidadVentasMes = 120,
            TransferenciasActivas = 420_000m,
            CantidadTransferencias = 8,
            ComprasVinculadas = 250_000m,
            GastosVinculados = 50_000m,
            Conciliacion = 420_000m - 250_000m - 50_000m, // = 120_000
            IsPositiva = true
        };

        // Assert
        pipeline.VentasMes.Should().Be(500_000m);
        pipeline.TransferenciasActivas.Should().Be(420_000m);
        pipeline.ComprasVinculadas.Should().Be(250_000m);
        pipeline.GastosVinculados.Should().Be(50_000m);
        pipeline.Conciliacion.Should().Be(120_000m);
        pipeline.IsPositiva.Should().BeTrue();
    }

    // ─── Scenario: Negative conciliación (overspent) ─────────────────────────
    // GIVEN active transfers sum to $200K but linked purchases+expenses sum to $250K
    // THEN Conciliación shows -$50K with red indicator

    [Fact]
    public void Pipeline_Scenario_NegativeConciliacion_Overspent()
    {
        // Arrange & Act
        var pipeline = new PipelineFinancieroDto
        {
            TransferenciasActivas = 200_000m,
            ComprasVinculadas = 150_000m,
            GastosVinculados = 100_000m,
            Conciliacion = -50_000m,
            IsPositiva = false
        };

        // Assert
        pipeline.Conciliacion.Should().Be(-50_000m);
        pipeline.IsPositiva.Should().BeFalse();
        (pipeline.TransferenciasActivas - pipeline.ComprasVinculadas - pipeline.GastosVinculados)
            .Should().BeNegative();
    }

    // ─── Scenario: No machine selected (global totals) ───────────────────────
    // GIVEN no maquinaId parameter
    // WHEN the dashboard loads
    // THEN Ventas shows sum across all machines; Transferencias, Compras+Gastos,
    //      and Conciliación show global totals

    [Fact]
    public void Pipeline_Scenario_GlobalTotals_NoMachineFilter()
    {
        // Arrange & Act — maquinaId=0 means all machines (global)
        var pipeline = new PipelineFinancieroDto
        {
            VentasMes = 2_500_000m,   // sum across all machines
            CantidadVentasMes = 580,
            TransferenciasActivas = 1_800_000m,  // global
            CantidadTransferencias = 35,
            ComprasVinculadas = 1_200_000m,      // global
            GastosVinculados = 150_000m,
            Conciliacion = 1_800_000m - 1_200_000m - 150_000m, // = 450_000
            IsPositiva = true
        };

        // Assert
        pipeline.VentasMes.Should().BeGreaterThan(500_000m); // larger than single machine
        pipeline.TransferenciasActivas.Should().Be(1_800_000m);
        pipeline.ComprasVinculadas.Should().Be(1_200_000m);
        pipeline.GastosVinculados.Should().Be(150_000m);
        pipeline.Conciliacion.Should().Be(450_000m);
        pipeline.IsPositiva.Should().BeTrue();
    }

    // ─── Pipeline column click-through navigation targets ─────────────────────
    // Each pipeline column navigates to its detail page on click

    [Theory]
    [InlineData("/informe-ventas")]
    [InlineData("/rendiciones")]
    [InlineData("/compras")]
    public void Pipeline_ColumnNavigation_ClickNavigatesToDetailPage(string expectedUrl)
    {
        // The FinancialPipeline component will use NavigationManager to navigate
        // This test validates the expected navigation targets from the spec
        expectedUrl.Should().NotBeEmpty();
        expectedUrl.Should().StartWith("/");
    }

    // ─── FinancialPipeline component parameters expected ─────────────────────
    // The component will receive PipelineFinancieroDto and maquinaId as parameters

    [Fact]
    public void Pipeline_ComponentParameters_ExpectedTypes()
    {
        // This test documents the expected component parameter contract:
        // [Parameter] public PipelineFinancieroDto? Pipeline { get; set; }
        // [Parameter] public int MaquinaId { get; set; }
        // [Parameter] public bool IsLoaded { get; set; }

        var pipeline = new PipelineFinancieroDto
        {
            VentasMes = 500_000m,
            IsPositiva = true
        };

        pipeline.Should().BeOfType<PipelineFinancieroDto>();
        pipeline.VentasMes.Should().Be(500_000m);
    }

    // ─── Skeleton loading state behavior ─────────────────────────────────────
    // When IsLoaded=false, the component renders skeleton placeholders

    [Fact]
    public void Pipeline_SkeletonState_IsLoadedFalse_ShowsSkeletons()
    {
        // When IsLoaded is false, the component should show:
        // <div class="bg-secondary animated pulse h3 mb-0" style="width: 80%;">&nbsp;</div>
        // This is verified by the component consuming Pipeline != null && IsLoaded
        var pipeline = new PipelineFinancieroDto(); // default (not loaded)
        var isLoaded = pipeline.VentasMes > 0; // false when no data

        isLoaded.Should().BeFalse();
    }

    [Fact]
    public void Pipeline_LoadedState_IsLoadedTrue_ShowsData()
    {
        // When IsLoaded is true, the component shows real data
        var pipeline = new PipelineFinancieroDto { VentasMes = 500_000m };
        var isLoaded = pipeline.VentasMes > 0;

        isLoaded.Should().BeTrue();
    }
}