namespace VendingManager.Tests.Interfaces;

using FluentAssertions;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

public class ITemplateRecargaLifecycleServiceTests
{
    /// <summary>
    /// Verifies the interface defines the expected state-transition methods.
    /// State machine: Pendiente (0) ↔ Terminado (2).
    /// ActivarAsync removed (intermediate Activo state eliminated).
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
    /// ActivarAsync no longer exists (intermediate Activo state eliminated).
    /// </summary>
    [Fact]
    public void ActivarAsync_Removed_FromInterface()
    {
        var type = typeof(ITemplateRecargaLifecycleService);
        type.GetMethod("ActivarAsync").Should().BeNull(
            "ActivarAsync removed — Activo state no longer exists in the lifecycle");
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
    /// GetLatestActivoTemplateSlotsAsync no longer exists (renamed to GetLatestTerminadoTemplateSlotsAsync).
    /// </summary>
    [Fact]
    public void GetLatestActivoTemplateSlotsAsync_Removed_FromInterface()
    {
        var type = typeof(ITemplateRecargaLifecycleService);
        type.GetMethod("GetLatestActivoTemplateSlotsAsync").Should().BeNull(
            "GetLatestActivoTemplateSlotsAsync renamed to GetLatestTerminadoTemplateSlotsAsync");
        type.GetMethod(nameof(ITemplateRecargaLifecycleService.GetLatestTerminadoTemplateSlotsAsync)).Should().NotBeNull();
    }
}