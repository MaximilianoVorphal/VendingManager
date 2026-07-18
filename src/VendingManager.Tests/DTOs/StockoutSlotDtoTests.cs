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
            FechaAgotamientoEstimada = new DateTime(2025, 1, 10),
            TieneVentasPosterioresAlAgotamiento = true,
            PrimeraVentaPosteriorAlAgotamiento = new DateTime(2025, 1, 11),
            UltimaVentaPosteriorAlAgotamiento = new DateTime(2025, 1, 15),
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
            VentasOperativasObservadas = 8,
            HorasExposicionOperativas = 28,
            QualityFlags = StockoutQualityFlags.PostDepletionSales,
            EstimateConfidence = EstimateConfidence.Low,
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
        dto.FechaAgotamientoEstimada.Should().Be(new DateTime(2025, 1, 10));
        dto.TieneVentasPosterioresAlAgotamiento.Should().BeTrue();
        dto.PrimeraVentaPosteriorAlAgotamiento.Should().Be(new DateTime(2025, 1, 11));
        dto.VentasOperativasObservadas.Should().Be(8);
        dto.EstimateConfidence.Should().Be(EstimateConfidence.Low);
        dto.NivelAlerta.Should().Be("Crítico");
        dto.DiasSinStock.Should().BeApproximately(73.0 / 24.0, 0.001);
    }

    [Fact]
    public void StockoutSlotDto_ComputedProperties_WorkCorrectly()
    {
        var dto = new StockoutSlotDto
        {
            HorasSinStock = 50,
            VelocidadPorHora = 2.5m
        };

        dto.DiasSinStock.Should().BeApproximately(50.0 / 24.0, 0.001);
        dto.VelocidadDiaria.Should().Be(35m); // 2.5 * 14
        dto.NivelAlerta.Should().Be("Alto");
        dto.ColorAlerta.Should().Be("bg-warning text-dark");
    }

    [Fact]
    public void StockoutDtos_UseCalendarDaysAndExposeDepletionMetadata()
    {
        var depletion = new DateTime(2025, 1, 10, 2, 24, 0);
        var dto = new StockoutAnalysisDto
        {
            HorasSinStock = 180,
            FechaAgotamientoEstimada = depletion,
            TieneVentasPosterioresAlAgotamiento = true,
            UltimaVentaPosteriorAlAgotamiento = depletion.AddDays(1)
        };

        dto.DiasSinStock.Should().Be(7.5);
        dto.FechaAgotamientoEstimada.Should().Be(depletion);
        dto.TieneVentasPosterioresAlAgotamiento.Should().BeTrue();
        dto.UltimaVentaPosteriorAlAgotamiento.Should().Be(depletion.AddDays(1));
    }

    [Fact]
    public void StockoutDtos_MirrorObservedEffectiveVelocityAndObsoleteAliases()
    {
        var analysis = new StockoutAnalysisDto { VelocidadObservadaSlotPorHora = .25m, VelocidadEfectivaPorHora = .5m, OrigenVelocidad = OrigenVelocidad.ProductoMaquina };
        var slot = new StockoutSlotDto { VelocidadObservadaSlotPorHora = .25m, VelocidadEfectivaPorHora = .5m, OrigenVelocidad = OrigenVelocidad.ProductoMaquina };

        analysis.VentasOperativasObservadas.Should().Be(0);
        analysis.VelocidadPorHora.Should().Be(.5m);
        analysis.VelocidadDiaria.Should().Be(7m);
        slot.VelocidadPorHora.Should().Be(.5m);
        slot.VelocidadDiaria.Should().Be(7m);
        slot.OrigenVelocidad.Should().Be(OrigenVelocidad.ProductoMaquina);
    }

    [Fact]
    public void StockoutDtos_ExposeEstimatedUnmetPhysicalUnits()
    {
        foreach (var type in new[] { typeof(StockoutAnalysisDto), typeof(StockoutSlotDto), typeof(StockoutProductoDto) })
        {
            var property = type.GetProperty("UnidadesNoAtendidasEstimadas");
            property.Should().NotBeNull($"{type.Name} must distinguish estimated physical units from CLP estimates");
            property!.PropertyType.Should().Be(typeof(decimal));
        }
    }
}
