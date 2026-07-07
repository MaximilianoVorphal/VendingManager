// =========================================================================
// RecargaMovil.razor.cs — Code-behind for the mobile Recarga page
// =========================================================================
// 4 views switched by `View` enum: List, Overview, PickMachine, EditSlots.
// Each view's render is inline in RecargaMovil.razor via @if (_view == View.X).
// State is in-memory; loss-on-navigate is acceptable per NG1.
// 2 bottom sheets: product selector (inline <div>) and slot editor dock.
// The photo sheet (MobileMachinePhotoSheet) comes in PR 3.
// =========================================================================

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
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
    private bool _compactDensity = true;
    private string DensityKey
    {
        get => _compactDensity ? "compacta" : "comoda";
        set => _compactDensity = value == "compacta";
    }

    // ─── Photo Sheet (PR 3 placeholder) ──────────────────────────────────

    private bool _photoSheetVisible;

    // ─── Toast ───────────────────────────────────────────────────────────

    private string? _toastMessage;
    private string? _toastIcon;
    private Timer? _toastTimer;

    // ─── Cancellation ────────────────────────────────────────────────────

    private CancellationTokenSource _cts = new();

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
                Periodos = template.Periodos.Select(p => new CreatePeriodoDto
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
                }).ToList()
            };

            // Add new machine as a periodo
            updateDto.Periodos.Add(new CreatePeriodoDto
            {
                MaquinaId = machineId,
                FechaRecarga = DateTime.Now,
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
                    .Select(p => new CreatePeriodoDto
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
                    }).ToList()
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
        StateHasChanged();
    }

    private async Task ResetSlotsAsync(int machineId)
    {
        if (_activeTemplate == null || _editingMachine == null) return;

        // Reload slot configuration from API
        var freshSlots = await LoadMachineSlotsAsync(machineId);
        _slots = freshSlots;
        StateHasChanged();
        ShowToast("Slots restablecidos", "bi-arrow-counterclockwise");
    }

    /// <summary>
    /// SaveSlotsAsync — NO-OP in PR 2. The full save + photo sheet flow
    /// (POST .../slot-batch then photo sheet) is wired in PR 3.
    /// </summary>
    private Task SaveSlotsAsync()
    {
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
        _error = null;
        _productSheetVisible = false;
        _slotDockVisible = false;
        StateHasChanged();
    }

    private void GoToOverview(TemplateRecargaDto template)
    {
        _activeTemplate = template;
        _machines = template.Periodos.ToList();
        _view = View.Overview;
        _error = null;
        StateHasChanged();
    }

    private async Task GoToPickMachineAsync()
    {
        _pickSearch = "";
        _pickScanning = false;
        await LoadMachinePoolAsync();
        _view = View.PickMachine;
        _error = null;
        StateHasChanged();
    }

    private void GoToEditSlots(PeriodoRecargaDto machine)
    {
        _editingMachine = machine;
        _slots = machine.SnapshotSlots.ToList();
        _slotDockVisible = false;
        _productSheetVisible = false;
        _view = View.EditSlots;
        _error = null;
        StateHasChanged();
    }

    // =====================================================================
    // EVENT HANDLERS
    // =====================================================================

    private void OnNewCarga()
    {
        // Navigate to PC page for creating new templates
        Nav.NavigateTo("/templates-recarga");
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
    /// OnFinalizarCarga — NO-OP placeholder in PR 2.
    /// PR 3 wires this to open the MobileMachinePhotoSheet.
    /// </summary>
    private void OnFinalizarCarga()
    {
        // Wired in PR 3 — shows the photo sheet
        ShowToast("Finalizar carga se habilita en PR 3", "bi-info-circle");
    }

    // ─── Product Sheet ───────────────────────────────────────────────────

    private async void OpenProductSheet(SnapshotSlotDto slot)
    {
        _editingSlot = slot;
        _editingSlotIndex = _slots.IndexOf(slot);
        _productSearch = "";

        if (_productCatalog.Count == 0)
        {
            await LoadProductCatalogAsync();
        }

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
            await Task.Delay(1500); // Simulate scan animation

            // Pick the first available machine
            var availableMachines = GetAvailableMachines();
            if (availableMachines.Any())
            {
                var first = availableMachines.First();
                await OnAddMachineClick(first.Id);
            }
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
            _toastMessage = null;
            _toastIcon = null;
            InvokeAsync(StateHasChanged);
        }, null, 2400, Timeout.Infinite);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

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
        _cts.Cancel();
        _cts.Dispose();
        _toastTimer?.Dispose();
    }
}
