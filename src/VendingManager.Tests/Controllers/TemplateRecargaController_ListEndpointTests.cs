namespace VendingManager.Tests.Controllers;

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VendingManager.Controllers;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;

/// <summary>
/// Tests for TemplateRecargaController GET /api/TemplateRecarga/list endpoint.
/// Verifies the new lightweight list endpoint returns flat DTOs without nested Periodos.
/// </summary>
public class TemplateRecargaController_ListEndpointTests
{
    private readonly Mock<ITemplateRecargaService> _mockService;
    private readonly Mock<ITemplateRecargaLifecycleService> _mockLifecycle;
    private readonly TemplateRecargaController _controller;

    public TemplateRecargaController_ListEndpointTests()
    {
        _mockService = new Mock<ITemplateRecargaService>();
        _mockLifecycle = new Mock<ITemplateRecargaLifecycleService>();
        _controller = new TemplateRecargaController(_mockService.Object, _mockLifecycle.Object);
    }

    [Fact]
    public async Task GetList_ReturnsOkWithListItemDtos()
    {
        var items = new List<TemplateRecargaListItemDto>
        {
            new()
            {
                Id = 1,
                Nombre = "Recarga Test",
                Descripcion = "Test desc",
                MaquinaNombre = "Máquina 23",
                EsActivo = true,
                FechaCreacion = new DateTime(2025, 6, 15),
                Estado = EstadoTemplate.Terminado,
                PeriodoCount = 2,
                TotalProducts = 10
            }
        };
        _mockService.Setup(s => s.GetAllListAsync()).ReturnsAsync(items);

        var result = await _controller.GetList();

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returned = okResult.Value.Should().BeAssignableTo<List<TemplateRecargaListItemDto>>().Subject;
        returned.Should().HaveCount(1);
        returned[0].Id.Should().Be(1);
        returned[0].Nombre.Should().Be("Recarga Test");
        returned[0].PeriodoCount.Should().Be(2);
        returned[0].TotalProducts.Should().Be(10);
    }

    [Fact]
    public async Task GetList_EmptyDatabase_ReturnsEmptyList()
    {
        _mockService.Setup(s => s.GetAllListAsync()).ReturnsAsync(new List<TemplateRecargaListItemDto>());

        var result = await _controller.GetList();

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returned = okResult.Value.Should().BeAssignableTo<List<TemplateRecargaListItemDto>>().Subject;
        returned.Should().BeEmpty();
    }

    [Fact]
    public async Task GetList_ResponseHasNoNestedPeriodos()
    {
        var items = new List<TemplateRecargaListItemDto>
        {
            new()
            {
                Id = 1,
                Nombre = "Test",
                PeriodoCount = 3,
                TotalProducts = 15,
                Estado = EstadoTemplate.Terminado
            }
        };
        _mockService.Setup(s => s.GetAllListAsync()).ReturnsAsync(items);

        var result = await _controller.GetList();

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returned = okResult.Value.Should().BeAssignableTo<List<TemplateRecargaListItemDto>>().Subject;

        // Verify the DTO type has no Periodos or SnapshotSlots properties
        var props = typeof(TemplateRecargaListItemDto).GetProperties();
        props.Should().NotContain(p => p.Name == "Periodos",
            "list endpoint must return flat DTOs without nested Periodos");
        props.Should().NotContain(p => p.Name == "SnapshotSlots",
            "list endpoint must return flat DTOs without nested SnapshotSlots");
    }

    [Fact]
    public async Task GetList_CallsGetAllListAsync_NotGetAllAsync()
    {
        _mockService.Setup(s => s.GetAllListAsync())
            .ReturnsAsync(new List<TemplateRecargaListItemDto>());

        await _controller.GetList();

        _mockService.Verify(s => s.GetAllListAsync(), Times.Once);
        _mockService.Verify(s => s.GetAllAsync(), Times.Never,
            "list endpoint must call GetAllListAsync, not the heavy GetAllAsync");
    }

    [Fact]
    public async Task GetList_MultipleItems_ReturnsAllItems()
    {
        var items = new List<TemplateRecargaListItemDto>
        {
            new() { Id = 1, Nombre = "Template 1", Estado = EstadoTemplate.Terminado, PeriodoCount = 1, TotalProducts = 5 },
            new() { Id = 2, Nombre = "Template 2", Estado = EstadoTemplate.Pendiente, PeriodoCount = 3, TotalProducts = 0 },
            new() { Id = 3, Nombre = "Template 3", Estado = EstadoTemplate.Terminado, PeriodoCount = 2, TotalProducts = 8 }
        };
        _mockService.Setup(s => s.GetAllListAsync()).ReturnsAsync(items);

        var result = await _controller.GetList();

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returned = okResult.Value.Should().BeAssignableTo<List<TemplateRecargaListItemDto>>().Subject;
        returned.Should().HaveCount(3);
        returned[0].Nombre.Should().Be("Template 1");
        returned[1].Nombre.Should().Be("Template 2");
        returned[2].Nombre.Should().Be("Template 3");
    }
}
