// =========================================================================
// ConciliacionMovil.razor.cs — Code-behind for the mobile Conciliación page
// =========================================================================
// Faithful port of templates/conciliacion-movil/ConciliacionMovil.dc.html.
// Sibling of RecargaMovil: a view state-machine driven from the field.
//
// Flow: Lista de rendiciones → Resumen (transferencias/compras/gastos, saldo
//       en vivo) → Agregar transferencia (bottom sheet, con foto) → Agregar
//       boleta (tab "De Compras" para vincular · tab "Subir nueva" con OCR
//       Gemini que la registra en Compras) → Comprobante (verificar/rechazar).
// El cierre (devolución) NO está en el móvil — eso queda en escritorio.
//
// Backend reuse (todos existentes):
//   GET  api/contabilidad/periodos                         → lista rendiciones
//   GET  api/contabilidad/periodos/{id}                    → detalle rendición
//   POST api/contabilidad/cuadre                           → nueva rendición (período + 1ª transferencia 1:1)
//   POST api/contabilidad/transferencia-con-movimiento     → transferencia adicional en rendición existente
//   POST api/contabilidad/transferencia/{id}/comprobante   → subir foto de transferencia
//   GET  api/rendicion/compras-no-vinculadas               → pool "De Compras"
//   POST api/contabilidad/transferencia/{tid}/vincular-compra/{cid} → vincular boleta del pool
//   POST api/compras/upload-factura                        → OCR (Gemini)
//   POST api/contabilidad/compra-vinculada                 → registrar boleta nueva + vincular
//   POST api/contabilidad/{transferencia|compra}/{id}/verificar / desverificar
// =========================================================================

using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;
using VendingManager.Web.Layout;

namespace VendingManager.Web.Pages;

public partial class ConciliacionMovil : ComponentBase, IDisposable
{
    // ─── View state machine ──────────────────────────────────────────────
    private enum View { List, Overview, AddBoleta, Comprobante }

    // Navbar visible only on the List; every detail/work view collapses it to
    // reclaim its ~72px of vertical space on mobile (see MainLayout.CollapseNavbar).
    private View _viewState = View.List;
    private View _view
    {
        get => _viewState;
        set
        {
            if (_viewState == value) return;
            _viewState = value;
            if (value == View.List) Layout?.ExpandNavbar();
            else Layout?.CollapseNavbar();
        }
    }

    [CascadingParameter] public MainLayout? Layout { get; set; }

    private enum BoletaKind { Compra, Gasto }
    private enum AddTab { Pool, Nueva }
    private enum OcrState { Idle, Reading, Done }

    // ─── Item kinds (for Resumen rows + selected comprobante) ─────────────
    private enum ItemKind { Transferencia, Compra, Gasto }

    // ─── Dependencies ────────────────────────────────────────────────────
    [Inject] private HttpClient Http { get; set; } = null!;
    [Inject] private ILogger<ConciliacionMovil> Logger { get; set; } = null!;

    // ─── Data state ──────────────────────────────────────────────────────
    private List<AccountingPeriodDto>? _rendiciones;
    private AccountingPeriodFullDto? _periodo;

    private bool _loading;
    private string? _error;

    // ─── Toast ───────────────────────────────────────────────────────────
    private string? _toastMessage;
    private string? _toastIcon;
    private Timer? _toastTimer;

    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    // ─── Add-transferencia sheet ─────────────────────────────────────────
    private bool _transfSheetVisible;
    private bool _creatingNewRendicion;   // true = "Nueva rendición" (cuadre); false = transferencia adicional
    private string _transfBeneficiario = "";
    private DateTime _transfFecha = DateTime.Today;
    private string _transfMontoText = "";
    private decimal _transfMonto;
    private IBrowserFile? _transfFoto;
    private string? _transfError;
    private bool _transfSaving;

    // ─── Add-boleta view ─────────────────────────────────────────────────
    private BoletaKind _boletaKind = BoletaKind.Compra;
    private AddTab _addTab = AddTab.Pool;
    private string? _boletaError;

