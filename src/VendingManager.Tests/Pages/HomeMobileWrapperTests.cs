using Bunit;
using FluentAssertions;
using Moq;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using VendingManager.Web.Pages;
using Xunit;

namespace VendingManager.Tests.Pages;

public class HomeMobileWrapperTests : TestContext
{
    private readonly Mock<HttpClient> _httpClientMock = new();
    private readonly Mock<NavigationManager> _navigationManagerMock = new();
    private readonly Mock<IJSRuntime> _jsRuntimeMock = new();

    public HomeMobileWrapperTests()
    {
        // Setup default responses for Home's API calls
        var emptyMaquinas = new object[] { };
        var emptyStats = new { Hoy = new {}, Semana = new {}, Mes = new {}, CantidadStockCritico = 0 };
        
        _httpClientMock
            .Setup(x => x.GetFromJsonAsync<List<object>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<object>());

        _httpClientMock
            .Setup(x => x.GetFromJsonAsync<object>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new object());
    }

    [Fact]
    public void Home_RendersWithMobileShellWrapper()
    {
        // Arrange
        Services.AddSingleton(_httpClientMock.Object);
        Services.AddSingleton(_navigationManagerMock.Object);
        Services.AddSingleton(_jsRuntimeMock.Object);

        // Act
        var cut = RenderComponent<Home>();

        // Assert
        var shell = cut.Find(".vm-mobile-shell");
        shell.Should().NotBeNull("Home page should be wrapped in MobileShell");
    }

    [Fact]
    public void Home_MobileShellContainsOriginalContent()
    {
        // Arrange
        Services.AddSingleton(_httpClientMock.Object);
        Services.AddSingleton(_navigationManagerMock.Object);
        Services.AddSingleton(_jsRuntimeMock.Object);

        // Act
        var cut = RenderComponent<Home>();

        // Assert
        cut.Markup.Should().Contain("industrial-wrapper", "Original industrial wrapper should be inside MobileShell");
    }
}