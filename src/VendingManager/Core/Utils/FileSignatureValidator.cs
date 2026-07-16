using System.IO.Compression;

namespace VendingManager.Core.Utils;

/// <summary>
/// Formats <see cref="FileSignatureValidator"/> can check for. Combine with
/// bitwise OR to allow more than one format at a call site (e.g.
/// <c>Jpeg | Png | Pdf</c>).
/// </summary>
[Flags]
public enum AllowedFormats
{
    Jpeg = 1,
    Png = 2,
    Pdf = 4,
    Xlsx = 8
}

/// <summary>
/// Validates uploaded file content by magic-byte signature, independent of the
/// declared file extension. Pure, no dependencies — safe to call from any layer.
/// Throws <see cref="ArgumentException"/> on mismatch, matching the existing
/// upload-validation convention used by <c>CompraService</c>/<c>TransferenciaService</c>.
/// </summary>
public static class FileSignatureValidator
{
    private static readonly byte[] JpegSignature = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] PdfSignature = { 0x25, 0x50, 0x44, 0x46, 0x2D }; // "%PDF-"

    private static readonly byte[][] ZipSignatures =
    {
        new byte[] { 0x50, 0x4B, 0x03, 0x04 },
        new byte[] { 0x50, 0x4B, 0x05, 0x06 },
        new byte[] { 0x50, 0x4B, 0x07, 0x08 }
    };

    /// <summary>
    /// Validates that <paramref name="content"/>'s leading bytes (and, for XLSX,
    /// its ZIP structure) match at least one of the allowed <paramref name="formats"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when content is null/empty or does not match any allowed format.
    /// </exception>
    public static void Validate(byte[]? content, AllowedFormats formats)
    {
        if (content == null || content.Length == 0)
            throw new ArgumentException("El archivo está vacío o no se pudo leer.");

        if (formats.HasFlag(AllowedFormats.Jpeg) && StartsWith(content, JpegSignature))
            return;

        if (formats.HasFlag(AllowedFormats.Png) && StartsWith(content, PngSignature))
            return;

        if (formats.HasFlag(AllowedFormats.Pdf) && StartsWith(content, PdfSignature))
            return;

        if (formats.HasFlag(AllowedFormats.Xlsx) && IsValidXlsx(content))
            return;

        throw new ArgumentException("El contenido del archivo no corresponde a un formato permitido.");
    }

    private static bool StartsWith(byte[] content, byte[] signature)
    {
        if (content.Length < signature.Length)
            return false;

        for (int i = 0; i < signature.Length; i++)
        {
            if (content[i] != signature[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// XLSX is an Office Open XML ZIP container. A raw ZIP-signature check alone
    /// would accept any renamed ZIP, so this also opens the archive and confirms
    /// a <c>[Content_Types].xml</c> entry exists — no sheet-internal validation.
    /// </summary>
    private static bool IsValidXlsx(byte[] content)
    {
        bool hasZipSignature = false;
        foreach (var signature in ZipSignatures)
        {
            if (StartsWith(content, signature))
            {
                hasZipSignature = true;
                break;
            }
        }

        if (!hasZipSignature)
            return false;

        try
        {
            using var stream = new MemoryStream(content);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName == "[Content_Types].xml")
                    return true;
            }

            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
