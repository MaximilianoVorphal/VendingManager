using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VendingManager.Shared.DTOs;
using VendingManager.Web.Components;
using VendingManager.Web.Services;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class PdcUnitCatalogTests
{
    [Fact]
    public void HardcodedUnits_ReturnsFourItemsInOrder()
    {
        var units = PdcUnitCatalog.HardcodedUnits;

        units.Should().HaveCount(4);

        // Item 0: Todas
        units[0].Id.Should().Be(0);
        units[0].Label.Should().Be("Todas");
        units[0].OurVendId.Should().BeNull();

        // Item 1: Máquina 001
        units[1].Id.Should().Be(1);
        units[1].Label.Should().Be("Máquina 001");
        units[1].OurVendId.Should().Be("2410280012");

        // Item 2: Máquina 002
        units[2].Id.Should().Be(2);
        units[2].Label.Should().Be("Máquina 002");
        units[2].OurVendId.Should().Be("2410280047");

        // Item 3: Máquina 003
        units[3].Id.Should().Be(3);
        units[3].Label.Should().Be("Máquina 003");
        units[3].OurVendId.Should().Be("2410280089");
    }
}

/// <summary>
/// Fake HTTP handler that returns whatever response was last set on it.
/// Defaults to 404 NotFound so the sidebar exercises its empty-state fallback.
/// </summary>
internal class ConfigurableFakeHandler : HttpMessageHandler
{
    public HttpResponseMessage? NextResponse { get; set; }
    public List<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(NextResponse ?? new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

public class PanelDeControlSidebarTests : TestContext
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly ConfigurableFakeHandler _handler = new();

    public PanelDeControlSidebarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var http = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost/") };
        Services.AddSingleton(http);
    }

