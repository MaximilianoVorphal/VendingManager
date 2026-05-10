namespace VendingManager.Tests.Services;

using Microsoft.EntityFrameworkCore;
using Moq;
using FluentAssertions;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Services;
using VendingManager.Tests.TestData;

public class TemplateRecargaService_FotoTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<Microsoft.Extensions.Logging.ILogger<TemplateRecargaService>> _mockLogger;
    private readonly TemplateRecargaService _service;

    public TemplateRecargaService_FotoTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"TemplateFotoTestDb_{Guid.NewGuid()}");
        _mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger<TemplateRecargaService>>();
        _service = new TemplateRecargaService(_context, _mockLogger.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    private async Task<PeriodoRecarga> SeedPeriodoWithFoto(int periodoId, string nombre, byte[]? fotoGuia, byte[]? fotoOcr)
    {
        var template = new TemplateRecarga
        {
            Id = 1,
            Nombre = "Test Template",
            FechaCreacion = DateTime.Now
        };
        _context.TemplatesRecarga.Add(template);

        var periodo = new PeriodoRecarga
        {
            Id = periodoId,
            TemplateRecargaId = template.Id,
            MaquinaId = 1,
            FechaRecarga = DateTime.Now.AddDays(-1),
            FotoGuia = fotoGuia,
            FotoOcr = fotoOcr
        };
        _context.PeriodosRecarga.Add(periodo);
        await _context.SaveChangesAsync();
        return periodo;
    }

    // ─── SaveFotoGuiaAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task SaveFotoGuiaAsync_HappyPath_StoresBytesAndReturns()
    {
        // Arrange
        var periodo = await SeedPeriodoWithFoto(1, "Test Periodo", null, null);
        var fotoBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic bytes
        var contentType = "image/png";

        // Act
        await _service.SaveFotoGuiaAsync(periodo.Id, fotoBytes, contentType);

        // Assert
        var updated = await _context.PeriodosRecarga.AsNoTracking().FirstAsync(p => p.Id == periodo.Id);
        updated.FotoGuia.Should().NotBeNull();
        updated.FotoGuia.Should().BeEquivalentTo(fotoBytes);
        // FotoOcr unchanged
        updated.FotoOcr.Should().BeNull();
    }

    [Fact]
    public async Task SaveFotoGuiaAsync_OverwriteExisting_ReplacesBytes()
    {
        // Arrange
        var originalBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG magic
        var periodo = await SeedPeriodoWithFoto(1, "Test Periodo", originalBytes, null);
        var newBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG

        // Act
        await _service.SaveFotoGuiaAsync(periodo.Id, newBytes, "image/png");

        // Assert
        var updated = await _context.PeriodosRecarga.AsNoTracking().FirstAsync(p => p.Id == periodo.Id);
        updated.FotoGuia.Should().BeEquivalentTo(newBytes);
    }

    [Fact]
    public async Task SaveFotoGuiaAsync_PeriodoNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var fotoBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var nonExistentId = 9999;

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.SaveFotoGuiaAsync(nonExistentId, fotoBytes, "image/png"));
    }

    // ─── GetFotoGuiaAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetFotoGuiaAsync_PhotoExists_ReturnsBytesAndContentType()
    {
        // Arrange
        var storedBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var periodo = await SeedPeriodoWithFoto(1, "Test Periodo", storedBytes, null);

        // Act
        var (data, contentType) = await _service.GetFotoGuiaAsync(periodo.Id);

        // Assert
        data.Should().NotBeNull();
        data.Should().BeEquivalentTo(storedBytes);
        contentType.Should().BeNull(); // Current impl doesn't store content type
    }

    [Fact]
    public async Task GetFotoGuiaAsync_NeverUploaded_ReturnsNullData()
    {
        // Arrange
        var periodo = await SeedPeriodoWithFoto(1, "Test Periodo", null, null);

        // Act
        var (data, contentType) = await _service.GetFotoGuiaAsync(periodo.Id);

        // Assert
        data.Should().BeNull();
    }

    [Fact]
    public async Task GetFotoGuiaAsync_PeriodoNotFound_ThrowsKeyNotFoundException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.GetFotoGuiaAsync(9999));
    }

    // ─── SaveFotoOcrAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task SaveFotoOcrAsync_HappyPath_StoresBytesAndReturns()
    {
        // Arrange
        var periodo = await SeedPeriodoWithFoto(1, "Test Periodo", null, null);
        var fotoBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var contentType = "image/png";

        // Act
        await _service.SaveFotoOcrAsync(periodo.Id, fotoBytes, contentType);

        // Assert
        var updated = await _context.PeriodosRecarga.AsNoTracking().FirstAsync(p => p.Id == periodo.Id);
        updated.FotoOcr.Should().NotBeNull();
        updated.FotoOcr.Should().BeEquivalentTo(fotoBytes);
        updated.FotoGuia.Should().BeNull();
    }

    [Fact]
    public async Task SaveFotoOcrAsync_OverwriteExisting_ReplacesBytes()
    {
        // Arrange
        var originalBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var periodo = await SeedPeriodoWithFoto(1, "Test Periodo", null, originalBytes);
        var newBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        // Act
        await _service.SaveFotoOcrAsync(periodo.Id, newBytes, "image/png");

        // Assert
        var updated = await _context.PeriodosRecarga.AsNoTracking().FirstAsync(p => p.Id == periodo.Id);
        updated.FotoOcr.Should().BeEquivalentTo(newBytes);
    }

    [Fact]
    public async Task SaveFotoOcrAsync_PeriodoNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var fotoBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.SaveFotoOcrAsync(9999, fotoBytes, "image/png"));
    }

    // ─── GetFotoOcrAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetFotoOcrAsync_PhotoExists_ReturnsBytesAndContentType()
    {
        // Arrange
        var storedBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var periodo = await SeedPeriodoWithFoto(1, "Test Periodo", null, storedBytes);

        // Act
        var (data, contentType) = await _service.GetFotoOcrAsync(periodo.Id);

        // Assert
        data.Should().NotBeNull();
        data.Should().BeEquivalentTo(storedBytes);
        contentType.Should().BeNull();
    }

    [Fact]
    public async Task GetFotoOcrAsync_NeverUploaded_ReturnsNullData()
    {
        // Arrange
        var periodo = await SeedPeriodoWithFoto(1, "Test Periodo", null, null);

        // Act
        var (data, contentType) = await _service.GetFotoOcrAsync(periodo.Id);

        // Assert
        data.Should().BeNull();
    }

    [Fact]
    public async Task GetFotoOcrAsync_PeriodoNotFound_ThrowsKeyNotFoundException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.GetFotoOcrAsync(9999));
    }
}
