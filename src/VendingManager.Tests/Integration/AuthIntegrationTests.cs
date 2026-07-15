using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VendingManager.Controllers;
using VendingManager.Shared.Constants;
using Xunit;

namespace VendingManager.Tests.Integration;

/// <summary>
/// Attribute verification tests for auth on protected controllers and Razor pages.
/// Verifies [Authorize] and [Authorize(Roles = Roles.Admin)] attributes
/// are correctly placed on controller classes, mutation actions, and page types.
/// </summary>
public class AuthIntegrationTests
{
    // ── Helper ─────────────────────────────────────────────────────────

    private static bool HasAuthAttribute(Type controllerType)
    {
        return controllerType.GetCustomAttribute<AuthorizeAttribute>() is not null;
    }

    private static bool HasPlainAuthAttribute(Type controllerType)
    {
        var attr = controllerType.GetCustomAttribute<AuthorizeAttribute>();
        return attr is not null && string.IsNullOrEmpty(attr.Roles);
    }

    private static bool HasAdminAttribute(MethodInfo action)
    {
        var attr = action.GetCustomAttribute<AuthorizeAttribute>();
        return attr is not null && attr.Roles == Roles.Admin;
    }

    private static bool HasAdminAttribute(Type type)
    {
        var attrs = type.GetCustomAttributes<AuthorizeAttribute>();
        return attrs.Any(a => a.Roles == Roles.Admin);
    }

    private static bool HasHttpPostAttribute(MethodInfo action)
    {
        return action.GetCustomAttribute<HttpPostAttribute>() is not null;
    }

    // ── OrdenCargaController ───────────────────────────────────────────

    [Fact]
    public void OrdenCargaController_HasClassLevelAuthorize()
    {
        HasPlainAuthAttribute(typeof(OrdenCargaController)).Should().BeTrue(
            "OrdenCargaController must have plain [Authorize] at class level (no Roles)");
    }

    [Fact]
    public void OrdenCargaController_CrearOrden_HasHttpPost()
    {
        var method = typeof(OrdenCargaController).GetMethod(nameof(OrdenCargaController.CrearOrden));
        method.Should().NotBeNull();
        HasHttpPostAttribute(method!).Should().BeTrue(
            "CrearOrden must have [HttpPost]");
    }

    [Fact]
    public void OrdenCargaController_CrearOrden_HasAdminAuth()
    {
        var method = typeof(OrdenCargaController).GetMethod(nameof(OrdenCargaController.CrearOrden));
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue(
            "CrearOrden must have [Authorize(Roles = Roles.Admin)]");
    }

