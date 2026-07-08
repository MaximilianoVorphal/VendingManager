// =========================================================================
// MobileDatePickerSheet.razor.cs — Code-behind for date picker bottom sheet
// =========================================================================

using Microsoft.AspNetCore.Components;

namespace VendingManager.Web.Components;

public partial class MobileDatePickerSheet : ComponentBase
{
    // ─── Parameters ─────────────────────────────────────────────────────

    /// <summary>Whether the bottom sheet is visible.</summary>
    [Parameter, EditorRequired] public bool Visible { get; set; }

    /// <summary>Called when the sheet is dismissed (Cancel or backdrop tap).</summary>
    [Parameter, EditorRequired] public EventCallback OnClose { get; set; }

    /// <summary>Called with the confirmed date when the user taps "Guardar".</summary>
    [Parameter, EditorRequired] public EventCallback<DateTime> OnDateConfirmed { get; set; }

    /// <summary>Current date to display in the picker.</summary>
    [Parameter] public DateTime CurrentDate { get; set; } = DateTime.Now;

    // ─── Internal State ─────────────────────────────────────────────────

    private DateTime _selectedDate;
    private bool _wasVisible;

    // ─── Lifecycle ──────────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        // Reset to CurrentDate when the sheet OPENS (false → true transition)
        if (Visible && !_wasVisible)
        {
            _selectedDate = CurrentDate;
        }
        _wasVisible = Visible;
    }

    // ─── Event Handlers ─────────────────────────────────────────────────

    private void HandleDateChanged(ChangeEventArgs e)
    {
        var value = e.Value?.ToString();
        if (DateTime.TryParse(value, out var parsed))
        {
            _selectedDate = parsed;
        }
    }

    private async Task HandleSave()
    {
        await OnDateConfirmed.InvokeAsync(_selectedDate);
    }

    private async Task HandleCancel()
    {
        await OnClose.InvokeAsync();
    }
}
