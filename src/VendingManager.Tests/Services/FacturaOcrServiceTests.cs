namespace VendingManager.Tests.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.DTOs;

public class FacturaOcrServiceTests
{
    private readonly Mock<IProductMatchingService> _mockMatcher;
    private readonly HttpClient _httpClient;
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly MockHttpMessageHandler _mockHttpHandler;
    private readonly FacturaOcrService _service;

    public FacturaOcrServiceTests()
    {
        _mockMatcher = new Mock<IProductMatchingService>();
        // Default: no match (each test can override)
        // Note: now uses the 4-param overload with EAN/SKU context
        _mockMatcher
            .Setup(m => m.MatchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(new ProductMatchResult
            {
                Producto = null,
                Confidence = 0.0,
                SugerirCreacion = true,
                MatchMethod = MatchMethod.None
            });

        // Default: save mapping is a no-op
        _mockMatcher
            .Setup(m => m.SaveMappingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);

        _mockHttpHandler = new MockHttpMessageHandler();
        _mockHttpHandler.SetDefaultResponse(new HttpResponseMessage
        {
            StatusCode = System.Net.HttpStatusCode.OK,
            Content = new StringContent("{\"proveedor\":\"Test\",\"items\":[{\"producto\":\"Coca Cola\",\"cantidad\":1,\"costo_unitario\":500,\"subtotal\":500}]}")
        });

        _httpClient = new HttpClient(_mockHttpHandler);

        _mockConfig = new Mock<IConfiguration>();
        _mockConfig.Setup(c => c["ScraperServiceUrl"]).Returns("http://test:5000");

        // RED: FacturaOcrService constructor doesn't accept IProductMatchingService yet
        _service = new FacturaOcrService(_httpClient, _mockConfig.Object, _mockMatcher.Object);
    }

    [Fact]
    public async Task ExtractInvoiceDataAsync_WithMatchingProduct_AssignsMatchResult()
    {
        // Arrange
        _mockMatcher
            .Setup(m => m.MatchAsync("Coca Cola", null, null, "Test"))
            .ReturnsAsync(new ProductMatchResult
            {
                Producto = new Producto { Id = 42, Nombre = "Coca Cola" },
                Confidence = 0.95,
                SugerirCreacion = false,
                MatchMethod = MatchMethod.Tokenized
            });

        var file = CreateMockFile("test.jpg", "image/jpeg");

        // Act
        var result = await _service.ExtractInvoiceDataAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        result.Items[0].ProductoIdMatch.Should().Be(42);
        result.Items[0].SugerirCreacion.Should().BeFalse();

        _mockMatcher.Verify(m => m.MatchAsync("Coca Cola", null, null, "Test"), Times.Once);
    }

    [Fact]
    public async Task ExtractInvoiceDataAsync_WithoutMatch_SetsSugerirCreacionTrue()
    {
        // Arrange — uses default setup from constructor (no match)
        var file = CreateMockFile("test.jpg", "image/jpeg");

        // Act
        var result = await _service.ExtractInvoiceDataAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        result.Items[0].ProductoIdMatch.Should().BeNull();
        result.Items[0].SugerirCreacion.Should().BeTrue();
    }

    [Fact]
    public async Task ExtractInvoiceDataAsync_SkipsNullProductNames()
    {
        // Arrange
        _mockHttpHandler.SetDefaultResponse(new HttpResponseMessage
        {
            StatusCode = System.Net.HttpStatusCode.OK,
            Content = new StringContent("{\"proveedor\":\"Test\",\"items\":[{\"producto\":null,\"cantidad\":1,\"costo_unitario\":500,\"subtotal\":500}]}")
        });

        var file = CreateMockFile("test.jpg", "image/jpeg");

        // Act
        var result = await _service.ExtractInvoiceDataAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        // Should NOT call the matcher for null product names and no EAN/SKU
        _mockMatcher.Verify(
            m => m.MatchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
    }

    // ─── HTTP Error Response ──────────────────────────────────────────────

    [Fact]
    public async Task ExtractInvoiceDataAsync_HttpError_ThrowsException()
    {
        // Arrange
        _mockHttpHandler.SetDefaultResponse(new HttpResponseMessage
        {
            StatusCode = System.Net.HttpStatusCode.InternalServerError,
            Content = new StringContent("Internal server error")
        });

        var file = CreateMockFile("test.jpg", "image/jpeg");

        // Act & Assert
        var act = () => _service.ExtractInvoiceDataAsync(file);
        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Internal server error*");
    }

    // ─── Empty Content Type with .jpg Extension ───────────────────────────

    [Fact]
    public async Task ExtractInvoiceDataAsync_EmptyContentType_WithJpgExtension_ResolvesMime()
    {
        // Arrange — file with empty content type, service must resolve by extension
        var file = CreateMockFile("test.jpg", "");

        // Act
        var result = await _service.ExtractInvoiceDataAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        _mockMatcher.Verify(m => m.MatchAsync("Coca Cola", null, null, "Test"), Times.Once);
    }

    private static IFormFile CreateMockFile(string fileName, string contentType)
    {
        var stream = new MemoryStream([0x01, 0x02, 0x03, 0x04]);
        var file = new Mock<IFormFile>();
        file.Setup(f => f.FileName).Returns(fileName);
        file.Setup(f => f.ContentType).Returns(contentType);
        file.Setup(f => f.Length).Returns(stream.Length);
        file.Setup(f => f.OpenReadStream()).Returns(stream);
        return file.Object;
    }
}

/// <summary>
/// Simplified mock HTTP message handler that returns a default response.
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    private HttpResponseMessage _response = new(System.Net.HttpStatusCode.OK);

    public void SetDefaultResponse(HttpResponseMessage response)
    {
        _response = response;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_response);
    }
}
