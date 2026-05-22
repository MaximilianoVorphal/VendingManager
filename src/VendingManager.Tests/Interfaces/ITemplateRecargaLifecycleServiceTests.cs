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

        type.GetMethod(nameof(ITemplateRecargaLifecycleService.ActivarAsync)).Should().NotBeNull();
        type.GetMethod(nameof(ITemplateRecargaLifecycleService.TerminarAsync)).Should().NotBeNull();
        type.GetMethod(nameof(ITemplateRecargaLifecycleService.ReabrirAsync)).Should().NotBeNull();
        type.GetMethod(nameof(ITemplateRecargaLifecycleService.GetLatestActivoTemplateSlotsAsync)).Should().NotBeNull();
        type.GetMethod(nameof(ITemplateRecargaLifecycleService.SyncSlotsToConfiguracionAsync)).Should().NotBeNull();
    }

    /// <summary>
    /// ActivarAsync returns TemplateRecargaDto and accepts templateId.
    /// </summary>
    [Fact]
    public void ActivarAsync_Signature_IsCorrect()
    {
        var method = typeof(ITemplateRecargaLifecycleService).GetMethod(nameof(ITemplateRecargaLifecycleService.ActivarAsync));
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<TemplateRecargaDto>));
        method.GetParameters().Should().HaveCount(1);
        method.GetParameters()[0].ParameterType.Should().Be(typeof(int));
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
    /// GetLatestActivoTemplateSlotsAsync returns List SnapshotSlotDto for a maquina.
    /// </summary>
    [Fact]
    public void GetLatestActivoTemplateSlotsAsync_Signature_IsCorrect()
    {
        var method = typeof(ITemplateRecargaLifecycleService).GetMethod(nameof(ITemplateRecargaLifecycleService.GetLatestActivoTemplateSlotsAsync));
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
    /// Verifies FinalizeAsync is REMOVED (old Activo state, now Activo is terminal state).
    /// </summary>
    [Fact]
    public void FinalizeAsync_Removed_FromInterface()
    {
        var type = typeof(ITemplateRecargaLifecycleService);
        type.GetMethod("FinalizeAsync").Should().BeNull("FinalizeAsync removed from interface");
    }

    /// <summary>
    /// Verifies GetLatestTerminadoTemplateSlotsAsync is renamed to GetLatestActivoTemplateSlotsAsync.
    /// </summary>
    [Fact]
    public void GetLatestTerminadoTemplateSlotsAsync_Renamed_ToGetLatestActivoTemplateSlotsAsync()
    {
        var type = typeof(ITemplateRecargaLifecycleService);
        type.GetMethod("GetLatestTerminadoTemplateSlotsAsync").Should().BeNull("GetLatestTerminadoTemplateSlotsAsync renamed to GetLatestActivoTemplateSlotsAsync");
        type.GetMethod(nameof(ITemplateRecargaLifecycleService.GetLatestActivoTemplateSlotsAsync)).Should().NotBeNull();
    }
}