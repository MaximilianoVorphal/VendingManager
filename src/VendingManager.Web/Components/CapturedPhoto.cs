namespace VendingManager.Web.Components;

/// <summary>
/// Holds a captured photo's bytes, content type, and original file name.
/// Photo sheet components read the file once for validation/preview and pass
/// this DTO to the parent page — this avoids a second OpenReadStream call on
/// the underlying IBrowserFile, which Blazor disallows.
/// </summary>
public sealed record CapturedPhoto(byte[] Bytes, string ContentType, string FileName);
