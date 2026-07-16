namespace VendingManager.Tests.Services;

using Microsoft.AspNetCore.Http;
using Moq;
using VendingManager.Core.Entities;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.Enums;
using VendingManager.Tests.TestData;

/// <summary>
/// Covers <see cref="TransferenciaService.SaveComprobanteImagenAsync"/> signature
/// validation and DB storage. After PR 1, bytes are stored directly in the DB
/// varbinary column — no disk writes occur.
/// </summary>
public class TransferenciaServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly TransferenciaService _service;

    public TransferenciaServiceTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"TransferenciaServiceTestDb_{Guid.NewGuid()}");
        _service = new TransferenciaService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
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
    public async Task SaveComprobanteImagenAsync_SpoofedContent_ThrowsArgumentException_NoBytesStored()
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

        var fetched = await _context.Transferencias.FindAsync(transferencia.Id);
        fetched!.ComprobanteImagen.Should().BeNull();
        fetched.ComprobanteImagenContentType.Should().BeNull();
        fetched.ComprobanteImagenFileName.Should().BeNull();
    }

    [Fact]
    public async Task SaveComprobanteImagenAsync_ValidJpeg_StoresBytesInDb()
    {
        // Arrange
        var transferencia = await SeedTransferenciaAsync();
        byte[] jpegBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
        var validFile = CreateMockFormFile(jpegBytes, "image/jpeg", "comprobante.jpg");

        // Act
        await _service.SaveComprobanteImagenAsync(transferencia.Id, validFile);

        // Assert — stored in DB, no disk writes
        var fetched = await _context.Transferencias.FindAsync(transferencia.Id);
        fetched!.ComprobanteImagen.Should().BeEquivalentTo(jpegBytes);
        fetched.ComprobanteImagenContentType.Should().Be("image/jpeg");
        fetched.ComprobanteImagenFileName.Should().Be("comprobante.jpg");
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

    [Fact]
    public async Task SaveComprobanteImagenAsync_ValidPdf_StoresBytesInDb()
    {
        // Arrange
        var transferencia = await SeedTransferenciaAsync();
        byte[] pdfBytes = { 0x25, 0x50, 0x44, 0x46, 0x2D }; // %PDF- header
        var validFile = CreateMockFormFile(pdfBytes, "application/pdf", "comprobante.pdf");

        // Act
        await _service.SaveComprobanteImagenAsync(transferencia.Id, validFile);

        // Assert
        var fetched = await _context.Transferencias.FindAsync(transferencia.Id);
        fetched!.ComprobanteImagen.Should().BeEquivalentTo(pdfBytes);
        fetched.ComprobanteImagenContentType.Should().Be("application/pdf");
        fetched.ComprobanteImagenFileName.Should().Be("comprobante.pdf");
    }

    [Fact]
    public async Task SaveComprobanteImagenAsync_ReUpload_ReplacesBytes()
    {
        // Arrange
        var transferencia = await SeedTransferenciaAsync();
        byte[] firstBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 };
        var firstFile = CreateMockFormFile(firstBytes, "image/jpeg", "first.jpg");
        await _service.SaveComprobanteImagenAsync(transferencia.Id, firstFile);

        byte[] secondBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x01 };
        var secondFile = CreateMockFormFile(secondBytes, "image/jpeg", "second.jpg");

        // Act
        await _service.SaveComprobanteImagenAsync(transferencia.Id, secondFile);

        // Assert — old bytes replaced by new
        var fetched = await _context.Transferencias.FindAsync(transferencia.Id);
        fetched!.ComprobanteImagen.Should().BeEquivalentTo(secondBytes);
        fetched.ComprobanteImagenFileName.Should().Be("second.jpg");
    }
}