    /// <summary>Configure the fake handler to return a machine-status payload with the given machines.</summary>
    private void SetStatusResponse(params MachineStatusDto[] machines)
    {
        var payload = new MachineStatusResponse { Machines = machines.ToList() };
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        _handler.NextResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>Render the sidebar with the auto-refresh timer disabled to keep tests deterministic.</summary>
    private IRenderedComponent<PanelDeControlSidebar> RenderWithNoRefresh()
        => RenderComponent<PanelDeControlSidebar>(p => p.Add(x => x.RefreshInterval, TimeSpan.Zero));

    [Fact]
    public void PanelDeControlSidebar_RendersAllFourItems()
    {
        var cut = RenderWithNoRefresh();
        var items = cut.FindAll(".pdc-unit-item");
        items.Should().HaveCount(4);
        items[0].TextContent.Trim().Should().Contain("Todas");
        items[1].TextContent.Trim().Should().Contain("Máquina 001");
        items[2].TextContent.Trim().Should().Contain("Máquina 002");
        items[3].TextContent.Trim().Should().Contain("Máquina 003");
    }

    [Fact]
    public void PanelDeControlSidebar_TodasItemIsActiveAndHasGlyph()
    {
        var cut = RenderWithNoRefresh();
        var items = cut.FindAll(".pdc-unit-item");
        items.Should().HaveCount(4);

        var todasItem = items[0];
        todasItem.ClassName.Should().Contain("pdc-unit-item--active");
        todasItem.InnerHtml.Should().Contain("◈");

        // Todas should NOT have a dot or secondary text
        todasItem.QuerySelector(".pdc-dot").Should().BeNull();
        todasItem.QuerySelector(".pdc-unit-item__secondary").Should().BeNull();
    }

    [Fact]
    public async Task PanelDeControlSidebar_OnlineMachinesHaveGreenDot()
    {
        SetStatusResponse(
            new MachineStatusDto { MachineId = "2410280012", Status = "online" },
            new MachineStatusDto { MachineId = "2410280047", Status = "online" },
            new MachineStatusDto { MachineId = "2410280089", Status = "online" });

        var cut = RenderWithNoRefresh();
        cut.WaitForState(() => cut.FindAll(".pdc-dot--ok").Count == 3);

        var items = cut.FindAll(".pdc-unit-item");
        items[1].QuerySelector(".pdc-dot.pdc-dot--ok").Should().NotBeNull();
        items[2].QuerySelector(".pdc-dot.pdc-dot--ok").Should().NotBeNull();
        items[3].QuerySelector(".pdc-dot.pdc-dot--ok").Should().NotBeNull();
    }

    [Fact]
    public async Task PanelDeControlSidebar_StockLowMachineHasOrangeDot()
    {
        SetStatusResponse(
            new MachineStatusDto { MachineId = "2410280012", Status = "online" },
            new MachineStatusDto { MachineId = "2410280047", Status = "online" },
            new MachineStatusDto { MachineId = "2410280089", Status = "warning" });

        var cut = RenderWithNoRefresh();
        cut.WaitForState(() => cut.FindAll(".pdc-dot--warn").Count == 1);

        var machine3 = cut.FindAll(".pdc-unit-item")[3];
        machine3.QuerySelector(".pdc-dot.pdc-dot--warn").Should().NotBeNull();
        var secondary = machine3.QuerySelector(".pdc-unit-item__secondary--warn");
        secondary.Should().NotBeNull();
        secondary!.TextContent.Trim().Should().Be("2410280089 · Stock bajo");
    }

    [Fact]
    public async Task PanelDeControlSidebar_OfflineMachineHasRedDot()
    {
        SetStatusResponse(
            new MachineStatusDto { MachineId = "2410280012", Status = "offline" });

        var cut = RenderWithNoRefresh();
        cut.WaitForState(() => cut.FindAll(".pdc-dot--err").Count == 1);

        var machine1 = cut.FindAll(".pdc-unit-item")[1];
        machine1.QuerySelector(".pdc-dot.pdc-dot--err").Should().NotBeNull();
        var secondary = machine1.QuerySelector(".pdc-unit-item__secondary--err");
        secondary.Should().NotBeNull();
        secondary!.TextContent.Trim().Should().Be("2410280012 · Sin conexión");
    }

    [Fact]
    public async Task PanelDeControlSidebar_RendersSecondaryText()
    {
        SetStatusResponse(
            new MachineStatusDto { MachineId = "2410280012", Status = "online" },
            new MachineStatusDto { MachineId = "2410280047", Status = "online" },
            new MachineStatusDto { MachineId = "2410280089", Status = "warning" });

        var cut = RenderWithNoRefresh();
        cut.WaitForState(() => cut.FindAll(".pdc-unit-item__secondary").Count == 3);

        var items = cut.FindAll(".pdc-unit-item");
        items[1].QuerySelector(".pdc-unit-item__secondary")!.TextContent.Trim()
            .Should().Be("2410280012 · Online");
        items[2].QuerySelector(".pdc-unit-item__secondary")!.TextContent.Trim()
            .Should().Be("2410280047 · Online");
        items[3].QuerySelector(".pdc-unit-item__secondary")!.TextContent.Trim()
            .Should().Be("2410280089 · Stock bajo");
    }

    [Fact]
    public void PanelDeControlSidebar_RendersFooterWithCount()
    {
        var cut = RenderWithNoRefresh();
        cut.Markup.Should().Contain("3 unidades activas");
    }

    [Fact]
    public async Task PanelDeControlSidebar_ClickInvokesCallback()
    {
        var invokedId = -1;
        var cut = RenderComponent<PanelDeControlSidebar>(p => p
            .Add(x => x.OnUnitSelected, EventCallback.Factory.Create<int>(this, id => invokedId = id))
            .Add(x => x.RefreshInterval, TimeSpan.Zero));

        cut.FindAll(".pdc-unit-item button").Should().HaveCount(4);

        await cut.InvokeAsync(() => cut.FindAll(".pdc-unit-item button")[1].Click());
        invokedId.Should().Be(1);

        await cut.InvokeAsync(() => cut.FindAll(".pdc-unit-item button")[0].Click());
        invokedId.Should().Be(0);
    }

    [Fact]
    public void PanelDeControlSidebar_RendersWhenApiUnavailable()
    {
        // No response set — fake handler returns 404. The sidebar should still render
        // and fall back to the default Online status (green dot) for all real machines.
        var cut = RenderWithNoRefresh();
        cut.FindAll(".pdc-sidebar").Should().HaveCount(1);
        cut.FindAll(".pdc-unit-item").Should().HaveCount(4);
    }

    [Fact]
    public void PanelDeControlSidebar_HeaderRendersUnidades()
    {
        var cut = RenderWithNoRefresh();
        cut.Markup.Should().Contain("UNIDADES");
    }

    [Fact]
    public void PanelDeControlSidebar_HitsMachineStatusEndpointOnInit()
    {
        SetStatusResponse(); // empty list
        RenderWithNoRefresh();

        _handler.Requests.Should().ContainSingle(r =>
            r.RequestUri != null && r.RequestUri.AbsolutePath.Contains("machine-status"));
    }
}
