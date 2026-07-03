using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using VendingManager.Web.Shared;

namespace VendingManager.Web.Pages
{
    // Code-behind for the Informe de Compras page. Wires the industrial-v3 markup
    // to the existing backend endpoints (api/Ventas/purchase-suggestion[/export]).
    // The DTO is a subset of what the template mock showed: it exposes stock in
    // machines, warehouse stock, period sales and the suggested quantity, but NOT
    // unit cost — so the "Inversión" column/KPI from the template are intentionally
    // absent here. Sorting stays client-side; the backend does the calculation.
    public partial class PurchaseReport : ComponentBase
    {
        [Inject] protected HttpClient Http { get; set; } = default!;
        [Inject] protected ILogger<PurchaseReport> Logger { get; set; } = default!;
        [Inject] protected IJSRuntime JS { get; set; } = default!;

        // ── UI state ──────────────────────────────────────────────────────────────
        private string _maquina = "0";
        private int _dias = 30;
        private bool _soloMaq = false;
        private bool _soloSug = true;
        private string _sortKey = "sug";
        private string _sortDir = "desc";

        private bool _loading = false;
        private bool _exporting = false;
        private string? _error;

        private List<MaquinaSimpleDto>? _maquinas;
        private List<PurchaseSuggestionDto>? _data;
        private CancellationTokenSource? _cts;

        private int MaquinaId => int.TryParse(_maquina, out var v) ? v : 0;

        protected override async Task OnInitializedAsync()
        {
            await CargarMaquinas();
            await Calcular();
        }

        private async Task CargarMaquinas()
        {
            try
            {
                _maquinas = await Http.GetFromJsonAsync<List<MaquinaSimpleDto>>("api/Ventas/lista-maquinas");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error cargando lista de máquinas");
            }
        }

        private void OnDiasChanged(ChangeEventArgs e)
        {
            if (int.TryParse(e.Value?.ToString(), out var v)) _dias = Math.Min(120, Math.Max(1, v));
        }

        private async Task Calcular()
        {
            if (_loading) return;

            _error = null;
            _loading = true;
            _data = null;
            StateHasChanged();

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            try
            {
                var url = $"api/Ventas/purchase-suggestion?days={_dias}&maquinaId={MaquinaId}";
                _data = await Http.GetFromJsonAsync<List<PurchaseSuggestionDto>>(url, ct);
            }
            catch (OperationCanceledException) { /* superseded or disposed */ }
            catch (Exception ex)
            {
                _error = "Error al calcular la sugerencia de compra: " + ex.Message;
                Logger.LogError(ex, "Error cargando purchase-suggestion");
            }
            finally
            {
                _loading = false;
                StateHasChanged();
            }
        }

        private async Task Exportar()
        {
            if (_exporting) return;

            _error = null;
            _exporting = true;
            StateHasChanged();

            try
            {
                var url = $"api/Ventas/purchase-suggestion/export?days={_dias}&maquinaId={MaquinaId}";
                var response = await Http.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsByteArrayAsync();
                    var fileName = $"OrdenCompra_{_dias}d_M{MaquinaId}.xlsx";
                    await JS.InvokeVoidAsync("descargarArchivo", content, fileName);
                }
                else
                {
                    _error = "Error al exportar: " + response.StatusCode;
                }
            }
            catch (Exception ex)
            {
                _error = "Error al exportar: " + ex.Message;
                Logger.LogError(ex, "Error exportando purchase-suggestion");
            }
            finally
            {
                _exporting = false;
                StateHasChanged();
            }
        }

        private void SortBy(string key)
        {
            if (_sortKey == key) _sortDir = _sortDir == "desc" ? "asc" : "desc";
            else { _sortKey = key; _sortDir = key == "prod" ? "asc" : "desc"; }
        }

        private string Caret(string k) => _sortKey == k ? (_sortDir == "desc" ? " ▼" : " ▲") : "";

