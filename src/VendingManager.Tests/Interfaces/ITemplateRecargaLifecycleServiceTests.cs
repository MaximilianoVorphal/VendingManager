namespace VendingManager.Tests.Interfaces;

using FluentAssertions;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

public class ITemplateRecargaLifecycleServiceTests
{
    /// <summary>
    /// Verifies the interface defines the expected state-transition methods.
    /// State machine: Pendiente (0) ↔ Terminado (1).
    /// StartLoadingAsync and FinalizeAsync removed (EnCarga/Activo states gone).
    /// </summary>
    [Fact]
    public void ITemplateRecargaLifecycleService_HasRequiredMethods()
    {
        var type = typeof(ITemplateRecargaLifecycleService);

        type.GetMethod(nameof(ITemplateRecargaLifecycleService.TerminarAsync)).Should().NotBeNull();
        type.GetMethod(nameof(ITemplateRecargaLifecycleService.ReabrirAsync)).Should().NotBeNull();
        type.GetMethod(nameof(ITemplateRecargaLifecycleService.GetLatestTerminadoTemplateSlotsAsync)).Should().NotBeNull();
        type.GetMethod(nameof(ITemplateRecargaLifecycleService.SyncSlotsToConfiguracionAsync)).Should().NotBeNull();
    }

    /// <summary>
    /// TerminarAsync returns TemplateRecargaDto and accepts templateId.
    /// </summary>
    [Fact]
    public void TerminarAsync_Signature_IsCorrect()
    {
        var method = typeof(ITemplateRecargaLifecycleService).GetMethod(nameof(ITemplateRecargaLifecycleService.TerminarAsync));
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<TemplateRecargaDto>));
        method.GetParameters().Should().HaveCount(1);
        method.GetParameters()[0].ParameterType.Should().Be(typeof(int));
    }

    /// <summary>
    /// ReabrirAsync returns TemplateRecargaDto and accepts templateId.
    /// </summary>
    [Fact]
    public void ReabrirAsync_Signature_IsCorrect()
    {
        var method = typeof(ITemplateRecargaLifecycleService).GetMethod(nameof(ITemplateRecargaLifecycleService.ReabrirAsync));
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<TemplateRecargaDto>));
        method.GetParameters().Should().HaveCount(1);
        method.GetParameters()[0].ParameterType.Should().Be(typeof(int));
    }

    /// <summary>
    /// GetLatestTerminadoTemplateSlotsAsync returns List SnapshotSlotDto for a maquina.
    /// </summary>
    [Fact]
    public void GetLatestTerminadoTemplateSlotsAsync_Signature_IsCorrect()
    {
        var method = typeof(ITemplateRecargaLifecycleService).GetMethod(nameof(ITemplateRecargaLifecycleService.GetLatestTerminadoTemplateSlotsAsync));
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

    /// <summary>
    /// Verifies StartLoadingAsync is REMOVED (EnCarga state gone).
    /// </summary>
    [Fact]
    public void StartLoadingAsync_Removed_FromInterface()
    {
        var type = typeof(ITemplateRecargaLifecycleService);
        type.GetMethod("StartLoadingAsync").Should().BeNull("EnCarga state no longer exists");
    }

    /// <summary>
    /// Verifies FinalizeAsync is REMOVED (Activo state gone).
    /// </summary>
    [Fact]
    public void FinalizeAsync_Removed_FromInterface()
    {
        var type = typeof(ITemplateRecargaLifecycleService);
        type.GetMethod("FinalizeAsync").Should().BeNull("Activo state no longer exists");
    }

    /// <summary>
    /// Verifies CloseAsync is renamed to TerminarAsync.
    /// </summary>
    [Fact]
    public void CloseAsync_Renamed_ToTerminarAsync()
    {
        var type = typeof(ITemplateRecargaLifecycleService);
        type.GetMethod("CloseAsync").Should().BeNull("CloseAsync renamed to TerminarAsync");
        type.GetMethod(nameof(ITemplateRecargaLifecycleService.TerminarAsync)).Should().NotBeNull();
    }

    /// <summary>
    /// Verifies ResetToDraftAsync is renamed to ReabrirAsync.
    /// </summary>
    [Fact]
    public void ResetToDraftAsync_Renamed_ToReabrirAsync()
    {
        var type = typeof(ITemplateRecargaLifecycleService);
        type.GetMethod("ResetToDraftAsync").Should().BeNull("ResetToDraftAsync renamed to ReabrirAsync");
        type.GetMethod(nameof(ITemplateRecargaLifecycleService.ReabrirAsync)).Should().NotBeNull();
    }

    /// <summary>
    /// Verifies GetActiveTemplateSlotsAsync is renamed to GetLatestTerminadoTemplateSlotsAsync.
    /// </summary>
    [Fact]
    public void GetActiveTemplateSlotsAsync_Renamed_ToGetLatestTerminadoTemplateSlotsAsync()
    {
        var type = typeof(ITemplateRecargaLifecycleService);
        type.GetMethod("GetActiveTemplateSlotsAsync").Should().BeNull("GetActiveTemplateSlotsAsync renamed to GetLatestTerminadoTemplateSlotsAsync");
        type.GetMethod(nameof(ITemplateRecargaLifecycleService.GetLatestTerminadoTemplateSlotsAsync)).Should().NotBeNull();
    }
}