namespace VendingManager.Tests.Controllers;

using System.IO;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VendingManager.Controllers;
using VendingManager.Core.Interfaces;

public class TemplateRecargaController_FotoTests
{
    private readonly Mock<ITemplateRecargaService> _mockService;
    private readonly TemplateRecargaController _controller;
    private const int TemplateId = 1;
    private const int PeriodoId = 10;

    public TemplateRecargaController_FotoTests()
    {
        _mockService = new Mock<ITemplateRecargaService>();
        _controller = new TemplateRecargaController(_mockService.Object);
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

    private static IFormFile CreateOversizedMockFormFile(int sizeMb, string contentType)
    {
        var content = new byte[sizeMb * 1024 * 1024];
        content[0] = 0xFF; content[1] = 0xD8; content[2] = 0xFF;
        return CreateMockFormFile(content, contentType, "test.jpg");
    }

    // ─── PUT /foto-guia ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadFotoGuia_ValidJpeg_Returns200()
    {
        _mockService.Setup(s => s.SaveFotoGuiaAsync(It.IsAny<int>(), It.IsAny<byte[]>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var file = CreateMockFormFile(new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg", "test.jpg");

        var result = await _controller.UploadFotoGuia(TemplateId, PeriodoId, file);

        result.Should().BeOfType<OkObjectResult>();
        _mockService.Verify(s => s.SaveFotoGuiaAsync(PeriodoId, It.IsAny<byte[]>(), "image/jpeg"), Times.Once);
    }

    [Fact]
    public async Task UploadFotoGuia_OversizedFile_Returns413()
    {
        // 11 MB file — exceeds 10 MB limit
        var file = CreateOversizedMockFormFile(11, "image/jpeg");

        var result = await _controller.UploadFotoGuia(TemplateId, PeriodoId, file);

        var statusCode = (result as ObjectResult)!.StatusCode;
        statusCode.Should().Be(413);
    }

    [Fact]
    public async Task UploadFotoGuia_NonImagePdf_Returns415()
    {
        var file = CreateMockFormFile(new byte[] { 0x25, 0x50, 0x44, 0x46 }, "application/pdf", "test.pdf");

        var result = await _controller.UploadFotoGuia(TemplateId, PeriodoId, file);

        var statusCode = (result as ObjectResult)!.StatusCode;
        statusCode.Should().Be(415);
    }

    [Fact]
    public async Task UploadFotoGuia_PeriodoNotFound_Returns404()
    {
        var file = CreateMockFormFile(new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg", "test.jpg");
        _mockService.Setup(s => s.SaveFotoGuiaAsync(It.IsAny<int>(), It.IsAny<byte[]>(), It.IsAny<string>()))
            .ThrowsAsync(new KeyNotFoundException("Período no encontrado"));

        var result = await _controller.UploadFotoGuia(TemplateId, 9999, file);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ─── GET /foto-guia ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetFotoGuia_PhotoExists_ReturnsFileResult()
    {
        var fotoBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        _mockService.Setup(s => s.GetFotoGuiaAsync(PeriodoId))
            .ReturnsAsync((fotoBytes, "image/jpeg"));

        var result = await _controller.GetFotoGuia(TemplateId, PeriodoId);

        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
        fileResult.ContentType.Should().Be("image/jpeg");
        fileResult.FileContents.Should().BeEquivalentTo(fotoBytes);
    }

    [Fact]
    public async Task GetFotoGuia_NullPhoto_Returns404()
    {
        _mockService.Setup(s => s.GetFotoGuiaAsync(PeriodoId))
            .ReturnsAsync(((byte[]?)null, (string?)null));

        var result = await _controller.GetFotoGuia(TemplateId, PeriodoId);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetFotoGuia_PeriodoNotFound_Returns404()
    {
        _mockService.Setup(s => s.GetFotoGuiaAsync(9999))
            .ThrowsAsync(new KeyNotFoundException("Período no encontrado"));

        var result = await _controller.GetFotoGuia(TemplateId, 9999);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ─── PUT /foto-ocr ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadFotoOcr_ValidJpeg_Returns200()
    {
        _mockService.Setup(s => s.SaveFotoOcrAsync(It.IsAny<int>(), It.IsAny<byte[]>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var file = CreateMockFormFile(new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg", "ocr.jpg");

        var result = await _controller.UploadFotoOcr(TemplateId, PeriodoId, file);

        result.Should().BeOfType<OkObjectResult>();
        _mockService.Verify(s => s.SaveFotoOcrAsync(PeriodoId, It.IsAny<byte[]>(), "image/jpeg"), Times.Once);
    }

    [Fact]
    public async Task UploadFotoOcr_OversizedFile_Returns413()
    {
        // 6 MB file — exceeds 5 MB limit
        var file = CreateOversizedMockFormFile(6, "image/jpeg");

        var result = await _controller.UploadFotoOcr(TemplateId, PeriodoId, file);

        var statusCode = (result as ObjectResult)!.StatusCode;
        statusCode.Should().Be(413);
    }

    [Fact]
    public async Task UploadFotoOcr_SvgFile_Returns415()
    {
        var file = CreateMockFormFile(
            @"<?xml version=""1.0""?><svg xmlns=""http://www.w3.org/2000/svg""><rect/></svg>"u8.ToArray(),
            "image/svg+xml",
            "test.svg");

        var result = await _controller.UploadFotoOcr(TemplateId, PeriodoId, file);

        var statusCode = (result as ObjectResult)!.StatusCode;
        statusCode.Should().Be(415);
    }

    // ─── GET /foto-ocr ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetFotoOcr_PhotoExists_ReturnsFileResult()
    {
        var fotoBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        _mockService.Setup(s => s.GetFotoOcrAsync(PeriodoId))
            .ReturnsAsync((fotoBytes, "image/jpeg"));

        var result = await _controller.GetFotoOcr(TemplateId, PeriodoId);

        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
        fileResult.ContentType.Should().Be("image/jpeg");
        fileResult.FileContents.Should().BeEquivalentTo(fotoBytes);
    }

    [Fact]
    public async Task GetFotoOcr_NullPhoto_Returns404()
    {
        _mockService.Setup(s => s.GetFotoOcrAsync(PeriodoId))
            .ReturnsAsync(((byte[]?)null, (string?)null));

        var result = await _controller.GetFotoOcr(TemplateId, PeriodoId);

        result.Should().BeOfType<NotFoundObjectResult>();
    }
}