        // Estilo verde del botón Exportar Excel (equivale al excelBtn.style del template)
        private static readonly Dictionary<string, object> ExcelStyle = new()
        {
            ["style"] = "background:var(--signal-success);color:#fff;border-color:var(--ink-900);"
        };

        // ── Formato (es-CL) ─────────────────────────────────────────────────────────
        private static readonly System.Globalization.NumberFormatInfo Nfi =
            new() { NumberGroupSeparator = ".", NumberDecimalDigits = 0 };
        private static string Num(double n) => Math.Round(n).ToString("N0", Nfi);

        // ── Máquina select ──────────────────────────────────────────────────────────
        private List<VmSelect.VmSelectOption> MaquinaOptions =>
            new List<VmSelect.VmSelectOption> { new("0", ":: Consolidado global ::") }
                .Concat((_maquinas ?? new()).Select(m =>
                    new VmSelect.VmSelectOption(m.Id.ToString(), m.Nombre)))
                .ToList();

        // ── Header counters ─────────────────────────────────────────────────────────
        private int UniversoN => _data?.Count ?? 0;
        private int MachinesN => _maquinas?.Count ?? 0;

        // ── Universe (filtros de máquina) ────────────────────────────────────────────
        private IEnumerable<PurchaseSuggestionDto> Universe =>
            (_data ?? new()).Where(r => !_soloMaq || r.EnMaquina);

        // ── KPIs ─────────────────────────────────────────────────────────────────────
        private int KReponer => Universe.Count(r => r.CantidadSugerida > 0);
        private string KUnidades => Num(Universe.Where(r => r.CantidadSugerida > 0).Sum(r => r.CantidadSugerida));
        private int KCubiertos => Universe.Count(r => r.CantidadSugerida == 0);

        // ── Tabla (filtros + orden) ──────────────────────────────────────────────────
        private List<PurchaseSuggestionDto> View
        {
            get
            {
                var view = Universe;
                if (_soloSug) view = view.Where(r => r.CantidadSugerida > 0);

                var dir = _sortDir == "asc" ? 1 : -1;
                Comparison<PurchaseSuggestionDto> cmp = _sortKey switch
                {
                    "prod" => (a, b) => string.Compare(a.NombreProducto, b.NombreProducto, StringComparison.Ordinal) * dir,
                    "ventas" => (a, b) => (a.VentasUltimos30Dias - b.VentasUltimos30Dias) * dir,
                    "stockmaq" => (a, b) => (a.StockActualMaquinas - b.StockActualMaquinas) * dir,
                    "bodega" => (a, b) => (a.StockBodega - b.StockBodega) * dir,
                    _ => (a, b) => (a.CantidadSugerida - b.CantidadSugerida) * dir,
                };
                var list = view.ToList();
                list.Sort(cmp);
                return list;
            }
        }

        private List<RowVm> Rows => View.Select(r => new RowVm(
            r.NombreProducto, r.EnMaquina, Num(r.VentasUltimos30Dias),
            Num(r.StockActualMaquinas), Num(r.StockBodega),
            Num(r.CantidadSugerida), r.CantidadSugerida > 0)).ToList();

        private int RowReponer => View.Count(r => r.CantidadSugerida > 0);
        private string TotUnidades => Num(View.Sum(r => r.CantidadSugerida));

        // ── View models / DTOs locales ───────────────────────────────────────────────
        private record RowVm(string Prod, bool EnMaq, string Ventas, string StockMaq, string Bodega, string Sug, bool SugPos);

        public class MaquinaSimpleDto
        {
            public int Id { get; set; }
            public string Nombre { get; set; } = string.Empty;
        }

        public class PurchaseSuggestionDto
        {
            public int ProductoId { get; set; }
            public string NombreProducto { get; set; } = string.Empty;
            public int VentasUltimos30Dias { get; set; }
            public int StockActualMaquinas { get; set; }
            public int StockBodega { get; set; }
            public int CantidadSugerida { get; set; }
            public bool EnMaquina { get; set; }
        }
    }
}
