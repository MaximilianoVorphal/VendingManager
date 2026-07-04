using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace VendingManager.Web.Components;

/// <summary>
/// Inline aside panel for viewing and interacting with a foto guía image.
/// Provides pan/zoom via JS interop (foto-guia.js) and upload buttons.
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
    private IJSObjectReference? _panZoomCtrl;
    private string _zoomLabel = "100%";
    private bool _disposed;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed) return;

        if (firstRender)
        {
            try
            {
                _jsModule = await JS.InvokeAsync<IJSObjectReference>("import", "./js/foto-guia.js");

                if (_jsModule != null && !string.IsNullOrEmpty(FotoGuiaUrl))
                {
                    _panZoomCtrl = await _jsModule.InvokeAsync<IJSObjectReference>("initPanZoom", _bodyRef);

                    if (_panZoomCtrl != null)
                    {
                        _zoomLabel = await _panZoomCtrl.InvokeAsync<string>("label");
                        StateHasChanged();
                    }
                }
            }
            catch
            {
                // JS interop failures are non-fatal — panel still renders with defaults
            }
        }
    }

    private async Task ZoomIn()
    {
        if (_panZoomCtrl != null)
        {
            await _panZoomCtrl.InvokeVoidAsync("zoomIn");
            _zoomLabel = await _panZoomCtrl.InvokeAsync<string>("label");
            StateHasChanged();
        }
    }

    private async Task ZoomOut()
    {
        if (_panZoomCtrl != null)
        {
            await _panZoomCtrl.InvokeVoidAsync("zoomOut");
            _zoomLabel = await _panZoomCtrl.InvokeAsync<string>("label");
            StateHasChanged();
        }
    }

    private async Task ResetZoom()
    {
        if (_panZoomCtrl != null)
        {
            await _panZoomCtrl.InvokeVoidAsync("reset");
            _zoomLabel = await _panZoomCtrl.InvokeAsync<string>("label");
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
        if (_panZoomCtrl != null)
        {
            try { await _panZoomCtrl.DisposeAsync(); } catch { }
        }
        if (_jsModule != null)
        {
            try { await _jsModule.DisposeAsync(); } catch { }
        }
    }
}
