using VendingManager.Core.Interfaces;

namespace VendingManager.Infrastructure.Services;

/// <summary>
/// Validates file content by reading the leading magic bytes (M-1).
/// Supported types: JPEG, PNG, PDF.
/// </summary>
public class FileContentValidator : IFileContentValidator
{
    // Magic byte signatures for allowed upload types.
    private static readonly byte[] JpegMagic = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] PngMagic  = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] PdfMagic  = { 0x25, 0x50, 0x44, 0x46 }; // %PDF

    /// <inheritdoc/>
    public void Validate(Stream content, string claimedExtension)
    {
        if (content == null) throw new ArgumentNullException(nameof(content));
        if (string.IsNullOrEmpty(claimedExtension)) throw new ArgumentException("Extension must not be empty.", nameof(claimedExtension));

        var ext = claimedExtension.ToLowerInvariant().TrimStart('.');

        byte[] expectedMagic = ext switch
        {
            "jpg" or "jpeg" => JpegMagic,
            "png"           => PngMagic,
            "pdf"           => PdfMagic,
            _ => throw new ArgumentException($"Unsupported file extension: .{ext}. Allowed: .jpg, .jpeg, .png, .pdf.")
        };

        // Read enough bytes to compare the longest signature (PNG = 8 bytes).
        int requiredLength = expectedMagic.Length;
        var buffer = new byte[requiredLength];
        int bytesRead = content.Read(buffer, 0, requiredLength);

        if (bytesRead < requiredLength || !buffer.Take(requiredLength).SequenceEqual(expectedMagic))
        {
            throw new ArgumentException(
                $"File content does not match the claimed type (.{ext}). Upload rejected.");
        }
    }
}
