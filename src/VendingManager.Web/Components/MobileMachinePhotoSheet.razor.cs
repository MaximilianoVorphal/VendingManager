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

    private IBrowserFile? _capturedFile;
    private string? _previewUrl;
    private bool _uploading;
    private string? _error;
    private bool _wasVisible;
    private bool _disposed;

    // ─── Lifecycle ──────────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        // Only reset state when the sheet OPENS (false → true transition)
        if (Visible && !_wasVisible)
        {
            _capturedFile = null;
            _previewUrl = null;
            _uploading = false;
            _error = null;
        }
        _wasVisible = Visible;
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

            // Reject files smaller than 12 bytes — too small for any valid image
            if (bytesRead < 12)
            {
                _error = "Archivo inválido o demasiado pequeño. Probá con otra foto.";
                StateHasChanged();
                return;
            }

            // ftyp box starts at offset 4: "ftyp" + brand (4 bytes)
            // HEIC/HEIF brands: "heic", "heix", "hevc", "hevx", "mif1", "mif3", "heim", "heis"
            // AVIF brands: "avif", "avis"
            if (header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p')
            {
                var brand = System.Text.Encoding.ASCII.GetString(header, 8, 4);
                var heicBrands = new[] { "heic", "heix", "hevc", "hevx", "mif1", "mif3", "heim", "heis" };
                var avifBrands = new[] { "avif", "avis" };

                if (heicBrands.Contains(brand))
                {
                    _error = "Formato HEIC no soportado. Convertila a JPG o PNG.";
                    StateHasChanged();
                    return;
                }

                if (avifBrands.Contains(brand))
                {
                    _error = "Formato AVIF no soportado. Convertila a JPG o PNG.";
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
        StateHasChanged();

        try
        {
            await OnPhotoAccepted.InvokeAsync(_capturedFile);
            // On success, the parent closes the sheet and resets state
        }
        catch (Exception)
        {
            throw; // let the renderer handle unhandled errors
        }
        finally
        {
            _uploading = false; // ALWAYS reset, so the user can retry on error
            StateHasChanged();
        }
    }

    private async Task HandleCancel()
    {
        await OnClose.InvokeAsync();
    }

    /// <summary>
    /// Called by the parent page to set an error message inline in the sheet.
    /// </summary>
    public async Task SetErrorAsync(string? error)
    {
        _error = error;
        await InvokeAsync(StateHasChanged);
    }

    // ─── IDisposable ────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _previewUrl = null; // Release the data-URL
    }
}