    // Pool tab
    private List<CompraDto> _pool = new();
    private string _poolSearch = "";
    private bool _poolLoading;

    // Nueva tab (OCR)
    private OcrState _ocrState = OcrState.Idle;
    private string _ocrProveedor = "";
    private string _ocrDoc = "";
    private DateTime _ocrFecha = DateTime.Today;
    private string _ocrSubcat = "GENERALES";
    private byte[]? _ocrFotoBytes;
    private string? _ocrFotoContentType;
    private string? _ocrFotoName;
    private readonly List<OcrLine> _ocrItems = new();
    private bool _ocrSaving;

    private sealed class OcrLine
    {
        public string Producto { get; set; } = "";
        public int Cantidad { get; set; } = 1;
        public decimal CostoUnitario { get; set; }
        public int? ProductoIdMatch { get; set; }
        public string? Ean { get; set; }
        public string? Sku { get; set; }
        public int? PackSize { get; set; }
        public decimal Subtotal => Cantidad * CostoUnitario;
    }

    // manual draft line for the OCR editor
    private string _ocrDraftDesc = "";
    private int _ocrDraftCant = 1;
    private decimal _ocrDraftPrecio;

    // ─── Selected comprobante ────────────────────────────────────────────
    private ItemKind _selKind;
    private int _selId;
    private string? _comprobanteError;
    private bool _verifBusy;

    // =====================================================================
    // LIFECYCLE
    // =====================================================================
    protected override async Task OnInitializedAsync()
    {
        _view = View.List;
        await LoadRendicionesAsync();
    }

