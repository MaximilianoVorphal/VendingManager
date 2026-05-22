namespace VendingManager.Tests.Interfaces;

using FluentAssertions;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

public class ITemplateRecargaLifecycleServiceTests
{
    /// <summary>
    /// Verifies the interface defines the expected state-transition methods.
    /// This is structural but establishes the contract.
    /// </summary>
    [Fact]
    public void ITemplateRecargaLifecycleService_HasRequiredMethods()
    {
        var type = typeof(ITemplateRecargaLifecycleService);

        type.GetMethod(nameof(ITemplateRecargaLifecycleService.StartLoadingAsync)).Should().NotBeNull();
        type.GetMethod(nameof(ITemplateRecargaLifecycleService.FinalizeAsync)).Should().NotBeNull();
        type.GetMethod(nameof(ITemplateRecargaLifecycleService.CloseAsync)).Should().NotBeNull();
        type.GetMethod(nameof(ITemplateRecargaLifecycleService.ResetToDraftAsync)).Should().NotBeNull();
        type.GetMethod(nameof(ITemplateRecargaLifecycleService.GetActiveTemplateSlotsAsync)).Should().NotBeNull();
        type.GetMethod(nameof(ITemplateRecargaLifecycleService.SyncSlotsToConfiguracionAsync)).Should().NotBeNull();
    }

    /// <summary>
    /// StartLoadingAsync returns TemplateRecargaDto and accepts templateId.
    /// </summary>
    [Fact]
    public void StartLoadingAsync_Signature_IsCorrect()
    {
        var method = typeof(ITemplateRecargaLifecycleService).GetMethod(nameof(ITemplateRecargaLifecycleService.StartLoadingAsync));
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<TemplateRecargaDto>));
        method.GetParameters().Should().HaveCount(1);
        method.GetParameters()[0].ParameterType.Should().Be(typeof(int));
    }

    /// <summary>
    /// FinalizeAsync returns TemplateRecargaDto and accepts templateId.
    /// </summary>
    [Fact]
    public void FinalizeAsync_Signature_IsCorrect()
    {
        var method = typeof(ITemplateRecargaLifecycleService).GetMethod(nameof(ITemplateRecargaLifecycleService.FinalizeAsync));
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<TemplateRecargaDto>));
        method.GetParameters().Should().HaveCount(1);
        method.GetParameters()[0].ParameterType.Should().Be(typeof(int));
    }

    /// <summary>
    /// CloseAsync returns TemplateRecargaDto and accepts templateId.
    /// </summary>
    [Fact]
    public void CloseAsync_Signature_IsCorrect()
    {
        var method = typeof(ITemplateRecargaLifecycleService).GetMethod(nameof(ITemplateRecargaLifecycleService.CloseAsync));
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<TemplateRecargaDto>));
        method.GetParameters().Should().HaveCount(1);
        method.GetParameters()[0].ParameterType.Should().Be(typeof(int));
    }

    /// <summary>
    /// ResetToDraftAsync returns TemplateRecargaDto and accepts templateId.
    /// </summary>
    [Fact]
    public void ResetToDraftAsync_Signature_IsCorrect()
    {
        var method = typeof(ITemplateRecargaLifecycleService).GetMethod(nameof(ITemplateRecargaLifecycleService.ResetToDraftAsync));
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<TemplateRecargaDto>));
        method.GetParameters().Should().HaveCount(1);
        method.GetParameters()[0].ParameterType.Should().Be(typeof(int));
    }

    /// <summary>
    /// GetActiveTemplateSlotsAsync returns List SnapshotSlotDto for a maquina.
    /// </summary>
    [Fact]
    public void GetActiveTemplateSlotsAsync_Signature_IsCorrect()
    {
        var method = typeof(ITemplateRecargaLifecycleService).GetMethod(nameof(ITemplateRecargaLifecycleService.GetActiveTemplateSlotsAsync));
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<List<SnapshotSlotDto>>));
        method.GetParameters().Should().HaveCount(1);
        method.GetParameters()[0].ParameterType.Should().Be(typeof(int));
    }

    /// <summary>
    /// SyncSlotsToConfiguracionAsync returns int (count of slots synced).
    /// </summary>
    [Fact]
    public void SyncSlotsToConfiguracionAsync_Signature_IsCorrect()
    {
        var method = typeof(ITemplateRecargaLifecycleService).GetMethod(nameof(ITemplateRecargaLifecycleService.SyncSlotsToConfiguracionAsync));
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<int>));
        method.GetParameters().Should().HaveCount(1);
        method.GetParameters()[0].ParameterType.Should().Be(typeof(int));
    }
}