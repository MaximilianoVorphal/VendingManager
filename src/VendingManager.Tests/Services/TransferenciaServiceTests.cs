namespace VendingManager.Tests.Services;

using Microsoft.AspNetCore.Http;
using Moq;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.Enums;
using VendingManager.Tests.TestData;

/// <summary>
/// Covers <see cref="TransferenciaService.SaveComprobanteImagenAsync"/> signature
/// validation (M-1b, REQ-UPLOAD-02). This is the one live on-disk write path among
/// the upload sites — spoofed content must be rejected before anything is written.
/// </summary>
public class TransferenciaServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IUploadPathProvider> _mockUploadPathProvider;
    private readonly string _uploadBasePath;
    private readonly TransferenciaService _service;

    public TransferenciaServiceTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"TransferenciaServiceTestDb_{Guid.NewGuid()}");

        _uploadBasePath = Path.Combine(Path.GetTempPath(), $"vm-transferencia-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_uploadBasePath);

        _mockUploadPathProvider = new Mock<IUploadPathProvider>();
        _mockUploadPathProvider.Setup(p => p.GetUploadBasePath()).Returns(_uploadBasePath);

        _service = new TransferenciaService(_context, _mockUploadPathProvider.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (Directory.Exists(_uploadBasePath))
            Directory.Delete(_uploadBasePath, recursive: true);
    }

    private static IFormFile CreateMockFormFile(byte[] content, string contentType, string fileName)
    {
        var stream = new MemoryStream(content);
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(content.Length);
        fileMock.Setup(f => f.ContentType).Returns(contentType);
        fileMock.Setup(f => f.FileName).Returns(fileName);
        fileMock.Setup(f => f.OpenReadStream()).Returns(stream);
        fileMock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns((Stream s, CancellationToken ct) => stream.CopyToAsync(s, ct));
        return fileMock.Object;
    }

    private async Task<Transferencia> SeedTransferenciaAsync()
    {
        var transferencia = new Transferencia
        {
            Fecha = new DateTime(2026, 6, 1),
            Monto = 1000,
            Estado = TransferenciaEstado.Pendiente
        };
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();
        return transferencia;
    }

    [Fact]
    public async Task SaveComprobanteImagenAsync_SpoofedContent_ThrowsArgumentException_NoDiskWriteOccurs()
    {
        // Arrange
        var transferencia = await SeedTransferenciaAsync();
        var spoofedFile = CreateMockFormFile(
            System.Text.Encoding.ASCII.GetBytes("this is not really a jpeg"),
            "image/jpeg",
            "comprobante.jpg");

        // Act
        var act = () => _service.SaveComprobanteImagenAsync(transferencia.Id, spoofedFile);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();

        var transferenciasDir = Path.Combine(_uploadBasePath, "uploads", "transferencias");
        if (Directory.Exists(transferenciasDir))
        {
            Directory.GetFiles(transferenciasDir).Should().BeEmpty();
        }

        var fetched = await _context.Transferencias.FindAsync(transferencia.Id);
        fetched!.ComprobanteImagenPath.Should().BeNull();
    }

    [Fact]
    public async Task SaveComprobanteImagenAsync_ValidJpeg_WritesToDiskUnchanged()
    {
        // Arrange
        var transferencia = await SeedTransferenciaAsync();
        byte[] jpegBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
        var validFile = CreateMockFormFile(jpegBytes, "image/jpeg", "comprobante.jpg");

        // Act
        var relativePath = await _service.SaveComprobanteImagenAsync(transferencia.Id, validFile);

        // Assert
        relativePath.Should().StartWith("/uploads/transferencias/");
        var physicalPath = Path.Combine(_uploadBasePath, relativePath.TrimStart('/'));
        File.Exists(physicalPath).Should().BeTrue();
        var writtenBytes = await File.ReadAllBytesAsync(physicalPath);
        writtenBytes.Should().BeEquivalentTo(jpegBytes);

        var fetched = await _context.Transferencias.FindAsync(transferencia.Id);
        fetched!.ComprobanteImagenPath.Should().Be(relativePath);
    }

    [Fact]
    public async Task SaveComprobanteImagenAsync_MissingTransferencia_ThrowsKeyNotFoundException()
    {
        // Arrange
        byte[] jpegBytes = { 0xFF, 0xD8, 0xFF, 0xE0 };
        var validFile = CreateMockFormFile(jpegBytes, "image/jpeg", "comprobante.jpg");

        // Act
        var act = () => _service.SaveComprobanteImagenAsync(99999, validFile);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
