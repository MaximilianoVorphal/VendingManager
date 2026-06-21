namespace VendingManager.Tests.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using VendingManager.Core.Configuration;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Services;
using VendingManager.Tests.TestData;

public class CompraServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IWebHostEnvironment> _mockEnv;
    private readonly IOptions<VendingConfig> _config;
    private readonly Mock<IProductMatchingService> _mockProductMatching;
    private readonly CompraService _service;

    public CompraServiceTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"CompraServiceTestDb_{Guid.NewGuid()}");

        _mockEnv = new Mock<IWebHostEnvironment>();
        _mockEnv.SetupGet(e => e.WebRootPath).Returns("/tmp/wwwroot");
        _mockEnv.SetupGet(e => e.ContentRootPath).Returns("/tmp");

        var vendingConfig = new VendingConfig();
        _config = Options.Create(vendingConfig);

        _mockProductMatching = new Mock<IProductMatchingService>();
        _mockProductMatching
            .Setup(m => m.SaveMappingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);

        var uploadProvider = new DefaultUploadPathProvider(_mockEnv.Object, _config);
        _service = new CompraService(_context, _mockProductMatching.Object, uploadProvider);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task RegistrarCompraAsync_ConItemPendienteSinProducto_DeberiaReproducirError()
    {
        // Arrange
        var compra = new Compra
        {
            Proveedor = "Proveedor Test",
            NumeroDocumento = "FACT-001",
            FechaCompra = new DateTime(2026, 5, 27, 10, 0, 0, DateTimeKind.Local),
            Estado = "PENDIENTE",
            PagadaCaja = false,
            TipoFactura = "MERCADERIA",
            MontoTotal = 0,
            Detalles = new List<DetalleCompra>
            {
                new()
                {
                    ProductoId = null,
                    EsPendiente = true,
                    DescripcionItem = null,
                    Cantidad = 1,
                    CostoUnitario = 0,
                    Subtotal = 0
                }
            }
        };

        // Act
        Exception? ex = null;
        try
        {
            var result = await _service.RegistrarCompraAsync(compra);
        }
        catch (Exception e)
        {
            ex = e;
        }

        // Assert — capture full exception chain
        if (ex != null)
        {
            var fullMessage = BuildExceptionChain(ex);
            throw new Xunit.Sdk.XunitException(
                $"ERROR REPRODUCIDO:\n" +
                $"Type: {ex.GetType().FullName}\n" +
                $"Full chain:\n{fullMessage}");
        }

        // Si no hay error, la compra se guardó correctamente
        var savedCompra = await _context.Compras
            .Include(c => c.Detalles)
            .FirstOrDefaultAsync(c => c.Id == compra.Id);
        savedCompra.Should().NotBeNull();
        savedCompra!.Detalles.Should().ContainSingle();
        savedCompra.Detalles[0].ProductoId.Should().BeNull();
        savedCompra.Detalles[0].EsPendiente.Should().BeTrue();
    }

    private static string BuildExceptionChain(Exception ex)
    {
        var parts = new List<string>();
        var current = ex;
        int depth = 0;
        while (current != null && depth < 10)
        {
            parts.Add($"[{depth}] {current.GetType().Name}: {current.Message}");
            current = current.InnerException;
            depth++;
        }
        return string.Join("\n", parts);
    }
}
