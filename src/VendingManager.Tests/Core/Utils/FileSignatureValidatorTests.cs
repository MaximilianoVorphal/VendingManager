using System.IO.Compression;
using System.Text;
using VendingManager.Core.Utils;
using Xunit;

namespace VendingManager.Tests.Core.Utils;

public class FileSignatureValidatorTests
{
    // ─── Valid content accepted per format (REQ-UPLOAD-01) ─────────────────

    [Fact]
    public void Validate_ValidJpeg_DoesNotThrow()
    {
        byte[] content = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };

        var act = () => FileSignatureValidator.Validate(content, AllowedFormats.Jpeg);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_ValidPng_DoesNotThrow()
    {
        byte[] content = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };

        var act = () => FileSignatureValidator.Validate(content, AllowedFormats.Png);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_ValidPdf_DoesNotThrow()
    {
        byte[] content = Encoding.ASCII.GetBytes("%PDF-1.7\n%content");

        var act = () => FileSignatureValidator.Validate(content, AllowedFormats.Pdf);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_ValidXlsx_DoesNotThrow()
    {
        byte[] content = BuildMinimalXlsx();

        var act = () => FileSignatureValidator.Validate(content, AllowedFormats.Xlsx);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_MultipleAllowedFormats_AcceptsAnyMatchingOne()
    {
        byte[] pngContent = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        var act = () => FileSignatureValidator.Validate(pngContent, AllowedFormats.Jpeg | AllowedFormats.Png | AllowedFormats.Pdf);

        act.Should().NotThrow();
    }

    // ─── Spoofed extension / mismatched content rejected (REQ-UPLOAD-01) ───

    [Fact]
    public void Validate_TextRenamedAsJpeg_ThrowsArgumentException()
    {
        byte[] content = Encoding.ASCII.GetBytes("this is not a jpeg, just plain text");

        var act = () => FileSignatureValidator.Validate(content, AllowedFormats.Jpeg);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_TextRenamedAsPdf_ThrowsArgumentException()
    {
        byte[] content = Encoding.ASCII.GetBytes("<html>not a real pdf</html>");

        var act = () => FileSignatureValidator.Validate(content, AllowedFormats.Pdf);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_PlainZipWithoutContentTypesEntry_RenamedAsXlsx_ThrowsArgumentException()
    {
        // A generic ZIP archive (no [Content_Types].xml) renamed to .xlsx must be
        // rejected — the raw ZIP magic-byte check alone would incorrectly accept it.
        byte[] content = BuildPlainZipWithoutContentTypes();

        var act = () => FileSignatureValidator.Validate(content, AllowedFormats.Xlsx);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_CorruptZipSignatureButNotValidZip_ThrowsArgumentException()
    {
        // Has the ZIP local-header magic bytes but is not a real archive.
        byte[] content = { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x01, 0x02 };

        var act = () => FileSignatureValidator.Validate(content, AllowedFormats.Xlsx);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_EmptyContent_ThrowsArgumentException()
    {
        var act = () => FileSignatureValidator.Validate(Array.Empty<byte>(), AllowedFormats.Jpeg);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_NullContent_ThrowsArgumentException()
    {
        var act = () => FileSignatureValidator.Validate(null, AllowedFormats.Jpeg);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_FormatNotInAllowedSet_ThrowsArgumentException()
    {
        // Genuine JPEG bytes but only PDF is allowed at this call site.
        byte[] content = { 0xFF, 0xD8, 0xFF, 0xE0 };

        var act = () => FileSignatureValidator.Validate(content, AllowedFormats.Pdf);

        act.Should().Throw<ArgumentException>();
    }

    private static byte[] BuildMinimalXlsx()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("[Content_Types].xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"></Types>");
        }

        return ms.ToArray();
    }

    private static byte[] BuildPlainZipWithoutContentTypes()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("readme.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("just a regular zip, not an office document");
        }

        return ms.ToArray();
    }
}
