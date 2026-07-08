// =========================================================================
// MobileMachinePhotoSheet.razor.cs — Code-behind for the photo bottom sheet
// =========================================================================
// Presentation-only bottom sheet for proof-of-load photo. Does NOT call the
// API directly — the parent (RecargaMovil) handles the foto-guía PUT in
// HandlePhotoAccepted.
// =========================================================================

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace VendingManager.Web.Components;

public partial class MobileMachinePhotoSheet : ComponentBase, IDisposable
{
    // ─── Parameters ─────────────────────────────────────────────────────

    /// <summary>Whether the bottom sheet is visible.</summary>
    [Parameter, EditorRequired] public bool Visible { get; set; }

    /// <summary>Called when the sheet is dismissed (Cancel or backdrop tap).</summary>
    [Parameter, EditorRequired] public EventCallback OnClose { get; set; }

    /// <summary>Called with the captured file when the user taps "Subir y finalizar".</summary>
    [Parameter, EditorRequired] public EventCallback<IBrowserFile> OnPhotoAccepted { get; set; }

    /// <summary>Sheet title. Default: "Foto de la máquina".</summary>
    [Parameter] public string Title { get; set; } = "Foto de la máquina";

    /// <summary>Optional subtitle shown below the title.</summary>
    [Parameter] public string? Subtitle { get; set; }

    /// <summary>Optional machine label (e.g. "Máquina …2400").</summary>
    [Parameter] public string? MachineLabel { get; set; }

    // ─── Internal State ─────────────────────────────────────────────────

    private InputFile? _inputFile;
    private IBrowserFile? _capturedFile;
    private string? _previewUrl;
    private bool _uploading;
    private string? _error;
    private bool _disposed;

    // ─── Lifecycle ──────────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        // Reset state when the sheet opens (Visible transitions true)
        if (Visible)
        {
            _capturedFile = null;
            _previewUrl = null;
            _uploading = false;
            _error = null;
        }
    }

    // ─── File Selection ─────────────────────────────────────────────────

    private async Task HandleFileSelected(InputFileChangeEventArgs e)
    {
        _error = null;

        var file = e.GetMultipleFiles(1).FirstOrDefault();
        if (file == null) return;

        // Validate content type
        var contentType = file.ContentType.ToLowerInvariant();
        var allowed = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };

        if (!allowed.Contains(contentType))
        {
            _error = "Formato no soportado. Usá JPG, PNG, GIF o WebP.";
            StateHasChanged();
            return;
        }

        // HEIC detection via magic bytes — iOS may report HEIC as image/jpeg
        try
        {
            const long maxSize = 10 * 1024 * 1024; // 10 MB
            await using var stream = file.OpenReadStream(maxSize);
            var header = new byte[12];
            var bytesRead = await stream.ReadAsync(header.AsMemory(0, 12));

            // ftyp box starts at offset 4: "ftyp" + brand (4 bytes)
            // HEIC brands: "heic", "heix", "hevc", "hevx"
            if (bytesRead >= 12 && header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p')
            {
                var isHeic = (header[8] == 'h' && header[9] == 'e' && (header[10] == 'i' || header[10] == 'x') && header[11] == 'c')
                          || (header[8] == 'm' && header[9] == 'i' && header[10] == 'f' && header[11] == '3')
                          || (header[8] == 'h' && header[9] == 'e' && header[10] == 'v' && (header[11] == 'c' || header[11] == 'x'));

                if (isHeic)
                {
                    _error = "Formato HEIC no soportado. Convertila a JPG o PNG.";
                    StateHasChanged();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _error = $"Error al leer el archivo: {ex.Message}";
            StateHasChanged();
            return;
        }

        // Read file into data-URL for preview
        try
        {
            const long maxSize = 10 * 1024 * 1024;
            await using var readStream = file.OpenReadStream(maxSize);
            using var ms = new MemoryStream();
            await readStream.CopyToAsync(ms);
            var bytes = ms.ToArray();

            _capturedFile = file;
            _previewUrl = $"data:{file.ContentType};base64,{Convert.ToBase64String(bytes)}";
        }
        catch (Exception ex)
        {
            _error = $"Error al cargar la imagen: {ex.Message}";
        }

        StateHasChanged();
    }

    /// <summary>Clear the captured photo so the user can retake it.</summary>
    private void ClearPhoto()
    {
        _capturedFile = null;
        _previewUrl = null;
        _error = null;
        StateHasChanged();
    }

    // ─── Submit / Cancel ────────────────────────────────────────────────

    private async Task HandleSubmit()
    {
        if (_capturedFile == null) return;

        _uploading = true;
        _error = null;
        StateHasChanged();

        try
        {
            await OnPhotoAccepted.InvokeAsync(_capturedFile);
        }
        catch (Exception ex)
        {
            _error = $"Error al enviar la foto: {ex.Message}";
            _uploading = false;
            StateHasChanged();
        }
        // On success, the parent (RecargaMovil) closes the sheet and resets state
    }

    private async Task HandleCancel()
    {
        await OnClose.InvokeAsync();
    }

    // ─── IDisposable ────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _previewUrl = null; // Release the data-URL
    }
}