    // =====================================================================
    // HTTP — rendiciones
    // =====================================================================
    private async Task LoadRendicionesAsync()
    {
        _loading = true;
        _error = null;
        StateHasChanged();
        try
        {
            _rendiciones = await Http.GetFromJsonAsync<List<AccountingPeriodDto>>(
                "api/contabilidad/periodos", _cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load rendiciones");
            _error = "Error al cargar las rendiciones";
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private async Task LoadRendicionDetailAsync(int id)
    {
        _loading = true;
        _error = null;
        StateHasChanged();
        try
        {
            _periodo = await Http.GetFromJsonAsync<AccountingPeriodFullDto>(
                $"api/contabilidad/periodos/{id}", _cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load rendición {Id}", id);
            _error = "Error al cargar la rendición";
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    // =====================================================================
    // NAVIGATION
    // =====================================================================
    private async Task GoToList()
    {
        _view = View.List;
        _periodo = null;
        _error = null;
        await LoadRendicionesAsync();
    }

    private async Task OpenRendicion(int id)
    {
        await LoadRendicionDetailAsync(id);
        if (_periodo is not null)
        {
            _view = View.Overview;
            StateHasChanged();
        }
    }

    private void GoToOverview()
    {
        _view = View.Overview;
        _error = null;
        StateHasChanged();
    }

    // =====================================================================
    // ADD TRANSFERENCIA (bottom sheet)
    // =====================================================================
    private void OpenNewRendicionSheet()
    {
        _creatingNewRendicion = true;
        ResetTransfSheet();
        _transfSheetVisible = true;
        StateHasChanged();
    }

    private void OpenAddTransferenciaSheet()
    {
        _creatingNewRendicion = false;
        ResetTransfSheet();
        // pre-fill worker from the current rendición
        _transfBeneficiario = _periodo?.Trabajador ?? "";
        _transfSheetVisible = true;
        StateHasChanged();
    }

    private void ResetTransfSheet()
    {
        _transfBeneficiario = "";
        _transfFecha = DateTime.Today;
        _transfMontoText = "";
        _transfMonto = 0;
        _transfFoto = null;
        _transfError = null;
        _transfSaving = false;
    }

    private void CloseTransfSheet()
    {
        _transfSheetVisible = false;
        StateHasChanged();
    }

    private void OnTransfMontoChanged(ChangeEventArgs e)
    {
        _transfMontoText = e.Value?.ToString() ?? "";
        _transfMonto = ParseMonto(_transfMontoText);
    }

    private void OnTransfFotoSelected(InputFileChangeEventArgs e)
    {
        _transfFoto = e.FileCount > 0 ? e.File : null;
    }

    private async Task SaveTransferencia()
    {
        if (_transfMonto <= 0)
        {
            _transfError = "El monto debe ser mayor a cero.";
            return;
        }

        _transfError = null;
        _transfSaving = true;
        StateHasChanged();

        try
        {
            if (_creatingNewRendicion)
            {
                var req = new CrearCuadreRequest
                {
                    Trabajador = _transfBeneficiario,
                    Monto = _transfMonto,
                    Fecha = _transfFecha
                };
                var resp = await Http.PostAsJsonAsync("api/contabilidad/cuadre", req, _cts.Token);
                if (!await GuardOk(resp, msg => _transfError = msg)) return;

                var created = await resp.Content.ReadFromJsonAsync<CuadreCreadoDto>(_cts.Token);
                if (created is not null)
                {
                    await UploadTransfFotoIfAny(created.TransferenciaId);
                    _transfSheetVisible = false;
                    await LoadRendicionDetailAsync(created.PeriodoId);
                    _view = View.Overview;
                    ShowToast("Rendición creada");
                }
            }
            else
            {
                if (_periodo is null) return;
                var rendicionId = _periodo.Transferencias.FirstOrDefault()?.RendicionId ?? 0;
                var req = new TransferenciaConMovimientoRequest
                {
                    RendicionId = rendicionId,
                    PeriodoId = _periodo.Id,
                    Trabajador = string.IsNullOrWhiteSpace(_transfBeneficiario)
                        ? (_periodo.Trabajador ?? "")
                        : _transfBeneficiario,
                    Fecha = _transfFecha,
                    Monto = _transfMonto
                };
                var resp = await Http.PostAsJsonAsync(
                    "api/contabilidad/transferencia-con-movimiento", req, _cts.Token);
                if (!await GuardOk(resp, msg => _transfError = msg)) return;

                var created = await resp.Content.ReadFromJsonAsync<TransferenciaDto>(_cts.Token);
                if (created is not null)
                {
                    await UploadTransfFotoIfAny(created.Id);
                }
                _transfSheetVisible = false;
                await LoadRendicionDetailAsync(_periodo.Id);
                ShowToast("Transferencia agregada");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to save transferencia");
            _transfError = "Error de red al guardar la transferencia.";
        }
        finally
        {
            _transfSaving = false;
            StateHasChanged();
        }
    }

    private async Task UploadTransfFotoIfAny(int transferenciaId)
    {
        if (_transfFoto is null) return;
        try
        {
            using var content = new MultipartFormDataContent();
            var stream = _transfFoto.OpenReadStream(10 * 1024 * 1024, _cts.Token);
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(_transfFoto.ContentType);
            content.Add(fileContent, "file", _transfFoto.Name);
            await Http.PostAsync(
                $"api/contabilidad/transferencia/{transferenciaId}/comprobante", content, _cts.Token);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Foto upload failed for transferencia {Id} — non-critical", transferenciaId);
        }
    }

    // =====================================================================
    // ADD BOLETA (Compra / Gasto)
    // =====================================================================
    private async Task OpenAddBoleta(BoletaKind kind)
    {
        _boletaKind = kind;
        _addTab = AddTab.Pool;
        _boletaError = null;
        _poolSearch = "";
        ResetOcr();
        _view = View.AddBoleta;
        StateHasChanged();
        await LoadPoolAsync();
    }

    private async Task SetTab(AddTab tab)
    {
        _addTab = tab;
        _boletaError = null;
        StateHasChanged();
        if (tab == AddTab.Pool) await LoadPoolAsync();
    }

    private async Task LoadPoolAsync()
    {
        _poolLoading = true;
        StateHasChanged();
        try
        {
            var qs = new List<string>();
            if (!string.IsNullOrWhiteSpace(_poolSearch))
            {
                qs.Add($"proveedor={Uri.EscapeDataString(_poolSearch)}");
                qs.Add($"numeroDocumento={Uri.EscapeDataString(_poolSearch)}");
            }
            var url = "api/rendicion/compras-no-vinculadas" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
            var data = await Http.GetFromJsonAsync<List<CompraDto>>(url, _cts.Token) ?? new();
            // filter by kind: Gasto → GASTO_GENERAL, Compra → mercadería (todo lo que no es gasto)
            _pool = data.Where(c => _boletaKind == BoletaKind.Gasto
                    ? string.Equals(c.TipoFactura, "GASTO_GENERAL", StringComparison.OrdinalIgnoreCase)
                    : !string.Equals(c.TipoFactura, "GASTO_GENERAL", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load pool");
            _pool = new();
            _boletaError = "Error al cargar las boletas sin vincular.";
        }
        finally
        {
            _poolLoading = false;
            StateHasChanged();
        }
    }

    private void OnPoolSearchChanged(ChangeEventArgs e)
    {
        _poolSearch = e.Value?.ToString() ?? "";
        _ = LoadPoolAsync();
    }

    private async Task VincularPool(int compraId)
    {
        if (_periodo is null) return;
        var transf = _periodo.Transferencias.FirstOrDefault();
        if (transf is null)
        {
            _boletaError = "La rendición no tiene transferencia para vincular.";
            return;
        }

        _boletaError = null;
        try
        {
            var resp = await Http.PostAsync(
                $"api/contabilidad/transferencia/{transf.Id}/vincular-compra/{compraId}", null, _cts.Token);
            if (!await GuardOk(resp, msg => _boletaError = msg)) return;

            await LoadRendicionDetailAsync(_periodo.Id);
            _view = View.Overview;
            ShowToast("Boleta vinculada");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to vincular boleta {Id}", compraId);
            _boletaError = "Error de red al vincular la boleta.";
        }
        StateHasChanged();
    }

    // ─── OCR (Subir nueva) ───────────────────────────────────────────────
    private void ResetOcr()
    {
        _ocrState = OcrState.Idle;
        _ocrProveedor = "";
        _ocrDoc = "";
        _ocrFecha = DateTime.Today;
        _ocrSubcat = "GENERALES";
        _ocrFotoBytes = null;
        _ocrFotoContentType = null;
        _ocrFotoName = null;
        _ocrItems.Clear();
        _ocrDraftDesc = "";
        _ocrDraftCant = 1;
        _ocrDraftPrecio = 0;
        _ocrSaving = false;
    }

    private void RetakeOcr()
    {
        ResetOcr();
        StateHasChanged();
    }

    private async Task OnOcrPhotoSelected(InputFileChangeEventArgs e)
    {
        if (e.FileCount == 0) return;
        var file = e.File;
        const long maxSize = 10 * 1024 * 1024;
        if (file.Size > maxSize)
        {
            _boletaError = "La imagen supera los 10MB.";
            return;
        }

        _boletaError = null;
        _ocrState = OcrState.Reading;
        StateHasChanged();

        try
        {
            byte[] bytes;
            using (var stream = file.OpenReadStream(maxSize, _cts.Token))
            using (var ms = new MemoryStream())
            {
                await stream.CopyToAsync(ms, _cts.Token);
                bytes = ms.ToArray();
            }
            _ocrFotoBytes = bytes;
            _ocrFotoContentType = file.ContentType;
            _ocrFotoName = file.Name;

            using var content = new MultipartFormDataContent();
            var byteContent = new ByteArrayContent(bytes);
            byteContent.Headers.ContentType = new MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
            content.Add(byteContent, "file", file.Name);

            var resp = await Http.PostAsync("api/compras/upload-factura", content, _cts.Token);
            if (resp.IsSuccessStatusCode)
            {
                var result = await resp.Content.ReadFromJsonAsync<OcrInvoiceResultDto>(_cts.Token);
                ApplyOcrResult(result);
            }
            else
            {
                Logger.LogWarning("OCR upload returned {Status}", resp.StatusCode);
                // Fall back to empty editable form (manual entry).
                ApplyOcrResult(null);
                ShowToast("El OCR no pudo leer la boleta. Cargala a mano.", "bi-exclamation-circle");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.LogError(ex, "OCR upload failed");
            ApplyOcrResult(null);
            _boletaError = "Error al leer la boleta. Podés cargarla a mano.";
        }
        finally
        {
            _ocrState = OcrState.Done;
            StateHasChanged();
        }
    }

    private void ApplyOcrResult(OcrInvoiceResultDto? r)
    {
        _ocrItems.Clear();
        if (r is null)
        {
            _ocrProveedor = "";
            _ocrDoc = "";
            _ocrFecha = DateTime.Today;
            return;
        }

        _ocrProveedor = r.Proveedor ?? "";
        _ocrDoc = r.NumeroDocumento ?? "";
        if (TryParseFecha(r.Fecha, out var dt)) _ocrFecha = dt;

        // subcategoría heurística (gasto)
        var prov = _ocrProveedor.ToLowerInvariant();
        if (prov.Contains("bencina") || prov.Contains("copec") || prov.Contains("shell") ||
            prov.Contains("petro") || prov.Contains("gasolin"))
            _ocrSubcat = "LOGISTICA";
        else if (prov.Contains("peaje") || prov.Contains("autopista") || prov.Contains("tag") ||
                 prov.Contains("costanera") || prov.Contains("vespucio"))
            _ocrSubcat = "PEAJES";

        foreach (var item in r.Items)
        {
            _ocrItems.Add(new OcrLine
            {
                Producto = item.Producto ?? "",
                Cantidad = item.Cantidad > 0 ? (int)Math.Round(item.Cantidad) : 1,
                CostoUnitario = item.CostoUnitario,
                ProductoIdMatch = item.ProductoIdMatch is > 0 ? item.ProductoIdMatch : null,
                Ean = item.Ean,
                Sku = item.Sku,
                PackSize = item.PackSize
            });
        }

        // gasto sin líneas: crear una línea con el monto total
        if (_ocrItems.Count == 0 && r.MontoTotal > 0)
        {
            _ocrItems.Add(new OcrLine
            {
                Producto = string.IsNullOrWhiteSpace(_ocrProveedor) ? "Gasto" : _ocrProveedor,
                Cantidad = 1,
                CostoUnitario = r.MontoTotal
            });
        }
    }

    private decimal OcrTotal => _ocrItems.Sum(i => i.Subtotal);

    private void AddOcrItem()
    {
        if (string.IsNullOrWhiteSpace(_ocrDraftDesc)) return;
        _ocrItems.Add(new OcrLine
        {
            Producto = _ocrDraftDesc.Trim(),
            Cantidad = _ocrDraftCant > 0 ? _ocrDraftCant : 1,
            CostoUnitario = _ocrDraftPrecio
        });
        _ocrDraftDesc = "";
        _ocrDraftCant = 1;
        _ocrDraftPrecio = 0;
        StateHasChanged();
    }

    private void RemoveOcrItem(OcrLine line)
    {
        _ocrItems.Remove(line);
        StateHasChanged();
    }

    private async Task SaveNueva()
    {
        if (_periodo is null) return;
        var transf = _periodo.Transferencias.FirstOrDefault();
        if (transf is null)
        {
            _boletaError = "La rendición no tiene transferencia para vincular la boleta.";
            return;
        }
        if (_ocrItems.Count == 0)
        {
            _boletaError = "Agregá al menos un ítem a la boleta.";
            return;
        }

        _boletaError = null;
        _ocrSaving = true;
        StateHasChanged();

        try
        {
            var esGasto = _boletaKind == BoletaKind.Gasto;
            var req = new CompraVinculadaRequest
            {
                TransferenciaId = transf.Id,
                RendicionId = transf.RendicionId ?? 0,
                Trabajador = _periodo.Trabajador ?? "",
                Proveedor = _ocrProveedor,
                NumeroDocumento = string.IsNullOrWhiteSpace(_ocrDoc) ? null : _ocrDoc,
                FechaCompra = _ocrFecha,
                TipoFactura = esGasto ? "GASTO_GENERAL" : "MERCADERIA",
                SubcategoriaGasto = esGasto ? _ocrSubcat : null,
                Detalles = _ocrItems.Select(i => new RegistrarDetalleCompraRequestDto
                {
                    ProductoId = i.ProductoIdMatch,
                    DescripcionItem = i.Producto,
                    Cantidad = i.Cantidad,
                    CostoUnitario = i.CostoUnitario,
                    Ean = i.Ean,
                    Sku = i.Sku,
                    PackSize = i.PackSize
                }).ToList()
            };

            var resp = await Http.PostAsJsonAsync("api/contabilidad/compra-vinculada", req, _cts.Token);
            if (!await GuardOk(resp, msg => _boletaError = msg)) return;

            await LoadRendicionDetailAsync(_periodo.Id);
            _view = View.Overview;
            ShowToast("Boleta registrada en Compras");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to save OCR boleta");
            _boletaError = "Error de red al registrar la boleta.";
        }
        finally
        {
            _ocrSaving = false;
            StateHasChanged();
        }
    }

    // =====================================================================
    // COMPROBANTE (verificar / rechazar)
    // =====================================================================
    private void OpenComprobante(ItemKind kind, int id)
    {
        _selKind = kind;
        _selId = id;
        _comprobanteError = null;
        _view = View.Comprobante;
        StateHasChanged();
    }

    private async Task VerifyAndNext()
    {
        if (_periodo is null || _selKind == ItemKind.Gasto) return;
        _verifBusy = true;
        StateHasChanged();
        try
        {
            var url = _selKind == ItemKind.Transferencia
                ? $"api/contabilidad/transferencia/{_selId}/verificar"
                : $"api/contabilidad/compra/{_selId}/verificar";
            var resp = await Http.PostAsync(url, null, _cts.Token);
            if (!await GuardOk(resp, msg => _comprobanteError = msg)) return;

            await LoadRendicionDetailAsync(_periodo.Id);

            // jump to next pending item, else back to overview
            var next = FirstPendingItem();
            if (next is not null)
            {
                _selKind = next.Value.kind;
                _selId = next.Value.id;
                ShowToast("Verificado · siguiente pendiente", "bi-check2-all");
            }
            else
            {
                _view = View.Overview;
                ShowToast("Todo verificado", "bi-check2-circle");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Verify failed");
            _comprobanteError = "Error al verificar.";
        }
        finally
        {
            _verifBusy = false;
            StateHasChanged();
        }
    }

    private async Task Unverify()
    {
        if (_periodo is null || _selKind == ItemKind.Gasto) return;
        _verifBusy = true;
        StateHasChanged();
        try
        {
            var url = _selKind == ItemKind.Transferencia
                ? $"api/contabilidad/transferencia/{_selId}/desverificar"
                : $"api/contabilidad/compra/{_selId}/desverificar";
            var resp = await Http.PostAsync(url, null, _cts.Token);
            if (!await GuardOk(resp, msg => _comprobanteError = msg)) return;
            await LoadRendicionDetailAsync(_periodo.Id);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unverify failed");
            _comprobanteError = "Error al quitar la verificación.";
        }
        finally
        {
            _verifBusy = false;
            StateHasChanged();
        }
    }

    // Returns the first pending (unverified) item across transferencias + compras.
    private (ItemKind kind, int id)? FirstPendingItem()
    {
        if (_periodo is null) return null;
        foreach (var t in _periodo.Transferencias)
        {
            if (!t.Verificada) return (ItemKind.Transferencia, t.Id);
        }
        foreach (var t in _periodo.Transferencias)
            foreach (var c in t.Compras)
                if (!c.Verificada) return (ItemKind.Compra, c.Id);
        return null;
    }

    // =====================================================================
    // TOAST
    // =====================================================================
    private void ShowToast(string message, string icon = "bi-check2-circle")
    {
        _toastMessage = message;
        _toastIcon = icon;
        StateHasChanged();

        _toastTimer?.Dispose();
        _toastTimer = new Timer(_ =>
        {
            if (_disposed) return;
            _toastMessage = null;
            _toastIcon = null;
            InvokeAsync(StateHasChanged);
        }, null, 2400, Timeout.Infinite);
    }

    // =====================================================================
    // DERIVED VALUES / HELPERS
    // =====================================================================
    private static readonly CultureInfo Cl = CultureInfo.GetCultureInfo("es-CL");

    private static string Fmt(decimal v)
    {
        var sign = v < 0 ? "-" : "";
        return $"{sign}${Math.Abs(v).ToString("N0", Cl)}";
    }

    private static decimal ParseMonto(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        // strip everything but digits (Chilean amounts use '.' as thousands sep)
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return decimal.TryParse(digits, out var v) ? v : 0;
    }

    private static bool TryParseFecha(string? s, out DateTime dt)
    {
        dt = DateTime.Today;
        if (string.IsNullOrWhiteSpace(s)) return false;
        string[] formats = { "dd-MM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "d-M-yyyy", "d/M/yyyy" };
        if (DateTime.TryParseExact(s, formats, Cl, DateTimeStyles.None, out dt)) return true;
        return DateTime.TryParse(s, Cl, DateTimeStyles.None, out dt);
    }

    private async Task<bool> GuardOk(HttpResponseMessage resp, Action<string> setError)
    {
        if (resp.IsSuccessStatusCode) return true;
        string body;
        try { body = await resp.Content.ReadAsStringAsync(_cts.Token); }
        catch { body = ""; }
        setError(string.IsNullOrWhiteSpace(body) ? $"Error ({(int)resp.StatusCode})." : $"Error: {body}");
        StateHasChanged();
        return false;
    }

    // ── Rendición-level derived totals ────────────────────────────────────
    private decimal Transferido => _periodo?.TotalTransferido ?? 0;
    private decimal ComprasGastos => (_periodo?.TotalCompras ?? 0) + (_periodo?.TotalGastos ?? 0);
    private decimal Saldo => _periodo?.Diferencia ?? 0;
    private bool RendicionAbierta => _periodo?.Estado == AccountingPeriodEstado.Abierto;

    private (int verif, int total) VerifStats()
    {
        if (_periodo is null) return (0, 0);
        int total = 0, verif = 0;
        foreach (var t in _periodo.Transferencias)
        {
            total++; if (t.Verificada) verif++;
            foreach (var c in t.Compras)
            {
                total++; if (c.Verificada) verif++;
            }
        }
        // gastos are informational (no verify state in backend) — not counted
        return (verif, total);
    }

    private IEnumerable<CompraDto> AllCompras =>
        _periodo?.Transferencias.SelectMany(t => t.Compras) ?? Enumerable.Empty<CompraDto>();

    // ── Estado label for list cards ───────────────────────────────────────
    private static string EstadoLabel(AccountingPeriodEstado e) =>
        e == AccountingPeriodEstado.Abierto ? "Abierta" : "Cerrada";

    // =====================================================================
    // IDISPOSABLE
    // =====================================================================
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Restore the navbar when leaving the page — a detail view may have
        // collapsed it, and the next page would otherwise render without one.
        Layout?.ExpandNavbar();

        _cts.Cancel();
        _cts.Dispose();
        _toastTimer?.Dispose();
    }
}
