// =========================================================================
// MobileRecargaSaveSheet.razor.cs — Code-behind for save bottom sheet
// =========================================================================
// Bottom sheet for per-machine save. Presentation-only: captures a photo
// and passes it back to the parent via OnSaveAndOverview or
// OnSaveAndPickAnother callbacks. The parent (RecargaMovil) handles the
// slot-batch then foto-guia API sequence.
//
// Uses the same blob: URL preview pattern as MobileMachinePhotoSheet to
// avoid holding full base64 strings in the DOM.
// =========================================================================

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace VendingManager.Web.Components;

public partial class MobileRecargaSaveSheet : ComponentBase, IDisposable, IAsyncDisposable
{
    // ─── Dependencies ───────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = null!;

    // ─── Parameters ─────────────────────────────────────────────────────

    /// <summary>Whether the bottom sheet is visible.</summary>
    [Parameter, EditorRequired] public bool Visible { get; set; }

    /// <summary>Called when the sheet is dismissed (Cancel or backdrop tap).</summary>
    [Parameter, EditorRequired] public EventCallback OnClose { get; set; }

    /// <summary>
    /// Called with the captured file when the user taps "Guardar carga".
    /// Parent should call slot-batch then foto-guia, then navigate to Resumen.
    /// </summary>
    [Parameter, EditorRequired] public EventCallback<IBrowserFile> OnSaveAndOverview { get; set; }

    /// <summary>
    /// Called with the captured file when the user taps "Guardar y cargar otra máquina".
    /// Parent should call slot-batch then foto-guia, then navigate to PickMachine.
    /// </summary>
    [Parameter, EditorRequired] public EventCallback<IBrowserFile> OnSaveAndPickAnother { get; set; }

    /// <summary>Machine label (e.g. "…2400").</summary>
    [Parameter] public string MachineLabel { get; set; } = string.Empty;

    /// <summary>Summary text for loaded units (e.g. "12 u. cargadas").</summary>
    [Parameter] public string? UnitSummary { get; set; }

    /// <summary>Summary text for slots (e.g. "5 productos").</summary>
    [Parameter] public string? SlotSummary { get; set; }

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
            // Revoke any existing blob URL from a previous session
            if (_previewUrl != null && _previewUrl.StartsWith("blob:", StringComparison.Ordinal))
            {
                var oldUrl = _previewUrl;
                _ = InvokeAsync(() => RevokeBlobUrlAsync(oldUrl));
            }

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

        // Read file into blob URL for preview (avoids storing large base64 in the DOM)
        try
        {
            const long maxSize = 10 * 1024 * 1024;
            await using var readStream = file.OpenReadStream(maxSize);
            using var ms = new MemoryStream();
            await readStream.CopyToAsync(ms);
            var bytes = ms.ToArray();

            // Revoke previous blob URL if reopening with a new photo before clearing
            if (_previewUrl != null && _previewUrl.StartsWith("blob:", StringComparison.Ordinal))
            {
                await RevokeBlobUrlAsync(_previewUrl);
            }

            _capturedFile = file;
            _previewUrl = await JS.InvokeAsync<string>(
                "vmCreateBlobUrl",
                Convert.ToBase64String(bytes),
                file.ContentType);
        }
        catch (Exception ex)
        {
            _error = $"Error al cargar la imagen: {ex.Message}";
        }

        StateHasChanged();
    }

    /// <summary>Clear the captured photo so the user can retake it.</summary>
    private async Task ClearPhoto()
    {
        if (_previewUrl != null && _previewUrl.StartsWith("blob:", StringComparison.Ordinal))
        {
            await RevokeBlobUrlAsync(_previewUrl);
        }

        _capturedFile = null;
        _previewUrl = null;
        _error = null;
        StateHasChanged();
    }

    // ─── Blob URL Lifecycle ─────────────────────────────────────────────

    /// <summary>
    /// Revoke a blob: URL via JS interop to free browser memory.
    /// Safe to call even during disposal (errors are silently caught).
    /// </summary>
    private async Task RevokeBlobUrlAsync(string url)
    {
        try
        {
            await JS.InvokeVoidAsync("vmRevokeBlobUrl", url);
        }
        catch
        {
            // JS interop may fail during disposal (page unloading or detached context)
        }
    }

    // ─── Save Buttons ───────────────────────────────────────────────────

    private async Task HandleSaveAndOverview()
    {
        if (_capturedFile == null) return;

        _uploading = true;
        StateHasChanged();

        try
        {
            await OnSaveAndOverview.InvokeAsync(_capturedFile);
        }
        finally
        {
            _uploading = false;
            StateHasChanged();
        }
    }

    private async Task HandleSaveAndPickAnother()
    {
        if (_capturedFile == null) return;

        _uploading = true;
        StateHasChanged();

        try
        {
            await OnSaveAndPickAnother.InvokeAsync(_capturedFile);
        }
        finally
        {
            _uploading = false;
            StateHasChanged();
        }
    }

    // ─── Cancel ─────────────────────────────────────────────────────────

    private async Task HandleCancel()
    {
        await OnClose.InvokeAsync();
    }

    // ─── Public API ─────────────────────────────────────────────────────

    /// <summary>
    /// Called by the parent page to set an error message inline in the sheet.
    /// </summary>
    public async Task SetErrorAsync(string? error)
    {
        _error = error;
        await InvokeAsync(StateHasChanged);
    }

    // ─── IDisposable / IAsyncDisposable ─────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_previewUrl != null && _previewUrl.StartsWith("blob:", StringComparison.Ordinal))
        {
            try
            {
                await JS.InvokeVoidAsync("vmRevokeBlobUrl", _previewUrl);
            }
            catch
            {
                // JS interop may fail during disposal (page unloading or detached context)
            }
        }

        _previewUrl = null;
    }

    void IDisposable.Dispose()
    {
        // Cleanup handled by DisposeAsync — this guard prevents double-dispose
        // without duplicating the async revocation logic.
    }
}