    [Fact]
    public void OrdenCargaController_FinalizarOrden_HasAdminAuth()
    {
        var method = typeof(OrdenCargaController).GetMethod(nameof(OrdenCargaController.FinalizarOrden));
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void OrdenCargaController_ActualizarNombre_HasAdminAuth()
    {
        var method = typeof(OrdenCargaController).GetMethod(nameof(OrdenCargaController.ActualizarNombre));
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void OrdenCargaController_ActualizarOrden_HasAdminAuth()
    {
        var method = typeof(OrdenCargaController).GetMethod(nameof(OrdenCargaController.ActualizarOrden));
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void OrdenCargaController_Confirmar_HasAdminAuth()
    {
        var method = typeof(OrdenCargaController).GetMethod(nameof(OrdenCargaController.Confirmar));
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void OrdenCargaController_ExtractFromPhoto_HasAdminAuth()
    {
        var method = typeof(OrdenCargaController).GetMethod(nameof(OrdenCargaController.ExtractFromPhoto));
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    // ── InformesController (VendingManager.Web.Controllers namespace) ──

    [Fact]
    public void InformesController_HasClassLevelAuthorize()
    {
        var type = typeof(VendingManager.Web.Controllers.InformesController);
        HasPlainAuthAttribute(type).Should().BeTrue(
            "InformesController must have plain [Authorize] at class level (no Roles)");
    }

    [Fact]
    public void InformesController_Upload_HasAdminAuth()
    {
        var type = typeof(VendingManager.Web.Controllers.InformesController);
        var method = type.GetMethod("Upload");
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void InformesController_Delete_HasAdminAuth()
    {
        var type = typeof(VendingManager.Web.Controllers.InformesController);
        var method = type.GetMethod("Delete");
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    // ── MaquinasController ─────────────────────────────────────────────

    [Fact]
    public void MaquinasController_HasClassLevelAuthorize()
    {
        HasPlainAuthAttribute(typeof(MaquinasController)).Should().BeTrue(
            "MaquinasController must have plain [Authorize] at class level (no Roles)");
    }

    [Fact]
    public void MaquinasController_PostMaquina_HasAdminAuth()
    {
        var method = typeof(MaquinasController).GetMethod(nameof(MaquinasController.PostMaquina));
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void MaquinasController_PutMaquina_HasAdminAuth()
    {
        var method = typeof(MaquinasController).GetMethod(nameof(MaquinasController.PutMaquina));
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void MaquinasController_DeleteMaquina_HasAdminAuth()
    {
        var method = typeof(MaquinasController).GetMethod(nameof(MaquinasController.DeleteMaquina));
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void MaquinasController_UpdateSlot_HasAdminAuth()
    {
        var method = typeof(MaquinasController).GetMethod(nameof(MaquinasController.UpdateSlot));
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void MaquinasController_ProcesarMovimientos_HasAdminAuth()
    {
        var method = typeof(MaquinasController).GetMethod(nameof(MaquinasController.ProcesarMovimientos));
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    // ── TemplateRecargaController ──────────────────────────────────────

    [Fact]
    public void TemplateRecargaController_HasClassLevelAuthorize()
    {
        HasPlainAuthAttribute(typeof(TemplateRecargaController)).Should().BeTrue(
            "TemplateRecargaController must have plain [Authorize] at class level (no Roles)");
    }

    [Fact]
    public void TemplateRecargaController_Create_HasAdminAuth()
    {
        var method = typeof(TemplateRecargaController).GetMethod("Create");
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void TemplateRecargaController_Update_HasAdminAuth()
    {
        var method = typeof(TemplateRecargaController).GetMethod("Update");
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void TemplateRecargaController_Delete_HasAdminAuth()
    {
        var method = typeof(TemplateRecargaController).GetMethod("Delete");
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void TemplateRecargaController_Terminar_HasAdminAuth()
    {
        var method = typeof(TemplateRecargaController).GetMethod("Terminar");
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void TemplateRecargaController_Reabrir_HasAdminAuth()
    {
        var method = typeof(TemplateRecargaController).GetMethod("Reabrir");
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void TemplateRecargaController_SlotBatch_HasAdminAuth()
    {
        var method = typeof(TemplateRecargaController).GetMethod("SlotBatch");
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void TemplateRecargaController_SincronizarVentas_HasAdminAuth()
    {
        var method = typeof(TemplateRecargaController).GetMethod("SincronizarVentas");
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void TemplateRecargaController_SincronizarTodasVentas_HasAdminAuth()
    {
        var method = typeof(TemplateRecargaController).GetMethod("SincronizarTodasVentas");
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void TemplateRecargaController_SyncSlotProducto_HasAdminAuth()
    {
        var method = typeof(TemplateRecargaController).GetMethod("SyncSlotProducto");
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void TemplateRecargaController_UploadFotoGuia_HasAdminAuth()
    {
        var method = typeof(TemplateRecargaController).GetMethod("UploadFotoGuia");
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void TemplateRecargaController_DeleteFotoGuia_HasAdminAuth()
    {
        var method = typeof(TemplateRecargaController).GetMethod("DeleteFotoGuia");
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void TemplateRecargaController_UploadFotoOcr_HasAdminAuth()
    {
        var method = typeof(TemplateRecargaController).GetMethod("UploadFotoOcr");
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void TemplateRecargaController_DeleteFotoOcr_HasAdminAuth()
    {
        var method = typeof(TemplateRecargaController).GetMethod("DeleteFotoOcr");
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    // ── ProductosController ────────────────────────────────────────────

    [Fact]
    public void ProductosController_HasClassLevelAuthorize()
    {
        HasPlainAuthAttribute(typeof(ProductosController)).Should().BeTrue(
            "ProductosController must have plain [Authorize] at class level (no Roles)");
    }

    [Fact]
    public void ProductosController_PostProducto_HasAdminAuth()
    {
        var method = typeof(ProductosController).GetMethod(nameof(ProductosController.PostProducto));
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void ProductosController_PutProducto_HasAdminAuth()
    {
        var method = typeof(ProductosController).GetMethod(nameof(ProductosController.PutProducto));
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void ProductosController_DeleteProducto_HasAdminAuth()
    {
        var method = typeof(ProductosController).GetMethod(nameof(ProductosController.DeleteProducto));
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void ProductosController_SubirCatalogo_HasAdminAuth()
    {
        var method = typeof(ProductosController).GetMethod(nameof(ProductosController.SubirCatalogo));
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void ProductosController_AjustarStock_HasAdminAuth()
    {
        var method = typeof(ProductosController).GetMethod(nameof(ProductosController.AjustarStock));
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void ProductosController_CreateEanMapping_HasAdminAuth()
    {
        var method = typeof(ProductosController).GetMethod(nameof(ProductosController.CreateEanMapping));
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void ProductosController_UpdateEanMapping_HasAdminAuth()
    {
        var method = typeof(ProductosController).GetMethod(nameof(ProductosController.UpdateEanMapping));
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    [Fact]
    public void ProductosController_DeleteEanMapping_HasAdminAuth()
    {
        var method = typeof(ProductosController).GetMethod(nameof(ProductosController.DeleteEanMapping));
        method.Should().NotBeNull();
        HasAdminAttribute(method!).Should().BeTrue();
    }

    // ── Dead action revival ───────────────────────────────────────────

    [Fact]
    public void VentasController_SubirVentasMaquina_HasHttpPost()
    {
        var method = typeof(VentasController).GetMethod("SubirVentasMaquina");
        method.Should().NotBeNull();
        HasHttpPostAttribute(method!).Should().BeTrue(
            "SubirVentasMaquina must have [HttpPost]");
    }

    [Fact]
    public void InventarioController_SubirCatalogo_HasHttpPost()
    {
        var type = typeof(VendingManager.Web.Controllers.InventarioController);
        var method = type.GetMethod("SubirCatalogo");
        method.Should().NotBeNull();
        HasHttpPostAttribute(method!).Should().BeTrue(
            "SubirCatalogo must have [HttpPost]");
    }

    // ── Razor page auth ──────────────────────────────────────────────

    [Fact]
    public void RecargaMovil_HasAdminAuth()
    {
        var type = typeof(VendingManager.Web.Pages.RecargaMovil);
        HasAdminAttribute(type).Should().BeTrue(
            "RecargaMovil page must have [Authorize(Roles = Roles.Admin)]");
    }
}
