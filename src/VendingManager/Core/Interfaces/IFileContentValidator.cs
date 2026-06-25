namespace VendingManager.Core.Interfaces;

/// <summary>
/// Validates uploaded file content using magic-byte inspection (M-1).
/// Rejects files whose leading bytes do not match the claimed extension/MIME type.
/// </summary>
public interface IFileContentValidator
{
    /// <summary>
    /// Validates that <paramref name="content"/> starts with the magic bytes for <paramref name="claimedExtension"/>.
    /// </summary>
    /// <param name="content">A readable stream positioned at the beginning of the file.
    /// NOTE: this method advances the stream position by the magic-byte header length and does NOT
    /// rewind it. Callers that reuse the stream afterwards must reset its position (e.g. Seek(0)).</param>
    /// <param name="claimedExtension">The file extension including the leading dot, e.g. ".jpg", ".png", ".pdf".</param>
    /// <exception cref="ArgumentException">Thrown when the file content does not match the claimed type.</exception>
    void Validate(Stream content, string claimedExtension);
}
