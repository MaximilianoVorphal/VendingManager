using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace VendingManager.Web.Components;

/// <summary>
/// Inline aside panel for viewing and interacting with a foto guía image.
/// Provides pan/zoom via JS interop (foto-guia.js) and upload buttons.
///
/// JS module functions (foto-guia.js): initPanZoom(el), zoomIn(), zoomOut(),
/// reset(), label() — all exported as module-level functions. No controller
/// object pattern (bUnit cannot mock IJSObjectReference return values).
/// </summary>
public partial class FotoGuiaPanel : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = null!;

    /// <summary>Machine ID shown in the subtitle.</summary>
    [Parameter] public string MaquinaId { get; set; } = "";

    /// <summary>Data URI or blob URL of the foto guía image. Null/empty shows empty-state.</summary>
    [Parameter] public string? FotoGuiaUrl { get; set; }

    /// <summary>Fired when the close × button is clicked.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>Fired when a camera or file upload produces bytes.</summary>
    [Parameter] public EventCallback<byte[]> OnFotoUpload { get; set; }

    private ElementReference _bodyRef;
    private IJSObjectReference? _jsModule;
    private string _zoomLabel = "100%";
    private bool _disposed;
    private string? _lastPanZoomUrl;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed) return;

        if (firstRender)
        {
            try
            {
                _jsModule = await JS.InvokeAsync<IJSObjectReference>("import", "./js/foto-guia.js");
            }
            catch
            {
                // JS interop failures are non-fatal — panel still renders with defaults
            }
        }

        // Initialize pan/zoom whenever a photo URL becomes available or changes,
        // regardless of whether this is the first render or a re-render.
        // This handles: first render with photo, async photo load arriving after
        // first render, user uploading a photo from within the panel, and user
        // replacing an existing photo with a new one.
        if (_jsModule != null && !string.IsNullOrEmpty(FotoGuiaUrl) && FotoGuiaUrl != _lastPanZoomUrl)
        {
            try
            {
                await _jsModule.InvokeVoidAsync("initPanZoom", _bodyRef);
                _zoomLabel = await _jsModule.InvokeAsync<string>("label");
                _lastPanZoomUrl = FotoGuiaUrl;
                StateHasChanged();
            }
            catch
            {
                // JS interop failures are non-fatal
            }
        }
    }

    private async Task ZoomIn()
    {
        if (_jsModule != null)
        {
            await _jsModule.InvokeVoidAsync("zoomIn");
            _zoomLabel = await _jsModule.InvokeAsync<string>("label");
            StateHasChanged();
        }
    }

    private async Task ZoomOut()
    {
        if (_jsModule != null)
        {
            await _jsModule.InvokeVoidAsync("zoomOut");
            _zoomLabel = await _jsModule.InvokeAsync<string>("label");
            StateHasChanged();
        }
    }

    private async Task ResetZoom()
    {
        if (_jsModule != null)
        {
            await _jsModule.InvokeVoidAsync("reset");
            _zoomLabel = await _jsModule.InvokeAsync<string>("label");
            StateHasChanged();
        }
    }

    private async Task UploadFromCamera(InputFileChangeEventArgs e)
    {
        await HandleUpload(e);
    }

    private async Task UploadFromFile(InputFileChangeEventArgs e)
    {
        await HandleUpload(e);
    }

    private async Task HandleUpload(InputFileChangeEventArgs e)
    {
        if (_disposed) return;
        foreach (var file in e.GetMultipleFiles(1))
        {
            await using var stream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
            using var ms = new System.IO.MemoryStream();
            await stream.CopyToAsync(ms);
            await OnFotoUpload.InvokeAsync(ms.ToArray());
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_jsModule != null)
        {
            try { await _jsModule.DisposeAsync(); } catch { }
        }
    }
}
