namespace VendingManager.Tests.Interfaces;

using FluentAssertions;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

public class ITemplateRecargaAnalyticsServiceTests
{
    /// <summary>
    /// Verifies the interface defines the expected analytics methods.
    /// This is structural but establishes the contract.
    /// </summary>
    [Fact]
    public void ITemplateRecargaAnalyticsService_HasRequiredMethods()
    {
        var type = typeof(ITemplateRecargaAnalyticsService);

        type.GetMethod(nameof(ITemplateRecargaAnalyticsService.AnalyzarPorTemplateAsync)).Should().NotBeNull();
        type.GetMethod(nameof(ITemplateRecargaAnalyticsService.SyncVentasWithTemplateAsync)).Should().NotBeNull();
        type.GetMethod(nameof(ITemplateRecargaAnalyticsService.SyncAllVentasAsync)).Should().NotBeNull();
        type.GetMethod(nameof(ITemplateRecargaAnalyticsService.SyncSlotProductoAsync)).Should().NotBeNull();
    }

    /// <summary>
    /// AnalyzarPorTemplateAsync takes templateId and umbralHorasSilencio, returns stockout list.
    /// </summary>
    [Fact]
    public void AnalyzarPorTemplateAsync_Signature_IsCorrect()
    {
        var method = typeof(ITemplateRecargaAnalyticsService).GetMethod(nameof(ITemplateRecargaAnalyticsService.AnalyzarPorTemplateAsync));
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<List<StockoutAnalysisDto>>));
        method.GetParameters().Should().HaveCount(2);
        method.GetParameters()[0].ParameterType.Should().Be(typeof(int));
        method.GetParameters()[1].ParameterType.Should().Be(typeof(double));
    }

    /// <summary>
    /// SyncVentasWithTemplateAsync takes templateId and actualizarCostos bool.
    /// </summary>
    [Fact]
    public void SyncVentasWithTemplateAsync_Signature_IsCorrect()
    {
        var method = typeof(ITemplateRecargaAnalyticsService).GetMethod(nameof(ITemplateRecargaAnalyticsService.SyncVentasWithTemplateAsync));
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<int>));
        method.GetParameters().Should().HaveCount(2);
        method.GetParameters()[0].ParameterType.Should().Be(typeof(int));
        method.GetParameters()[1].ParameterType.Should().Be(typeof(bool));
    }

    /// <summary>
    /// SyncAllVentasAsync takes actualizarCostos bool, returns aggregate result.
    /// </summary>
    [Fact]
    public void SyncAllVentasAsync_Signature_IsCorrect()
    {
        var method = typeof(ITemplateRecargaAnalyticsService).GetMethod(nameof(ITemplateRecargaAnalyticsService.SyncAllVentasAsync));
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<SyncAllVentasResultDto>));
        method.GetParameters().Should().HaveCount(1);
        method.GetParameters()[0].ParameterType.Should().Be(typeof(bool));
    }

    /// <summary>
    /// SyncSlotProductoAsync takes templateId, periodoId, numeroSlot, productoId.
    /// </summary>
    [Fact]
    public void SyncSlotProductoAsync_Signature_IsCorrect()
    {
        var method = typeof(ITemplateRecargaAnalyticsService).GetMethod(nameof(ITemplateRecargaAnalyticsService.SyncSlotProductoAsync));
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<SyncSlotProductoResultDto>));
        method.GetParameters().Should().HaveCount(4);
        method.GetParameters()[0].ParameterType.Should().Be(typeof(int));
        method.GetParameters()[1].ParameterType.Should().Be(typeof(int));
        method.GetParameters()[2].ParameterType.Should().Be(typeof(string));
        method.GetParameters()[3].ParameterType.Should().Be(typeof(int));
    }
}