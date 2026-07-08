// =========================================================================
// RecargaMovil.razor.cs — Code-behind for the mobile Recarga page
// =========================================================================
// SECTIONS (search for the section markers):
//   1. View enum + state fields
//   2. Lifecycle (OnInitializedAsync)
//   3. HTTP methods
//   4. Navigation (GoTo*)
//   5. Event handlers + sheets
//   6. Helpers + IDisposable
// =========================================================================

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms; // PR 3 — InputFile for photo sheet
using Microsoft.Extensions.Logging;
using VendingManager.Web.Components; // PR 3 fix 5 — MobileMachinePhotoSheet @ref
using Microsoft.JSInterop;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;

namespace VendingManager.Web.Pages;

public partial class RecargaMovil : ComponentBase, IDisposable
{
    // ─── View Enum ───────────────────────────────────────────────────────

    private enum View { List, Overview, PickMachine, EditSlots }
    private View _view = View.List;

    // ─── Dependencies (injections set from .razor) ───────────────────────

    [Inject] private HttpClient Http { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private ILogger<RecargaMovil> Logger { get; set; } = null!;

    // ─── Data State ──────────────────────────────────────────────────────

    private List<TemplateRecargaDto>? _templates;
    private TemplateRecargaDto? _activeTemplate;
    private List<PeriodoRecargaDto> _machines = new();
    private PeriodoRecargaDto? _editingMachine;
    private List<SnapshotSlotDto> _slots = new();
    private List<MaquinaSimpleDto> _machinePool = new();
    private List<ProductoSimpleDto> _productCatalog = new();

    // ─── Loading / Error ─────────────────────────────────────────────────

    private bool _loading;
    private string? _error;

    // ─── Pick View State ─────────────────────────────────────────────────

    private string _pickSearch = "";
    private bool _pickScanning;

    // ─── Product Sheet State ─────────────────────────────────────────────

    private bool _productSheetVisible;
    private string _productSearch = "";
    private List<ProductoSimpleDto> _filteredProducts = new();

    // ─── Slot Editor Dock State ─────────────────────────────────────────

    private bool _slotDockVisible;
    private SnapshotSlotDto? _editingSlot;
    private int _editingSlotIndex;
    private bool _compactDensity = true; // PR 4 — wire to slot grid density toggle
    private bool _hasChanges; // PR 4 — Guardar disabled when no changes
    private string DensityKey
    {
        get => _compactDensity ? "compacta" : "comoda";
        set => _compactDensity = value == "compacta";
    }

    // ─── Photo Sheet (PR 3) ─────────────────────────────────────────────

    private bool _photoSheetVisible; // Wired to MobileMachinePhotoSheet.Visible
    private PeriodoRecargaDto? _photoSheetMachine; // Machine context for the open photo sheet
    private MobileMachinePhotoSheet? _photoSheet; // @ref for inline error display

    // ─── Toast ───────────────────────────────────────────────────────────

    private string? _toastMessage;
    private string? _toastIcon;
    private Timer? _toastTimer;

    // ─── Cancellation ────────────────────────────────────────────────────

    private CancellationTokenSource _cts = new();
    private CancellationTokenSource? _uploadCts; // Per-upload CTS, cancelled on sheet close
    private bool _disposed;

    // =====================================================================
    // LIFECYCLE
    // =====================================================================

    protected override async Task OnInitializedAsync()
    {
        _view = View.List;
        await LoadTemplatesAsync();
    }

    // =====================================================================
    // HTTP METHODS
    // =====================================================================

    private async Task LoadTemplatesAsync()
    {
        _loading = true;
        _error = null;
        StateHasChanged();

        try
        {
            _templates = await Http.GetFromJsonAsync<List<TemplateRecargaDto>>(
                "/api/TemplateRecarga", _cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load templates");
            _error = "Error al cargar las recargas";
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private async Task LoadTemplateAsync(int templateId)
    {
        _loading = true;
        _error = null;
        StateHasChanged();

        try
        {
            _activeTemplate = await Http.GetFromJsonAsync<TemplateRecargaDto>(
                $"/api/TemplateRecarga/{templateId}", _cts.Token);

            if (_activeTemplate != null)
            {
                _machines = _activeTemplate.Periodos.ToList();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load template {Id}", templateId);
            _error = "Error al cargar la recarga";
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private async Task<List<SnapshotSlotDto>> LoadMachineSlotsAsync(int machineId)
    {
        try
        {
            var slots = await Http.GetFromJsonAsync<List<SnapshotSlotDto>>(
                $"/api/TemplateRecarga/maquina/{machineId}/slots", _cts.Token);
            return slots ?? new();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load slots for machine {Id}", machineId);
            _error = "Error al cargar slots de la máquina";
            return new();
        }
    }

    private async Task BatchUpdateSlotsAsync(int templateId, int periodoId, List<SlotActionDto> actions)
    {
        _loading = true;
        _error = null;
        StateHasChanged();

        try
        {
            var request = new SlotBatchRequest { Actions = actions };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await Http.PostAsync(
                $"/api/TemplateRecarga/{templateId}/periodo/{periodoId}/slot-batch",
                content, _cts.Token);

            response.EnsureSuccessStatusCode();
            ShowToast("Slots guardados");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to batch update slots");
            _error = "Error al guardar slots";
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private async Task AddMachineAsync(int templateId, int machineId)
    {
        _loading = true;
        _error = null;
        StateHasChanged();

        try
        {
            // Fetch current template state
            var template = await Http.GetFromJsonAsync<TemplateRecargaDto>(
                $"/api/TemplateRecarga/{templateId}", _cts.Token);
            if (template == null) return;

            // Get the slot configuration for this machine
            var slotConfig = await LoadMachineSlotsAsync(machineId);

            // Build the update DTO preserving existing periods + adding new one
            var updateDto = new UpdateTemplateRecargaDto
            {
                Nombre = template.Nombre,
                Descripcion = template.Descripcion,
                Periodos = template.Periodos.Select(ClonePeriodo).ToList()
            };

            // Add new machine as a periodo
            updateDto.Periodos.Add(new CreatePeriodoDto
            {
                MaquinaId = machineId,
                FechaRecarga = DateTime.UtcNow,
                SnapshotSlots = slotConfig.Select(s => new CreateSnapshotSlotDto
                {
                    NumeroSlot = s.NumeroSlot,
                    ProductoId = s.ProductoId,
                    CantidadInicial = s.CantidadInicial,
                    CapacidadSlot = s.CapacidadSlot,
                    Estado = s.Estado
                }).ToList()
            });

            var json = JsonSerializer.Serialize(updateDto);
            var putContent = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await Http.PutAsync(
                $"/api/TemplateRecarga/{templateId}", putContent, _cts.Token);
            response.EnsureSuccessStatusCode();

            // Reload template
            await LoadTemplateAsync(templateId);

            // Navigate to edit slots for the new machine
            var newPeriodo = _activeTemplate?.Periodos
                .FirstOrDefault(p => p.MaquinaId == machineId);
            if (newPeriodo != null)
            {
                GoToEditSlots(newPeriodo);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to add machine {MachineId} to template {TemplateId}",
                machineId, templateId);
            _error = "Error al agregar máquina";
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private async Task RemoveMachineAsync(int templateId, int periodoId)
    {
        _loading = true;
        _error = null;
        StateHasChanged();

        try
        {
            var template = await Http.GetFromJsonAsync<TemplateRecargaDto>(
                $"/api/TemplateRecarga/{templateId}", _cts.Token);
            if (template == null) return;

            var updateDto = new UpdateTemplateRecargaDto
            {
                Nombre = template.Nombre,
                Descripcion = template.Descripcion,
                Periodos = template.Periodos
                    .Where(p => p.Id != periodoId)
                    .Select(ClonePeriodo).ToList()
            };

            var json = JsonSerializer.Serialize(updateDto);
            var putContent = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await Http.PutAsync(
                $"/api/TemplateRecarga/{templateId}", putContent, _cts.Token);
            response.EnsureSuccessStatusCode();

            await LoadTemplateAsync(templateId);
            GoToList();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to remove machine {Id}", periodoId);
            _error = "Error al quitar máquina";
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private async Task VaciarSlotsAsync(int machineId)
    {
        if (_activeTemplate == null || _editingMachine == null) return;

        var actions = _slots
            .Where(s => s.CantidadInicial > 0)
            .Select(s => new SlotActionDto
            {
                SlotId = s.Id,
                ActionType = "EMPTY",
                Cantidad = 0
            })
            .ToList();

        if (actions.Count == 0)
        {
            ShowToast("No hay slots con carga", "bi-info-circle");
            return;
        }

        await BatchUpdateSlotsAsync(_activeTemplate.Id, _editingMachine.Id, actions);

        // Refresh local slot state
        foreach (var slot in _slots)
        {
            if (slot.CantidadInicial > 0)
            {
                slot.CantidadInicial = 0;
            }
        }
        _hasChanges = false;
        StateHasChanged();
    }

    private async Task ResetSlotsAsync(int machineId)
    {
        if (_activeTemplate == null || _editingMachine == null) return;

        // Reload slot configuration from API
        var freshSlots = await LoadMachineSlotsAsync(machineId);
        _slots = freshSlots;
        _hasChanges = false;
        StateHasChanged();
        ShowToast("Slots restablecidos", "bi-arrow-counterclockwise");
    }

    /// <summary>
    /// SaveSlotsAsync — NO-OP in PR 2. The full save + photo sheet flow
    /// (POST .../slot-batch then photo sheet) is wired in PR 3.
    /// </summary>
    private Task SaveSlotsAsync()
    {
        _hasChanges = false;
        // Wired in PR 3 with the photo sheet
        ShowToast("Guardar carga se habilita en PR 3", "bi-info-circle");
        return Task.CompletedTask;
    }

    private async Task LoadProductCatalogAsync()
    {
        try
        {
            _productCatalog = await Http.GetFromJsonAsync<List<ProductoSimpleDto>>(
                "/api/Ventas/lista-productos", _cts.Token) ?? new();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load product catalog");
            _productCatalog = new();
        }
    }

    private async Task LoadMachinePoolAsync()
    {
        try
        {
            _machinePool = await Http.GetFromJsonAsync<List<MaquinaSimpleDto>>(
                "/api/Ventas/lista-maquinas", _cts.Token) ?? new();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load machine pool");
            _machinePool = new();
        }
    }

    // =====================================================================
    // NAVIGATION
    // =====================================================================

    private void GoToList()
    {
        _view = View.List;
        _activeTemplate = null;
        _machines = new();
        _editingMachine = null;
        _slots = new();
        // Do NOT clear _error — caller may have set it (e.g. post-upload LoadTemplatesAsync failure)
        _productSheetVisible = false;
        _slotDockVisible = false;
        StateHasChanged();
    }

    private void GoToOverview(TemplateRecargaDto template)
    {
        _activeTemplate = template;
        _machines = template.Periodos.ToList();
        _editingMachine = null;
        _slots = new();
        _slotDockVisible = false;
        _productSheetVisible = false;
        _view = View.Overview;
        _error = null;
        StateHasChanged();
    }

    private async Task GoToPickMachineAsync()
    {
        _pickSearch = "";
        _pickScanning = false;
        _editingMachine = null;
        _slots = new();
        _slotDockVisible = false;
        _productSheetVisible = false;
        await LoadMachinePoolAsync();
        _view = View.PickMachine;
        _error = null;
        StateHasChanged();
    }

    private void GoToEditSlots(PeriodoRecargaDto machine)
    {
        _editingMachine = machine;
        _slots = machine.SnapshotSlots.ToList();
        _hasChanges = false;
        _slotDockVisible = false;
        _productSheetVisible = false;
        _view = View.EditSlots;
        _error = null;
        StateHasChanged();
    }

    // =====================================================================
    // EVENT HANDLERS
    // =====================================================================

    private async Task OnNuevaCargaAsync()
    {
        _loading = true;
        _error = null;
        StateHasChanged();

        try
        {
            var dto = new CreateTemplateRecargaDto
            {
                Nombre = $"Carga {DateTime.UtcNow:dd/MM}",
                Descripcion = null,
                Periodos = new List<CreatePeriodoDto>
                {
                    new CreatePeriodoDto
                    {
                        MaquinaId = 0,
                        FechaRecarga = DateTime.UtcNow,
                        SnapshotSlots = new List<CreateSnapshotSlotDto>()
                    }
                }
            };

            var json = JsonSerializer.Serialize(dto);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await Http.PostAsync("/api/TemplateRecarga", content, _cts.Token);
            response.EnsureSuccessStatusCode();

            var newTemplate = await response.Content.ReadFromJsonAsync<TemplateRecargaDto>(_cts.Token);
            if (newTemplate != null)
            {
                _activeTemplate = newTemplate;
                _machines = newTemplate.Periodos.ToList();
                _view = View.Overview;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to create new template");
            _error = "Error al crear nueva recarga";
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private async Task OnTemplateCardClick(TemplateRecargaDto template)
    {
        await LoadTemplateAsync(template.Id);
        if (_activeTemplate != null)
        {
            GoToOverview(_activeTemplate);
        }
    }

    private async Task OnAddMachineClick(int machineId)
    {
        if (_activeTemplate == null) return;
        await AddMachineAsync(_activeTemplate.Id, machineId);
    }

    private async Task OnRemoveMachineClick(PeriodoRecargaDto machine)
    {
        if (_activeTemplate == null) return;
        var confirmed = await JS.InvokeAsync<bool>("confirm", "¿Quitar máquina de la carga?");
        if (!confirmed) return;
        await RemoveMachineAsync(_activeTemplate.Id, machine.Id);
    }

    private void OnMachineCardClick(PeriodoRecargaDto machine)
    {
        GoToEditSlots(machine);
    }

    private void OnSlotEdit(SnapshotSlotDto slot)
    {
        OpenSlotDock(slot);
    }

    private void OnSlotChanged(SnapshotSlotDto slot, int newQty)
    {
        _hasChanges = true;
        slot.CantidadInicial = Math.Min(newQty, slot.CapacidadSlot);
        if (_editingSlot == slot && _slotDockVisible)
        {
            _editingSlotIndex = _slots.IndexOf(slot);
        }
        StateHasChanged();
    }

    private void OnProductSelected(ProductoSimpleDto product)
    {
        if (_editingSlot == null) return;

        _hasChanges = true;
        _editingSlot.ProductoId = product.Id;
        _editingSlot.ProductoNombre = product.Nombre;
        if (_editingSlot.CantidadInicial <= 0)
        {
            _editingSlot.CantidadInicial = Math.Min(1, _editingSlot.CapacidadSlot);
        }

        _productSheetVisible = false;
        StateHasChanged();
    }

    /// <summary>
    /// OnFinalizarCarga — opens the photo sheet when all machines are loaded.
    /// PR 3: wired to open MobileMachinePhotoSheet.
    /// </summary>
    private void OnFinalizarCarga()
    {
        if (_activeTemplate == null) return;

        // Pick the first machine that is loaded for photo context
        _photoSheetMachine = _machines.FirstOrDefault();
        if (_photoSheetMachine == null)
        {
            ShowToast("No hay máquinas para finalizar", "bi-exclamation-circle");
            return;
        }

        _photoSheetVisible = true;
        StateHasChanged();
    }

    /// <summary>
    /// HandlePhotoAccepted — receives the captured file from MobileMachinePhotoSheet,
    /// builds MultipartFormDataContent with field name "file", and PUTs to the
    /// foto-guía endpoint. On success: close sheet, toast, navigate to Lista.
    /// On error: keep sheet open with retry.
    /// </summary>
    private async Task HandlePhotoAccepted(IBrowserFile file)
    {
        var template = _activeTemplate;
        var machine = _photoSheetMachine;
        if (template == null || machine == null)
        {
            _error = "Contexto perdido. Volvé a intentarlo.";
            StateHasChanged();
            return;
        }

        // Find the periodo for the machine
        var periodo = template.Periodos.FirstOrDefault(p => p.MaquinaId == machine.MaquinaId);
        if (periodo == null)
        {
            _error = "Período no encontrado.";
            StateHasChanged();
            return;
        }

        // Read the file into a byte[] (max 10MB)
        byte[] bytes;
        try
        {
            await using var stream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, _cts.Token);
            bytes = ms.ToArray();
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            _error = $"Error al leer la foto: {ex.Message}";
            StateHasChanged();
            return;
        }

        // Build MultipartFormDataContent with field name "file" (the controller's expected field name)
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
        content.Add(fileContent, "file", file.Name);

        // PUT to the foto-guía endpoint
        _uploadCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        try
        {
            var url = $"api/TemplateRecarga/{template.Id}/periodo/{periodo.Id}/foto-guia";
            var response = await Http.PutAsync(url, content, _uploadCts.Token);
            if (response.IsSuccessStatusCode)
            {
                _photoSheetVisible = false;
                _photoSheetMachine = null;
                _error = null;

                // POST to /terminar to mark template as Finalizado
                try
                {
                    await Http.PostAsync($"/api/TemplateRecarga/{template.Id}/terminar", null, _uploadCts.Token);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to terminar template {Id} after foto-guía upload — non-critical", template.Id);
                }

                ShowToast("Carga finalizada", "bi-check2-circle");

                // Navigate to Lista — keep any error from LoadTemplatesAsync if it fails
                try
                {
                    await LoadTemplatesAsync();
                }
                catch
                {
                    // _error is already set by LoadTemplatesAsync — let it stay
                    Logger.LogWarning("Post-upload LoadTemplatesAsync failed, showing Lista with error");
                }
                GoToList();
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync(_uploadCts.Token);
                await (_photoSheet?.SetErrorAsync($"Error al subir la foto ({(int)response.StatusCode}). {errorBody}") ?? Task.CompletedTask);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await (_photoSheet?.SetErrorAsync($"Error al subir la foto: {ex.Message}") ?? Task.CompletedTask);
            Logger.LogError(ex, "Error uploading foto-guía for template {TemplateId}, periodo {PeriodoId}",
                template.Id, periodo.Id);
        }
        finally
        {
            _uploadCts?.Dispose();
            _uploadCts = null;
        }
    }

    /// <summary>
    /// HandlePhotoSheetClose — closes the photo sheet and cancels any in-flight upload.
    /// </summary>
    private void HandlePhotoSheetClose()
    {
        _photoSheetVisible = false;
        _photoSheetMachine = null;
        _uploadCts?.Cancel();
        StateHasChanged();
    }

    // ─── Product Sheet ───────────────────────────────────────────────────

    private async Task OpenProductSheetAsync(SnapshotSlotDto slot)
    {
        // Load catalog FIRST, then mutate state
        if (_productCatalog.Count == 0)
        {
            await LoadProductCatalogAsync();
        }

        _editingSlot = slot;
        _editingSlotIndex = _slots.IndexOf(slot);
        _productSearch = "";
        _filteredProducts = FilterProducts(_productSearch);
        _productSheetVisible = true;
        StateHasChanged();
    }

    private void CloseProductSheet()
    {
        _productSheetVisible = false;
        StateHasChanged();
    }

    private void OnProductSearchChanged(ChangeEventArgs e)
    {
        _productSearch = e.Value?.ToString() ?? "";
        _filteredProducts = FilterProducts(_productSearch);
        StateHasChanged();
    }

    private List<ProductoSimpleDto> FilterProducts(string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return _productCatalog;

        return _productCatalog
            .Where(p => p.Nombre.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // ─── Slot Editor Dock ────────────────────────────────────────────────

    private void OpenSlotDock(SnapshotSlotDto slot)
    {
        var idx = _slots.IndexOf(slot);
        if (idx < 0) return;

        _editingSlot = slot;
        _editingSlotIndex = idx;
        _slotDockVisible = true;
        StateHasChanged();
    }

    private void CloseSlotDock()
    {
        _slotDockVisible = false;
        _editingSlot = null;
        StateHasChanged();
    }

    private void GoToPreviousSlot()
    {
        if (_editingSlotIndex <= 0) return;
        _editingSlotIndex--;
        _editingSlot = _slots[_editingSlotIndex];
        StateHasChanged();
    }

    private void GoToNextSlot()
    {
        if (_editingSlotIndex >= _slots.Count - 1) return;
        _editingSlotIndex++;
        _editingSlot = _slots[_editingSlotIndex];
        StateHasChanged();
    }

    private void ClearEditingSlot()
    {
        if (_editingSlot == null) return;
        _hasChanges = true;
        _editingSlot.ProductoId = null;
        _editingSlot.ProductoNombre = "";
        _editingSlot.CantidadInicial = 0;
        StateHasChanged();
    }

    // ─── Pick View ───────────────────────────────────────────────────────

    private void OnPickSearchChanged(ChangeEventArgs e)
    {
        _pickSearch = e.Value?.ToString() ?? "";
        StateHasChanged();
    }

    private async Task OnScanClick()
    {
        _pickScanning = true;
        StateHasChanged();

        try
        {
            await Task.Delay(1500, _cts.Token); // Simulate scan animation

            // Pick the first available machine
            var availableMachines = GetAvailableMachines();
            if (availableMachines.Any())
            {
                var first = availableMachines.First();
                await OnAddMachineClick(first.Id);
            }
        }
        catch (OperationCanceledException)
        {
            // Navigation cancelled during scan — state already reset
        }
        finally
        {
            _pickScanning = false;
            StateHasChanged();
        }
    }

    private List<MaquinaSimpleDto> GetAvailableMachines()
    {
        if (_activeTemplate == null) return _machinePool;

        var activeMachineIds = _machines
            .Select(m => m.MaquinaId)
            .ToHashSet();

        var filtered = string.IsNullOrWhiteSpace(_pickSearch)
            ? _machinePool
            : _machinePool
                .Where(m => m.Nombre.Contains(_pickSearch, StringComparison.OrdinalIgnoreCase))
                .ToList();

        return filtered
            .Where(m => !activeMachineIds.Contains(m.Id))
            .ToList();
    }

    // ─── Scan Overlay ────────────────────────────────────────────────────

    private async Task OnScanOverlayTap()
    {
        if (!_pickScanning) return;
        _pickScanning = false;
        StateHasChanged();
        await Task.CompletedTask;
    }

    // ─── Toast ───────────────────────────────────────────────────────────

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

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static CreatePeriodoDto ClonePeriodo(PeriodoRecargaDto p)
    {
        return new CreatePeriodoDto
        {
            MaquinaId = p.MaquinaId,
            FechaRecarga = p.FechaRecarga,
            SnapshotSlots = p.SnapshotSlots.Select(s => new CreateSnapshotSlotDto
            {
                NumeroSlot = s.NumeroSlot,
                ProductoId = s.ProductoId,
                CantidadInicial = s.CantidadInicial,
                CapacidadSlot = s.CapacidadSlot,
                Estado = s.Estado
            }).ToList()
        };
    }

    private static string MachineShortCode(PeriodoRecargaDto machine)
    {
        return !string.IsNullOrEmpty(machine.IdInternoMaquina)
            ? machine.IdInternoMaquina.Length >= 4
                ? machine.IdInternoMaquina[^4..]
                : machine.IdInternoMaquina
            : $"M{machine.MaquinaId}";
    }

    private bool IsMachineLoaded(PeriodoRecargaDto machine)
    {
        return machine.SnapshotSlots.Any() &&
               machine.SnapshotSlots.All(s => s.CantidadInicial > 0 || s.ProductoId == null);
    }

    private int MachineUnits(PeriodoRecargaDto machine)
    {
        return machine.SnapshotSlots.Sum(s => s.CantidadInicial);
    }

    private int MachineCapacity(PeriodoRecargaDto machine)
    {
        return machine.SnapshotSlots.Sum(s => s.CapacidadSlot);
    }

    private (int loaded, int total) MachinesLoadedStats()
    {
        var loaded = _machines.Count(m => m.SnapshotSlots.Any(s => s.CantidadInicial > 0));
        return (loaded, _machines.Count);
    }

    private (int units, int capacity, int vacios) MachineSlotStats(PeriodoRecargaDto machine)
    {
        var slots = machine.SnapshotSlots;
        return (
            slots.Sum(s => s.CantidadInicial),
            slots.Sum(s => s.CapacidadSlot),
            slots.Count(s => s.CantidadInicial == 0 && s.ProductoId != null)
        );
    }

    private (int units, int capacity, int vacios) AllSlotsStats()
    {
        var allSlots = _slots;
        return (
            allSlots.Sum(s => s.CantidadInicial),
            allSlots.Sum(s => s.CapacidadSlot),
            allSlots.Count(s => s.CantidadInicial == 0 && s.ProductoId != null)
        );
    }

    private string ProgressFillClass(int pct)
    {
        return pct >= 80 ? "rm-progress__fill--good"
             : pct >= 30 ? "rm-progress__fill--warning"
             : "rm-progress__fill--danger";
    }

    private string GetEstadoTag(TemplateRecargaDto t)
    {
        return t.Estado == EstadoTemplate.Terminado ? "Finalizado" : "Pendiente";
    }

    private string GetEstadoTagVariant(TemplateRecargaDto t)
    {
        return t.Estado == EstadoTemplate.Terminado ? "success" : "warning";
    }

    private string GetCargadaTag(PeriodoRecargaDto m)
    {
        return m.SnapshotSlots.Any(s => s.CantidadInicial > 0) ? "Cargada" : "Sin cargar";
    }

    private string GetCargadaTagVariant(PeriodoRecargaDto m)
    {
        return m.SnapshotSlots.Any(s => s.CantidadInicial > 0) ? "success" : "outline";
    }

    private bool AllMachinesLoaded()
    {
        return _machines.Count > 0 &&
               _machines.All(m => m.SnapshotSlots.Any(s => s.CantidadInicial > 0));
    }

    // =====================================================================
    // IDISPOSABLE
    // =====================================================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _cts.Dispose();
        _uploadCts?.Cancel();
        _uploadCts?.Dispose();
        _toastTimer?.Dispose();
    }
}
