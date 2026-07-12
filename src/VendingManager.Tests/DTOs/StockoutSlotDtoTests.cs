namespace VendingManager.Tests.DTOs;

using System.Reflection;
using VendingManager.Shared.DTOs;

/// <summary>
/// Tests for StockoutSlotDto — mirrors every StockoutAnalysisDto property, including
/// FechasVentas, which the template analysis now populates eagerly so the Ventas Diarias
/// chart represents the template's sales without depending on the lazy timeline endpoint.
/// Follows the same reflection pattern as TemplateRecargaListItemDtoTests.
/// </summary>
public class StockoutSlotDtoTests
{
    [Fact]
    public void StockoutSlotDto_HasAllStockoutAnalysisProperties()
    {
        var sourceProps = typeof(StockoutAnalysisDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        var targetProps = typeof(StockoutSlotDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        // Every property in StockoutAnalysisDto must exist in StockoutSlotDto
        foreach (var prop in sourceProps)
        {
            targetProps.Should().Contain(prop,
                $"StockoutSlotDto should mirror property '{prop}' from StockoutAnalysisDto");
        }
    }

    [Fact]
    public void StockoutSlotDto_ContainsFechasVentas()
    {
        var props = typeof(StockoutSlotDto).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var fechasVentas = props.FirstOrDefault(p => p.Name == "FechasVentas");
        fechasVentas.Should().NotBeNull("StockoutSlotDto captures the template-period sale dates for the chart");
    }

    [Fact]
    public void StockoutSlotDto_CanBeConstructedWithAllFields()
    {
        var dto = new StockoutSlotDto
        {
            MaquinaId = 1,
            MaquinaNombre = "Máquina 23",
            ProductoId = 42,
            ProductoNombre = "Producto Test",
            NumeroSlot = "A1",
            PrimeraVenta = new DateTime(2025, 1, 1),
            UltimaVenta = new DateTime(2025, 1, 15),
            UltimaActividadMaquina = new DateTime(2025, 1, 20),
            FinReporte = new DateTime(2025, 2, 1),
            PosibleQuiebre = true,
            HorasSinStock = 73,
            StockInicial = 10,
            StockActual = 0,
            CantidadVendida = 8,
            FillPct = 0,
            DiasHastaStockout = 0,
            EsDeadSlot = false,
            HorasActivas = 336,
            VelocidadPorHora = 0.0238m,
            PrecioPromedioVenta = 1500m,
            GananciaPromedio = 600m,
            DineroPerdidoEstimado = 45000m,
            GananciaPerdidaEstimada = 18000m
        };

        dto.MaquinaId.Should().Be(1);
        dto.MaquinaNombre.Should().Be("Máquina 23");
        dto.ProductoId.Should().Be(42);
        dto.ProductoNombre.Should().Be("Producto Test");
        dto.NumeroSlot.Should().Be("A1");
        dto.PrimeraVenta.Should().Be(new DateTime(2025, 1, 1));
        dto.GananciaPerdidaEstimada.Should().Be(18000m);
        dto.NivelAlerta.Should().Be("Crítico");
        dto.DiasSinStock.Should().BeApproximately(3.042, 0.001);
    }

    [Fact]
    public void StockoutSlotDto_ComputedProperties_WorkCorrectly()
    {
        var dto = new StockoutSlotDto
        {
            HorasSinStock = 50,
            VelocidadPorHora = 2.5m
        };

        dto.DiasSinStock.Should().BeApproximately(2.083, 0.001);
        dto.VelocidadDiaria.Should().Be(60m);
        dto.NivelAlerta.Should().Be("Alto");
        dto.ColorAlerta.Should().Be("bg-warning text-dark");
    }
}
