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
    private readonly Mock<IProveedorMatchingService> _mockProveedorMatcher;
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

        _mockProveedorMatcher = new Mock<IProveedorMatchingService>();
        // Default: no supplier match (each test can override)
        _mockProveedorMatcher
            .Setup(m => m.MatchAsync(It.IsAny<string>()))
            .ReturnsAsync(new ProveedorMatchResult
            {
                ProveedorCatalog = null,
                Confidence = 0.0,
                SugerirCreacion = true,
                MatchMethod = ProveedorMatchMethod.None
            });

        _mockHttpHandler = new MockHttpMessageHandler();
        _mockHttpHandler.SetDefaultResponse(new HttpResponseMessage
        {
            StatusCode = System.Net.HttpStatusCode.OK,
            Content = new StringContent("{\"proveedor\":\"Test\",\"items\":[{\"producto\":\"Coca Cola\",\"cantidad\":1,\"costo_unitario\":500,\"subtotal\":500}]}")
        });

        _httpClient = new HttpClient(_mockHttpHandler);

        _mockConfig = new Mock<IConfiguration>();
        _mockConfig.Setup(c => c["ScraperServiceUrl"]).Returns("http://test:5000");

        // RED: FacturaOcrService constructor doesn't accept IProveedorMatchingService yet
        _service = new FacturaOcrService(_httpClient, _mockConfig.Object, _mockMatcher.Object, _mockProveedorMatcher.Object);
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

    // ─── Pack Unroll (PR 3) ───────────────────────────────────────────────

    [Fact]
    public async Task ExtractInvoiceDataAsync_WithPackSize_UnrollsQuantityAndCost()
    {
        // Arrange: mock match with PackSize=6
        _mockMatcher
            .Setup(m => m.MatchAsync("Coca Cola", "7791234567890", null, "Test"))
            .ReturnsAsync(new ProductMatchResult
            {
                Producto = new Producto { Id = 42, Nombre = "Coca Cola" },
                Confidence = 1.0,
                SugerirCreacion = false,
                MatchMethod = MatchMethod.Ean,
                ProductoEAN = new ProductoEAN
                {
                    EAN = "7791234567890",
                    ProductoId = 42,
                    PackSize = 6
                }
            });

        _mockHttpHandler.SetDefaultResponse(new HttpResponseMessage
        {
            StatusCode = System.Net.HttpStatusCode.OK,
            Content = new StringContent("{\"proveedor\":\"Test\",\"items\":[{\"producto\":\"Coca Cola\",\"ean\":\"7791234567890\",\"cantidad\":1,\"costo_unitario\":6000,\"subtotal\":6000}]}")
        });

        var file = CreateMockFile("test.jpg", "image/jpeg");

        // Act
        var result = await _service.ExtractInvoiceDataAsync(file);

        // Assert: pack unrolled 1×6 → 6 units at $1000 each
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        var item = result.Items[0];
        item.Cantidad.Should().Be(6);            // 1 × 6
        item.CostoUnitario.Should().Be(1000);    // 6000 / 6
        item.RequiereConfirmacionPack.Should().BeTrue();
        item.ProductoIdMatch.Should().Be(42);
    }

    [Fact]
    public async Task ExtractInvoiceDataAsync_PackSizeOne_NoChange()
    {
        // Arrange: mock match with PackSize=1 (not a pack)
        _mockMatcher
            .Setup(m => m.MatchAsync("Coca Cola", "7791234567890", null, "Test"))
            .ReturnsAsync(new ProductMatchResult
            {
                Producto = new Producto { Id = 42, Nombre = "Coca Cola" },
                Confidence = 1.0,
                SugerirCreacion = false,
                MatchMethod = MatchMethod.Ean,
                ProductoEAN = new ProductoEAN
                {
                    EAN = "7791234567890",
                    ProductoId = 42,
                    PackSize = 1
                }
            });

        _mockHttpHandler.SetDefaultResponse(new HttpResponseMessage
        {
            StatusCode = System.Net.HttpStatusCode.OK,
            Content = new StringContent("{\"proveedor\":\"Test\",\"items\":[{\"producto\":\"Coca Cola\",\"ean\":\"7791234567890\",\"cantidad\":2,\"costo_unitario\":3000,\"subtotal\":6000}]}")
        });

        var file = CreateMockFile("test.jpg", "image/jpeg");

        // Act
        var result = await _service.ExtractInvoiceDataAsync(file);

        // Assert: values unchanged
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        var item = result.Items[0];
        item.Cantidad.Should().Be(2);             // unchanged
        item.CostoUnitario.Should().Be(3000);     // unchanged
        item.RequiereConfirmacionPack.Should().BeFalse();
    }

    // ─── EAN Validation (PR 3) ────────────────────────────────────────────

    [Fact]
    public async Task ExtractInvoiceDataAsync_ValidEan13_Accepted()
    {
        // Arrange: mock matcher with valid 13-digit EAN
        _mockMatcher
            .Setup(m => m.MatchAsync("Coca Cola", "7791234567890", null, "Test"))
            .ReturnsAsync(new ProductMatchResult
            {
                Producto = new Producto { Id = 42, Nombre = "Coca Cola" },
                Confidence = 1.0,
                SugerirCreacion = false,
                MatchMethod = MatchMethod.Ean
            });

        _mockHttpHandler.SetDefaultResponse(new HttpResponseMessage
        {
            StatusCode = System.Net.HttpStatusCode.OK,
            Content = new StringContent("{\"proveedor\":\"Test\",\"items\":[{\"producto\":\"Coca Cola\",\"ean\":\"7791234567890\",\"cantidad\":1,\"costo_unitario\":500,\"subtotal\":500}]}")
        });

        var file = CreateMockFile("test.jpg", "image/jpeg");

        // Act
        var result = await _service.ExtractInvoiceDataAsync(file);

        // Assert: EAN was passed through to matcher
        _mockMatcher.Verify(m => m.MatchAsync("Coca Cola", "7791234567890", null, "Test"), Times.Once);
        result.Items[0].Ean.Should().Be("7791234567890");
    }

    [Fact]
    public async Task ExtractInvoiceDataAsync_ShortEan_Rejected()
    {
        // Arrange: 3-digit EAN is too short — server discards it
        _mockHttpHandler.SetDefaultResponse(new HttpResponseMessage
        {
            StatusCode = System.Net.HttpStatusCode.OK,
            Content = new StringContent("{\"proveedor\":\"Test\",\"items\":[{\"producto\":\"Coca Cola\",\"ean\":\"123\",\"cantidad\":1,\"costo_unitario\":500,\"subtotal\":500}]}")
        });

        var file = CreateMockFile("test.jpg", "image/jpeg");

        // Act
        var result = await _service.ExtractInvoiceDataAsync(file);

        // Assert: short EAN was discarded → MatchAsync receives null for ean
        _mockMatcher.Verify(m => m.MatchAsync("Coca Cola", null, It.IsAny<string?>(), "Test"), Times.Once);
        result.Items[0].Ean.Should().BeNull();
    }

    // ─── Proveedor Matching (PR2) ─────────────────────────────────────

    [Fact]
    public async Task ExtractInvoiceDataAsync_WithHighConfidenceProveedorMatch_SetsProveedorCatalogId()
    {
        // Arrange
        _mockProveedorMatcher
            .Setup(m => m.MatchAsync("Test"))
            .ReturnsAsync(new ProveedorMatchResult
            {
                ProveedorCatalog = new ProveedorCatalog { Id = 99, NombreCanonical = "Supplier Test" },
                Confidence = 0.95,
                SugerirCreacion = false,
                MatchMethod = ProveedorMatchMethod.Tokenized
            });

        var file = CreateMockFile("test.jpg", "image/jpeg");

        // Act
        var result = await _service.ExtractInvoiceDataAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.ProveedorCatalogId.Should().Be(99);
        result.Proveedor.Should().Be("Test"); // raw OCR string unchanged
        _mockProveedorMatcher.Verify(m => m.MatchAsync("Test"), Times.Once);
    }

    [Fact]
    public async Task ExtractInvoiceDataAsync_WithNoProveedorMatch_LeavesProveedorCatalogIdNull()
    {
        // Arrange — uses default setup: no match
        var file = CreateMockFile("test.jpg", "image/jpeg");

        // Act
        var result = await _service.ExtractInvoiceDataAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.ProveedorCatalogId.Should().BeNull();
        result.Proveedor.Should().Be("Test"); // raw OCR string unchanged
        _mockProveedorMatcher.Verify(m => m.MatchAsync("Test"), Times.Once);
    }

    [Fact]
    public async Task ExtractInvoiceDataAsync_WithNullProveedor_SkipsProveedorMatching()
    {
        // Arrange — no proveedor in OCR response
        _mockHttpHandler.SetDefaultResponse(new HttpResponseMessage
        {
            StatusCode = System.Net.HttpStatusCode.OK,
            Content = new StringContent("{\"proveedor\":null,\"items\":[{\"producto\":\"Coca Cola\",\"cantidad\":1,\"costo_unitario\":500,\"subtotal\":500}]}")
        });

        var file = CreateMockFile("test.jpg", "image/jpeg");

        // Act
        var result = await _service.ExtractInvoiceDataAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.ProveedorCatalogId.Should().BeNull();
        _mockProveedorMatcher.Verify(
            m => m.MatchAsync(It.IsAny<string>()),
            Times.Never);
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
