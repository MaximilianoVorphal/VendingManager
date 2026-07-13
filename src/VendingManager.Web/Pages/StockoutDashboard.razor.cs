using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using VendingManager.Shared.DTOs;
using VendingManager.Web.Shared;

namespace VendingManager.Web.Pages
{
    public partial class StockoutDashboard : ComponentBase
    {
        [Inject] protected HttpClient Http { get; set; } = default!;
        [Inject] protected ILogger<StockoutDashboard> Logger { get; set; } = default!;
        [Inject] protected NavigationManager NavManager { get; set; } = default!;
        [Inject] protected IJSRuntime JS { get; set; } = default!;
        private string MensajeError = "";
        private DateTime FechaInicio = DateTime.Today.AddDays(-7);
        private DateTime FechaFin = DateTime.Today;
        private int MaquinaSeleccionada = 0;
        private double UmbralHoras = 24;
        private bool SoloConQuiebre = true;
        private bool soloDeadSlots = false;

        // Templates
        private int TemplateSeleccionado = 0;
        private int MaquinaFiltroTemplate = 0;
        private List<TemplateRecargaDto>? ListaTemplates;
        private TemplateRecargaDto? TemplateActual;

        // Gráfico de Ventas Diarias
        private string ProductoMaquinaSeleccionado = "";
        private List<VentaDiariaDto>? VentasDiarias;

        private List<MaquinaSimpleDto>? ListaMaquinas;
        private List<StockoutAnalysisDto>? Datos;

        // Memoization for DatosAgrupados (recomputed only when dirty)
        private List<StockoutProductoDto>? _agrupadosCache;
        private bool _agrupadosDirty = true;

        // Track which slots have a timeline load in-flight or completed
        private readonly HashSet<string> _timelineLoadedSlots = new();

        // Sorting
        private string ColumnaOrden = "DineroPerdido";
        private bool Ascendente = false;

        /// <summary>
        /// Returns true when the user has made a valid selection (template or manual filters).
        /// Used to enable/disable the action buttons and show placeholder when nothing selected.
        /// </summary>
        private bool HaySeleccion =>
            (TemplateSeleccionado > 0) ||
            (MaquinaSeleccionada > 0 || FechaInicio != DateTime.Today.AddDays(-7) || FechaFin != DateTime.Today);

        private List<StockoutAnalysisDto> DatosFiltrados =>
            Datos == null ? new() :
            Datos.Where(d =>
                (!SoloConQuiebre || d.PosibleQuiebre) &&
                (!soloDeadSlots || d.EsDeadSlot)
            ).ToList();

        /// <summary>
        /// Datos agrupados por producto (agrega todos los slots del mismo producto).
        /// Se usa en la tabla de alertas cuando verAgrupado = true.
        /// </summary>
        private List<StockoutProductoDto> DatosAgrupados
        {
            get
            {
                // Return cached value when not dirty (memoization optimization)
                if (!_agrupadosDirty && _agrupadosCache != null)
                    return _agrupadosCache;

                // Fuente SIN el filtro de quiebre por slot: agrupamos primero por producto y
                // recién después decidimos el quiebre a nivel producto. Filtrar por
                // d.PosibleQuiebre acá dejaría afuera los slots que aún tienen stock y
                // falsearía los totales del producto (justo lo que queremos evitar).
                var baseDatos = (Datos ?? new())
                    .Where(d => !soloDeadSlots || d.EsDeadSlot)
                    .ToList();
                if (baseDatos.Count == 0) return _agrupadosCache = new();

                _agrupadosCache = baseDatos
                    .GroupBy(d => new { d.ProductoId, d.ProductoNombre })
                    .Select(g =>
                    {
                        var todasFechasVentas = g.SelectMany(d => d.FechasVentas).OrderBy(f => f).ToList();
                        var stockTotal = g.Sum(d => d.StockInicial);
                        var vendidoTotal = g.Sum(d => d.CantidadVendida);
                        var finPeriodo = g.Max(d => d.FinReporte);
                        var primeraVenta = g.Min(d => d.PrimeraVenta);
                        var ultimaVenta = g.Max(d => d.UltimaVenta);

                        var posibleQuiebre = stockTotal > 0 && vendidoTotal >= stockTotal;

                        bool tieneTimeline = vendidoTotal > 0 && todasFechasVentas.Count == vendidoTotal;

                        double horasSinStock;
                        decimal dineroPerdido, gananciaPerdida, velocidadDiaria;

                        if (tieneTimeline)
                        {
                            var fechaAgotamiento = posibleQuiebre
                                ? todasFechasVentas[stockTotal - 1]
                                : (DateTime?)null;
                            horasSinStock = fechaAgotamiento.HasValue
                                ? Math.Max(0, (finPeriodo - fechaAgotamiento.Value).TotalHours)
                                : 0;

                            var conVenta = g.Where(d => d.CantidadVendida > 0).ToList();
                            var unidades = conVenta.Sum(d => d.CantidadVendida);
                            decimal precioPromedio = unidades > 0
                                ? conVenta.Sum(d => d.PrecioPromedioVenta * d.CantidadVendida) / unidades
                                : 0;
                            decimal gananciaPromedio = unidades > 0
                                ? conVenta.Sum(d => d.GananciaPromedio * d.CantidadVendida) / unidades
                                : 0;

                            var finVentana = fechaAgotamiento ?? ultimaVenta;
                            double horasActivas = (primeraVenta.HasValue && finVentana.HasValue)
                                ? Math.Max(1, (finVentana.Value - primeraVenta.Value).TotalHours)
                                : 1;
                            decimal velocidadPorHora = vendidoTotal / (decimal)horasActivas;
                            velocidadDiaria = Math.Round(velocidadPorHora * 24, 1);

                            if (posibleQuiebre && horasSinStock > 0 && precioPromedio > 0)
                            {
                                dineroPerdido = Math.Round(velocidadPorHora * (decimal)horasSinStock * precioPromedio, 0);
                                gananciaPerdida = Math.Round(velocidadPorHora * (decimal)horasSinStock * gananciaPromedio, 0);
                            }
                            else
                            {
                                dineroPerdido = 0;
                                gananciaPerdida = 0;
                            }
                        }
                        else
                        {
                            horasSinStock = posibleQuiebre ? g.Max(d => d.HorasSinStock) : 0;
                            dineroPerdido = g.Sum(d => d.DineroPerdidoEstimado);
                            gananciaPerdida = g.Sum(d => d.GananciaPerdidaEstimada);
                            velocidadDiaria = Math.Round(g.Average(d => d.VelocidadDiaria), 1);
                        }

                        return new StockoutProductoDto
                        {
                            ProductoId = g.Key.ProductoId,
                            ProductoNombre = g.Key.ProductoNombre,
                            CantidadTotalSlots = g.Count(),
                            StockInicialTotal = stockTotal,
                            CantidadVendidaTotal = vendidoTotal,
                            PrimeraVenta = primeraVenta,
                            UltimaVenta = ultimaVenta,
                            HorasSinStock = horasSinStock,
                            DineroPerdidoEstimado = dineroPerdido,
                            GananciaPerdidaEstimada = gananciaPerdida,
                            VelocidadDiaria = velocidadDiaria,
                            PosibleQuiebre = posibleQuiebre,
                            Maquinas = g.Select(d => d.MaquinaNombre).Distinct().ToList()
                        };
                    })
                    .Where(p => !SoloConQuiebre || soloDeadSlots || p.PosibleQuiebre)
                    .OrderByDescending(p => p.GananciaPerdidaEstimada)
                    .ToList();
                _agrupadosDirty = false;
                return _agrupadosCache;
            }
        }

        // ============================================================================
        // v3 markup adapter — bridges the already-wired pipeline (Datos / DatosAgrupados,
        // loaded from api/Ventas/stockout-analysis and api/TemplateRecarga/{id}/analyze)
        // to the industrial-v3 markup view-models. DTOs are NOT reshaped; this layer is
        // pure string/format mapping + selection state.
        // ============================================================================

        private bool verAgrupado = true;
        private string? _sel;

        // ============================================================================
        // Mobile-only UX state (@media ≤520px): bottom-sheets + tapped-row detail.
        // Desktop never sets these; only the mobile markup drives them. No business
        // logic here — the sheets reuse the exact same view-models as the desktop.
        // ============================================================================
        private bool _filterOpen;
        private bool _detailOpen;
        private RowVm? _selRow;
        private void OpenFilter() => _filterOpen = true;
        private void CloseFilter() => _filterOpen = false;
        private void OpenDetail(RowVm r) { _sel = r.Producto; _selRow = r; _detailOpen = true; }
        private void OpenDetailByName(string name)
        {
            _sel = name;
            var r = Rows.FirstOrDefault(x => x.Producto == name);
            if (r != null) { _selRow = r; _detailOpen = true; }
        }
        private void CloseDetail() => _detailOpen = false;

        private string FilterSummary
        {
            get
            {
                var tpl = TemplateSeleccionado <= 0
                    ? "Sin template"
                    : (TemplateOptions.FirstOrDefault(o => o.Value == _template)?.Label ?? "Template");
                var maq = _maquina == "0"
                    ? "Todas las máquinas"
                    : (MaquinaOptions.FirstOrDefault(o => o.Value == _maquina)?.Label ?? "Máquina");
                return $"{tpl} · {maq} · {_umbral}h";
            }
        }
        private string UmbralDiasLabel
        {
            get { var d = Math.Max(1, (int)Math.Round(_umbral / 24.0)); return $"≈ {d} día{(d > 1 ? "s" : "")}"; }
        }

        // Bridge the v3 toggles/selects onto the existing wired filter state so they drive
        // the real DatosFiltrados / DatosAgrupados pipeline.
        private bool _soloDead { get => soloDeadSlots; set => soloDeadSlots = value; }
        private bool _soloQuiebres { get => SoloConQuiebre; set => SoloConQuiebre = value; }
        private bool _agrupar { get => verAgrupado; set => verAgrupado = value; }

        private string _template
        {
            get => TemplateSeleccionado.ToString();
            set { if (int.TryParse(value, out var v) && v != TemplateSeleccionado) { TemplateSeleccionado = v; _ = SeleccionarTemplateYAnalizar(); } }
        }
        private string _maquina
        {
            get => MaquinaFiltroTemplate.ToString();
            set
            {
                if (int.TryParse(value, out var v) && v != MaquinaFiltroTemplate)
                {
                    MaquinaFiltroTemplate = v;
                    if (TemplateSeleccionado > 0) _ = CargarDatosPorTemplate();
                }
            }
        }
        private int _umbral
        {
            get => (int)Math.Round(UmbralHoras);
            set => UmbralHoras = Math.Max(1, value);
        }
        private void OnUmbralChanged(ChangeEventArgs e)
        {
            if (int.TryParse(e.Value?.ToString(), out var v))
            {
                var nuevo = Math.Max(1, v);
                if (Math.Abs(nuevo - UmbralHoras) < 0.5) return;
                UmbralHoras = nuevo;
                if (TemplateSeleccionado > 0) _ = CargarDatosPorTemplate();
            }
        }

        /// <summary>
        /// El diseño de Quiebres no tiene botón "Analizar": elegir un template dispara
        /// el análisis directamente. Carga la metadata del template y luego los datos.
        /// </summary>
        private async Task SeleccionarTemplateYAnalizar()
        {
            await OnTemplateChanged();
            if (TemplateSeleccionado > 0) await CargarDatosPorTemplate();
            else { Datos = null; StateHasChanged(); }
        }

        private List<VmSelect.VmSelectOption> TemplateOptions =>
            new List<VmSelect.VmSelectOption> { new("0", ":: Seleccionar template ::") }
                .Concat((ListaTemplates ?? new()).Select(t =>
                    new VmSelect.VmSelectOption(t.Id.ToString(), $"{t.Nombre} ({t.CantidadMaquinas} máq.)")))
                .ToList();

        /// <summary>
        /// Machine filter dropdown options. When a template is selected, only shows
        /// machines that belong to that template's periods; otherwise shows all machines.
        /// </summary>
        private List<VmSelect.VmSelectOption> MaquinaOptions
        {
            get
            {
                var baseList = new List<VmSelect.VmSelectOption> { new("0", ":: Todas las máquinas ::") };

                if (TemplateSeleccionado > 0 && TemplateActual != null && TemplateActual.Periodos.Any())
                {
                    // Filter to only machines present in the selected template
                    var templateMachineIds = TemplateActual.Periodos
                        .Select(p => p.MaquinaId)
                        .ToHashSet();

                    var filtered = (ListaMaquinas ?? new())
                        .Where(m => templateMachineIds.Contains(m.Id))
                        .Select(m => new VmSelect.VmSelectOption(m.Id.ToString(), m.Nombre));

                    return baseList.Concat(filtered).ToList();
                }

                // No template selected — show all machines
                return baseList
                    .Concat((ListaMaquinas ?? new()).Select(m =>
                        new VmSelect.VmSelectOption(m.Id.ToString(), m.Nombre)))
                    .ToList();
            }
        }

        // Header counters
        private int AnalizadosN => Datos?.Count ?? 0;
        private int MachinesN => Datos?.Select(d => d.MaquinaId).Distinct().Count() ?? 0;
        private int Umbral48 => (int)Math.Round(UmbralHoras);

        // Format (es-CL: dot thousands separator, matches the template)
        private static readonly System.Globalization.NumberFormatInfo Nfi =
            new() { NumberGroupSeparator = ".", NumberDecimalDigits = 0 };
        private static string Money(decimal n) => "$" + Math.Round(n).ToString("N0", Nfi);
        private static string FmtFecha(DateTime? dt) => dt.HasValue ? dt.Value.ToString("dd/MM HH:mm") : "—";

        // View models the markup binds to
        private record RowVm(string Producto, string MaqSlot, int Stock, int Cap, string UltVenta, int Dias, string Vel, string Perdido, string Est);
        private record TlVm(int Rank, string Name, string MaqText, int Pct, string Color, int Dias);
        private record BarVm(string Val, int Hpct, string Col, string ColBg, string Day);

        private static string SevColor(string est) =>
            est == "critico" ? "var(--signal-danger)" : (est == "dead" ? "var(--ink-500)" : "var(--signal-warning)");

        private static string EstadoSlot(StockoutAnalysisDto d) =>
            d.EsDeadSlot ? "dead" : (d.NivelAlerta is "Crítico" or "Alto" ? "critico" : "alerta");
        private static string EstadoProducto(StockoutProductoDto p) =>
            p.NivelAlerta is "Crítico" or "Alto" ? "critico" : "alerta";

        private List<StockoutProductoDto> KpiProductos => Datos == null ? new() : DatosAgrupados;

        private List<RowVm> Rows
        {
            get
            {
                if (Datos == null) return new();
                if (verAgrupado)
                {
                    return DatosAgrupados.Select(p => new RowVm(
                        p.ProductoNombre,
                        $"{p.Maquinas.Count} máq · {p.CantidadTotalSlots} slot{(p.CantidadTotalSlots > 1 ? "s" : "")}",
                        Math.Max(0, p.StockInicialTotal - p.CantidadVendidaTotal),
                        p.StockInicialTotal,
                        FmtFecha(p.UltimaVenta),
                        (int)Math.Round(p.DiasSinStock),
                        p.VelocidadDiaria.ToString("0.0"),
                        p.GananciaPerdidaEstimada > 0 ? Money(p.GananciaPerdidaEstimada) : "—",
                        EstadoProducto(p))).ToList();
                }
                return DatosFiltrados.Select(d => new RowVm(
                    d.ProductoNombre,
                    $"{d.MaquinaNombre} · {d.NumeroSlot}",
                    d.StockActual,
                    d.StockInicial,
                    FmtFecha(d.UltimaVenta),
                    (int)Math.Round(d.DiasSinStock),
                    d.VelocidadDiaria.ToString("0.0"),
                    d.GananciaPerdidaEstimada > 0 ? Money(d.GananciaPerdidaEstimada) : "—",
                    EstadoSlot(d))).ToList();
            }
        }

        // KPIs (grouped by product, over the filtered set)
        private int KCritico => KpiProductos.Count(p => EstadoProducto(p) == "critico");
        private int KAlerta => KpiProductos.Count;
        private string KPerdido => Money(KpiProductos.Sum(p => p.DineroPerdidoEstimado));
        private string KGanancia => Money(KpiProductos.Sum(p => p.GananciaPerdidaEstimada));
        private StockoutProductoDto? Worst =>
            KpiProductos.OrderByDescending(p => p.GananciaPerdidaEstimada).ThenByDescending(p => p.DiasSinStock).FirstOrDefault();
        private string KWorstName => Worst?.ProductoNombre ?? "—";
        private string KWorstMeta => Worst == null ? "—" : $"{Worst.MaquinasResumen} · {(int)Math.Round(Worst.DiasSinStock)} días sin stock";

        private List<TlVm> Timeline
        {
            get
            {
                var prods = KpiProductos;
                if (prods.Count == 0) return new();
                var maxDias = Math.Max(prods.Max(p => p.DiasSinStock), 1);
                return prods.OrderByDescending(p => p.DiasSinStock).Take(6)
                    .Select((p, i) => new TlVm(
                        i + 1, p.ProductoNombre, $"{p.Maquinas.Count} máq",
                        (int)Math.Round(p.DiasSinStock / maxDias * 100),
                        SevColor(EstadoProducto(p)), (int)Math.Round(p.DiasSinStock)))
                    .ToList();
            }
        }

        // Selection + daily bars
        private string SelName
        {
            get
            {
                var names = KpiProductos.Select(p => p.ProductoNombre).ToList();
                if (_sel != null && names.Contains(_sel)) return _sel;
                return names.FirstOrDefault() ?? "—";
            }
        }
        private StockoutProductoDto? SelP => KpiProductos.FirstOrDefault(p => p.ProductoNombre == SelName);
        private int SelDias => (int)Math.Round(SelP?.DiasSinStock ?? 0);
        private string SelPerdido => Money(SelP?.GananciaPerdidaEstimada ?? 0);
        private string SelMeta => SelP == null ? "—" : $"{SelP.Maquinas.Count} máq · {SelP.CantidadTotalSlots} slot{(SelP.CantidadTotalSlots > 1 ? "s" : "")}";
        private List<VmSelect.VmSelectOption> ProdOptions =>
            KpiProductos.Select(p => new VmSelect.VmSelectOption(p.ProductoNombre, p.ProductoNombre)).ToList();

        private List<BarVm> Bars
        {
            get
            {
                var name = SelName;
                var selP = SelP;
                var fechas = (Datos ?? new())
                    .Where(d => d.ProductoNombre == name)
                    .SelectMany(d => d.FechasVentas)
                    .ToList();
                // Ventana = período real del template (FechaInicio→FechaFin, inclusive).
                // Si el período excede maxDays barras legibles, se muestran los últimos
                // maxDays días hasta el fin del período.
                const int maxDays = 31;
                var end = FechaFin.Date;
                var start = FechaInicio.Date;
                int span = Math.Max(1, (int)(end - start).TotalDays + 1);
                int days = Math.Min(span, maxDays);
                var first = span <= maxDays ? start : end.AddDays(-(days - 1));
                var labels = new List<DateTime>();
                for (int i = 0; i < days; i++) labels.Add(first.AddDays(i));
                var counts = labels.Select(day => fechas.Count(f => f.Date == day)).ToList();
                
                int velDia = selP != null ? (int)Math.Round(selP.VelocidadDiaria) : 0;
                var maxV = Math.Max(counts.Count > 0 ? counts.Max() : 1, 1);
                if (velDia > maxV) maxV = Math.Max(maxV, velDia);

                return counts.Select((v, i) =>
                {
                    bool isQuiebre = selP?.UltimaVenta != null && labels[i].Date > selP.UltimaVenta.Value.Date;

                    if (isQuiebre && v == 0 && velDia > 0)
                    {
                        return new BarVm(
                            velDia.ToString(),
                            Math.Max(6, (int)Math.Round((double)velDia / maxV * 100)),
                            "var(--signal-danger)",
                            "rgba(220,53,69,0.08)",
                            labels[i].Day.ToString("D2"));
                    }

                    return new BarVm(
                        v == 0 ? "·" : v.ToString(),
                        v == 0 ? 3 : Math.Max(6, (int)Math.Round((double)v / maxV * 100)),
                        v == 0 ? "var(--signal-danger)" : "var(--ink-900)",
                        v == 0 ? "rgba(220,53,69,0.08)" : "transparent",
                        labels[i].Day.ToString("D2"));
                }).ToList();
            }
        }

        // ============================================================================
        // Mapa de agotamiento — esquema físico de la máquina en foco, alimentado con
        // datos reales (Datos). El panel compacto muestra el estado de hoy; el modal
        // reproduce el vaciado en el tiempo usando FechasVentas (CalculateStockAtTime).
        // ============================================================================

        private bool mapOpen;

        private record SlotVm(string Num, string Prod, int Stock, int Cap, string Color,
            string BadgeTx, string Badge, bool Light, int FillPct, bool Selected);
        private record FloorVm(string Label, List<SlotVm> Slots);

        // Máquina en foco: filtro explícito > máquina del producto seleccionado > peor.
        private int FocusMaqId
        {
            get
            {
                if (MaquinaFiltroTemplate > 0) return MaquinaFiltroTemplate;
                if (Datos == null || Datos.Count == 0) return 0;
                var sel = Datos.FirstOrDefault(d => d.ProductoNombre == SelName);
                if (sel != null) return sel.MaquinaId;
                return Datos.OrderByDescending(d => d.GananciaPerdidaEstimada).First().MaquinaId;
            }
        }

        private string FocusMaqLabel =>
            (Datos ?? new()).FirstOrDefault(d => d.MaquinaId == FocusMaqId)?.MaquinaNombre ?? "—";

        // Solo slots del esquema físico con producto real: se excluyen el bucket
        // "Pendientes" (NumeroSlot vacío) y los slots sin producto / marcados como
        // pendientes o vacíos, que salen del análisis con ProductoId 0 ("Desconocido").
        private List<StockoutAnalysisDto> FocusSlots =>
            (Datos ?? new())
                .Where(d => d.MaquinaId == FocusMaqId
                    && !string.IsNullOrWhiteSpace(d.NumeroSlot)
                    && (d.ProductoId ?? 0) > 0)
                .ToList();

        // StockInicial desconocido (0) => no podemos inferir capacidad; usamos lo que haya.
        private static int SlotCap(StockoutAnalysisDto d) =>
            d.StockInicial > 0 ? d.StockInicial : Math.Max(d.StockActual, 1);

        private static string TrayOf(string? slot)
        {
            if (string.IsNullOrEmpty(slot)) return "#";
            var m = Regex.Match(slot, "^[A-Za-z]+");
            return m.Success ? m.Value.ToUpperInvariant() : "#";
        }

        private static int SlotNumSuffix(string? slot)
        {
            if (string.IsNullOrEmpty(slot)) return 0;
            var m = Regex.Match(slot, @"\d+");
            return m.Success && int.TryParse(m.Value, out var n) ? n : 0;
        }

        private SlotVm MakeSlot(StockoutAnalysisDto d, double ratio)
        {
            var cap = SlotCap(d);
            ratio = Math.Clamp(ratio, 0, 1);
            var stock = (int)Math.Round(ratio * cap);
            string color, badgeTx, badge;
            if (d.EsDeadSlot) { color = "var(--ink-500)"; badgeTx = "#fff"; badge = "D"; }
            else if (ratio <= 0.001) { color = "var(--signal-danger)"; badgeTx = "#fff"; badge = "C"; }
            else if (ratio <= 0.4) { color = "var(--signal-warning)"; badgeTx = "var(--ink-900)"; badge = "A"; }
            else { color = "var(--signal-success)"; badgeTx = "#fff"; badge = ""; }
            var light = ratio > 0.42;
            var fillPct = d.EsDeadSlot ? 4 : Math.Max(5, (int)Math.Round(ratio * 100));
            return new SlotVm(
                string.IsNullOrEmpty(d.NumeroSlot) ? "—" : d.NumeroSlot,
                d.ProductoNombre, stock, cap, color, badgeTx, badge, light, fillPct,
                d.ProductoNombre == SelName);
        }

        // Layout tipo máquina real: se ordenan todos los slots (por bandeja y número)
        // y se reparten en bandejas de tamaño fijo — la primera de 5, el resto de 10 —
        // igual para toda máquina, replicando el esquema del template de diseño.
        private List<FloorVm> BuildFloors(Func<StockoutAnalysisDto, double> ratioFn)
        {
            var ordered = FocusSlots
                .OrderBy(d => TrayOf(d.NumeroSlot))
                .ThenBy(d => SlotNumSuffix(d.NumeroSlot))
                .ToList();

            var floors = new List<FloorVm>();
            for (int idx = 0, floor = 0; idx < ordered.Count; floor++)
            {
                int size = floor == 0 ? 5 : 10;
                var slots = ordered.Skip(idx).Take(size)
                    .Select(d => MakeSlot(d, ratioFn(d))).ToList();
                var label = floor < 26 ? $"Bandeja {(char)('A' + floor)}" : $"Bandeja {floor + 1}";
                floors.Add(new FloorVm(label, slots));
                idx += size;
            }
            return floors;
        }

        private double RatioToday(StockoutAnalysisDto d) =>
            d.EsDeadSlot ? 0 : (double)d.StockActual / SlotCap(d);

        private double RatioAt(StockoutAnalysisDto d, DateTime time) =>
            d.EsDeadSlot ? 0 : (double)CalculateStockAtTime(d, time) / SlotCap(d);

        private List<FloorVm> SchematicFloors => BuildFloors(RatioToday);
        private List<FloorVm> ModalFloors => BuildFloors(d => RatioAt(d, CurrentTime));

        private int EnQuiebreN => FocusSlots.Count(d => d.EsDeadSlot || d.StockActual <= 0);
        private int ModalQuiebreN => FocusSlots.Count(d => d.EsDeadSlot || CalculateStockAtTime(d, CurrentTime) <= 0);
        private string SchematicMeta => $"{FocusSlots.Count} slots · {EnQuiebreN} en quiebre";

        // Scrubber (recarga -> hoy), sobre el CurrentStepIndex / TotalSteps ya existentes.
        private int TotalDays => Math.Max(1, (int)Math.Round((FechaFin - FechaInicio).TotalDays));
        private int ScrubDay => Math.Clamp((int)Math.Round((CurrentTime - FechaInicio).TotalDays), 0, TotalDays);
        private bool ScrubIsToday => CurrentStepIndex >= TotalSteps;
        private string ScrubDateLabel => CurrentTime.ToString("dd/MM");
        private string ScrubDayLabel => ScrubIsToday ? "HOY" : $"Día {ScrubDay} / {TotalDays}";

        private void OpenMap()
        {
            mapOpen = true;
            _ = LoadFocusSlotTimelinesAsync();
        }
        private void CloseMap()
        {
            if (IsPlaying) { IsPlaying = false; PlaybackTimer?.Dispose(); }
            mapOpen = false;
        }
        private void OnScrubInput(ChangeEventArgs e)
        {
            if (IsPlaying) { IsPlaying = false; PlaybackTimer?.Dispose(); }
            if (int.TryParse(e.Value?.ToString(), out var v))
                CurrentStepIndex = Math.Clamp(v, 0, TotalSteps);
            _ = LoadFocusSlotTimelinesAsync();
        }

        /// <summary>
        /// Lazy-loads the sale timeline for a specific slot from the dedicated endpoint.
        /// Called on-demand when the user interacts with the scrubber or opens the map.
        /// </summary>
        private async Task LoadSlotTimelineAsync(int maquinaId, string numeroSlot)
        {
            var key = $"{maquinaId}|{numeroSlot}";
            if (_timelineLoadedSlots.Contains(key)) return;

            if (TemplateSeleccionado == 0) return;

            try
            {
                var encodedSlot = Uri.EscapeDataString(numeroSlot);
                var dto = await Http.GetFromJsonAsync<SlotTimelineDto>(
                    $"api/TemplateRecarga/{TemplateSeleccionado}/slot-timeline?maquinaId={maquinaId}&numeroSlot={encodedSlot}");

                if (dto != null)
                {
                    var targets = Datos?.Where(d => d.MaquinaId == maquinaId && d.NumeroSlot == numeroSlot).ToList();
                    if (targets != null && targets.Count > 0)
                    {
                        foreach (var target in targets)
                        {
                            target.FechasVentas = dto.FechasVentas;
                        }
                        _agrupadosDirty = true;
                    }
                }

                _timelineLoadedSlots.Add(key);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading slot timeline for máquina {MaquinaId} slot {NumeroSlot}",
                    maquinaId, numeroSlot);
                // Slot remains unloaded in _timelineLoadedSlots, so it will be retried
                // on next interaction (scrubber move or map re-open).
            }
            finally
            {
                StateHasChanged();
            }
        }

        /// <summary>
        /// Loads timelines for all focus-machine slots that don't have data yet.
        /// Fire-and-forget: called from OpenMap and OnScrubInput.
        /// </summary>
        private async Task LoadFocusSlotTimelinesAsync()
        {
            if (TemplateSeleccionado == 0 || Datos == null) return;

            var unloaded = FocusSlots
                .Where(d => (d.FechasVentas == null || d.FechasVentas.Count == 0)
                            && !_timelineLoadedSlots.Contains($"{d.MaquinaId}|{d.NumeroSlot}"))
                .Take(5) // Limit batch to avoid burst of HTTP calls
                .ToList();

            foreach (var slot in unloaded)
            {
                await LoadSlotTimelineAsync(slot.MaquinaId, slot.NumeroSlot);
            }
        }

        protected override async Task OnInitializedAsync()
        {
            await CargarMaquinas();
            await CargarTemplates();

            // Leer parámetro templateId de la URL
            var uri = NavManager.ToAbsoluteUri(NavManager.Uri);
            if (QueryHelpers.ParseQuery(uri.Query).TryGetValue("templateId", out var templateIdValue))
            {
                if (int.TryParse(templateIdValue, out int templateId) && templateId > 0)
                {
                    TemplateSeleccionado = templateId;
                    await OnTemplateChanged();
                    await CargarDatosPorTemplate();
                    return;
                }
            }

            // No templateId in URL — do NOT auto-run. User must explicitly click the button.
        }

        private async Task CargarMaquinas()
        {
            try
            {
                ListaMaquinas = await Http.GetFromJsonAsync<List<MaquinaSimpleDto>>("api/Ventas/lista-maquinas");
            }
            catch { }
        }

        private async Task CargarTemplates()
        {
            try
            {
                ListaTemplates = await Http.GetFromJsonAsync<List<TemplateRecargaDto>>("api/TemplateRecarga");
            }
            catch { }
        }

        private async Task OnTemplateChanged()
        {
            TemplateActual = null;
            // Reset machine filter when template changes — the previously selected
            // machine might not belong to the new template.
            MaquinaFiltroTemplate = 0;
            if (TemplateSeleccionado > 0 && ListaTemplates != null)
            {
                TemplateActual = ListaTemplates.FirstOrDefault(t => t.Id == TemplateSeleccionado);
                if (TemplateActual == null)
                {
                    // Cargar desde API si no está en la lista
                    try
                    {
                        TemplateActual = await Http.GetFromJsonAsync<TemplateRecargaDto>($"api/TemplateRecarga/{TemplateSeleccionado}");
                    }
                    catch { }
                }
            }
        }

        private async Task CargarDatosPorTemplate()
        {
            if (TemplateSeleccionado == 0) return;

            MensajeError = "";
            Datos = null;
            VentasDiarias = null;
            ProductoMaquinaSeleccionado = "";
            StateHasChanged();

            try
            {
                string url = $"api/TemplateRecarga/{TemplateSeleccionado}/analyze?umbralHoras={UmbralHoras}";
                var slotDtos = await Http.GetFromJsonAsync<List<StockoutSlotDto>>(url);

                // Map StockoutSlotDto → local StockoutAnalysisDto. FechasVentas ahora viene
                // poblado desde el análisis eager (ventas del período del template).
                var todosLosDatos = slotDtos?.Select(s => new StockoutAnalysisDto
                {
                    MaquinaId = s.MaquinaId,
                    MaquinaNombre = s.MaquinaNombre,
                    ProductoId = s.ProductoId,
                    ProductoNombre = s.ProductoNombre,
                    NumeroSlot = s.NumeroSlot,
                    PrimeraVenta = s.PrimeraVenta,
                    UltimaVenta = s.UltimaVenta,
                    UltimaActividadMaquina = s.UltimaActividadMaquina,
                    FinReporte = s.FinReporte,
                    FechasVentas = s.FechasVentas,
                    PosibleQuiebre = s.PosibleQuiebre,
                    HorasSinStock = s.HorasSinStock,
                    StockInicial = s.StockInicial,
                    StockActual = s.StockActual,
                    CantidadVendida = s.CantidadVendida,
                    FillPct = s.FillPct,
                    DiasHastaStockout = s.DiasHastaStockout,
                    EsDeadSlot = s.EsDeadSlot,
                    HorasActivas = s.HorasActivas,
                    VelocidadPorHora = s.VelocidadPorHora,
                    PrecioPromedioVenta = s.PrecioPromedioVenta,
                    GananciaPromedio = s.GananciaPromedio,
                    DineroPerdidoEstimado = s.DineroPerdidoEstimado,
                    GananciaPerdidaEstimada = s.GananciaPerdidaEstimada,
                }).ToList();

                // Filtrar por máquina si hay una seleccionada
                if (MaquinaFiltroTemplate > 0 && todosLosDatos != null)
                {
                    Datos = todosLosDatos.Where(d => d.MaquinaId == MaquinaFiltroTemplate).ToList();

                    // Actualizar fechas del timeline basado en la máquina específica
                    var periodoMaquina = TemplateActual?.Periodos.FirstOrDefault(p => p.MaquinaId == MaquinaFiltroTemplate);
                    if (periodoMaquina != null)
                    {
                        FechaInicio = periodoMaquina.FechaRecarga;
                        FechaFin = periodoMaquina.FechaFin;
                    }
                }
                else
                {
                    Datos = todosLosDatos;

                    // Actualizar fechas del timeline basado en el template completo
                    if (TemplateActual != null && TemplateActual.Periodos.Any())
                    {
                        FechaInicio = TemplateActual.Periodos.Min(p => p.FechaRecarga);
                        FechaFin = TemplateActual.Periodos.Max(p => p.FechaFin);
                    }
                }

                _agrupadosDirty = true;
                _timelineLoadedSlots.Clear(); // Clear any cached timeline state

                // Initialize timeline
                CurrentStepIndex = 0;
                if (FechaFin > FechaInicio)
                {
                    TotalSteps = (int)(FechaFin - FechaInicio).TotalMinutes / TimeStepMinutes;
                    if (TotalSteps < 1) TotalSteps = 1;
                }

                AplicarOrdenamiento();
            }
            catch (Exception ex)
            {
                MensajeError = "Error al analizar template: " + ex.Message;
                Logger.LogError(ex, "Error analizando template");
                StateHasChanged();
            }
            finally
            {
                StateHasChanged();
            }
        }

        private async Task CargarDatos()
        {
            MensajeError = "";
            Datos = null;
            StateHasChanged();

            try
            {
                string inicio = FechaInicio.ToString("yyyy-MM-dd");
                string fin = FechaFin.ToString("yyyy-MM-dd");
                string url =
                $"api/Ventas/stockout-analysis?inicio={inicio}&fin={fin}&maquinaId={MaquinaSeleccionada}&umbralHoras={UmbralHoras}";

                Datos = await Http.GetFromJsonAsync<List<StockoutAnalysisDto>>(url);
                _agrupadosDirty = true;
                AplicarOrdenamiento();
            }
            catch (Exception ex)
            {
                MensajeError = "Error al cargar análisis: " + ex.Message;
                Logger.LogError(ex, "Error cargando stockout analysis");
            }
        }

        private void OrdenarPor(string col)
        {
            if (ColumnaOrden == col) Ascendente = !Ascendente;
            else { ColumnaOrden = col; Ascendente = false; }

            AplicarOrdenamiento();
        }

        private void AplicarOrdenamiento()
        {
            if (Datos == null) return;

            Datos = ColumnaOrden switch
            {
                "Maquina" => Ascendente ? Datos.OrderBy(x => x.MaquinaNombre).ToList() : Datos.OrderByDescending(x =>
                x.MaquinaNombre).ToList(),
                "Producto" => Ascendente ? Datos.OrderBy(x => x.ProductoNombre).ToList() : Datos.OrderByDescending(x =>
                x.ProductoNombre).ToList(),
                "PrimeraVenta" => Ascendente ? Datos.OrderBy(x => x.PrimeraVenta).ToList() : Datos.OrderByDescending(x =>
                x.PrimeraVenta).ToList(),
                "UltimaVenta" => Ascendente ? Datos.OrderBy(x => x.UltimaVenta).ToList() : Datos.OrderByDescending(x =>
                x.UltimaVenta).ToList(),
                "DiasAgotado" => Ascendente ? Datos.OrderBy(x => x.DiasSinStock).ToList() : Datos.OrderByDescending(x =>
                x.DiasSinStock).ToList(),
                "Velocidad" => Ascendente ? Datos.OrderBy(x => x.VelocidadDiaria).ToList() : Datos.OrderByDescending(x =>
                x.VelocidadDiaria).ToList(),
                "DineroPerdido" => Ascendente ? Datos.OrderBy(x => x.GananciaPerdidaEstimada).ToList() : Datos.OrderByDescending(x =>
                x.GananciaPerdidaEstimada).ToList(),
                _ => Datos
            };
        }

        private string RenderIcono(string col)
        {
            if (ColumnaOrden != col) return "";
            return Ascendente ? "▲" : "▼";
        }

        private async Task CargarVentasDiarias()
        {
            VentasDiarias = null;
            if (string.IsNullOrEmpty(ProductoMaquinaSeleccionado) || Datos == null) return;

            try
            {
                // Parsear el ID compuesto "productoId|maquinaId"
                var partes = ProductoMaquinaSeleccionado.Split('|');
                if (partes.Length != 2) return;

                if (!int.TryParse(partes[0], out int productoId) || !int.TryParse(partes[1], out int maquinaId))
                    return;

                // Obtener info del producto seleccionado
                var productoInfo = Datos.FirstOrDefault(d => d.ProductoId == productoId && d.MaquinaId == maquinaId);
                if (productoInfo == null) return;

                // Usar las fechas del análisis del producto (PrimeraVenta hasta fin del período)
                // Esto asegura que el gráfico coincida con los datos de la tabla de análisis
                // Usar fecha de inicio del periodo si existe (prioridad), sino primera venta, sino fecha global
                DateTime fechaInicio = FechaInicio;
                if (TemplateActual != null)
                {
                    var periodo = TemplateActual.Periodos.FirstOrDefault(p => p.MaquinaId == maquinaId);
                    if (periodo != null) fechaInicio = periodo.FechaRecarga; // Fecha + Hora exacta
                }
                else if (productoInfo.PrimeraVenta.HasValue)
                {
                    fechaInicio = productoInfo.PrimeraVenta.Value.Date;
                }
                DateTime fechaFin;

                // Determinar la fecha fin basada en el período del template o la última venta
                if (TemplateActual != null)
                {
                    // Buscar el período de esta máquina en el template
                    var periodo = TemplateActual.Periodos.FirstOrDefault(p => p.MaquinaId == maquinaId);
                    if (periodo != null)
                    {
                        fechaFin = periodo.FechaFin; // Fecha + Hora exacta
                    }
                    else
                    {
                        fechaFin = FechaFin;
                    }
                }
                else
                {
                    fechaFin = FechaFin;
                }

                // Llamar al endpoint para obtener ventas diarias
                // Usamos formato "s" (sortable) para enviar la hora completa: yyyy-MM-ddTHH:mm:ss
                string url = $"api/Ventas/ventas-diarias?productoId={productoId}&maquinaId={maquinaId}&inicio={fechaInicio:s}&fin={fechaFin:s}";

                var ventasApi = await Http.GetFromJsonAsync<List<VentaDiariaDto>>(url);

                if (ventasApi != null)
                {
                    // Asegurar que todos los días estén representados (incluyendo días sin ventas)
                    VentasDiarias = new List<VentaDiariaDto>();
                    for (var date = fechaInicio.Date; date <= fechaFin.Date; date = date.AddDays(1))
                    {
                        var ventaDia = ventasApi.FirstOrDefault(v => v.Fecha.Date == date.Date);
                        VentasDiarias.Add(new VentaDiariaDto
                        {
                            Fecha = date,
                            Cantidad = ventaDia?.Cantidad ?? 0
                        });
                    }

                    // Renderizar Gráfico
                    // Forzar renderizado para que el elemento <canvas> exista en el DOM
                    StateHasChanged();
                    await Task.Delay(100); // Pequeña pausa para asegurar que el DOM se actualizó
                    await RenderChart();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error cargando ventas diarias");
            }
        }

        private async Task RenderChart()
        {
            if (VentasDiarias == null || !VentasDiarias.Any()) return;

            var labels = VentasDiarias.Select(v => v.Fecha.ToString("dd/MM")).ToArray();
            var data = VentasDiarias.Select(v => v.Cantidad).ToArray();
            var backgroundColors = VentasDiarias.Select(v => v.Cantidad > 0 ? "#198754" : "#dc3545").ToArray(); // Green/Red

            var config = new
            {
                type = "bar",
                data = new
                {
                    labels = labels,
                    datasets = new[]
                    {
                    new
                    {
                        label = "Ventas (Unidades)",
                        data = data,
                        backgroundColor = backgroundColors,
                        borderWidth = 1
                    }
                }
                },
                options = new
                {
                    responsive = true,
                    maintainAspectRatio = false,
                    scales = new
                    {
                        y = new
                        {
                            beginAtZero = true,
                            ticks = new { precision = 0 }
                        }
                    },
                    plugins = new
                    {
                        legend = new { display = false }
                    }
                }
            };

            await JS.InvokeVoidAsync("ChartJsInterop.setupChart", "dailySalesChart", config);
        }

        // Timeline Control
        private int CurrentStepIndex;
        private int TotalSteps;
        private const int TimeStepMinutes = 30;
        private DateTime CurrentTime
        {
            get => FechaInicio.AddMinutes(CurrentStepIndex * TimeStepMinutes);
            set => CurrentStepIndex = (int)((value - FechaInicio).TotalMinutes / TimeStepMinutes);
        }
        private bool IsPlaying = false;
        private System.Threading.Timer? PlaybackTimer;

        // Data
        private int CalculateStockAtTime(StockoutAnalysisDto item, DateTime time)
        {
            // 1. Start with initial stock (or max capacity if unknown/0, but we ideally need initial)
            // If we don't have initial stock snapshot, we can't really know exact level, 
            // but we can try to work backwards from now? No, better to stick to forward if we have snapshot.
            // If StockInicial is 0, maybe we assume it was full? or we display accumulated sales?
            // Let's assume StockInicial is correct if available.

            int soldBeforeTime = 0;
            if (item.FechasVentas != null)
            {
                soldBeforeTime = item.FechasVentas.Count(t => t <= time);
            }

            int stock = item.StockInicial - soldBeforeTime;
            return stock < 0 ? 0 : stock;
        }

        private int ParseSlot(string slot)
        {
            if (int.TryParse(slot, out int n)) return n;
            return 999;
        }

        private void TogglePlayback()
        {
            IsPlaying = !IsPlaying;
            if (!IsPlaying) { PlaybackTimer?.Dispose(); return; }

            // Al llegar al final, reinicia desde la recarga.
            if (CurrentStepIndex >= TotalSteps) CurrentStepIndex = 0;

            // Recorre el período en ~28 cuadros, sin importar cuántos steps tenga.
            var step = Math.Max(1, TotalSteps / 28);
            PlaybackTimer = new System.Threading.Timer(async _ =>
            {
                if (CurrentStepIndex >= TotalSteps)
                {
                    IsPlaying = false;
                    PlaybackTimer?.Dispose();
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                CurrentStepIndex = Math.Min(TotalSteps, CurrentStepIndex + step);
                await InvokeAsync(StateHasChanged);
            }, null, 0, 450);
        }

        // DTOs Locales
        public class MaquinaSimpleDto
        {
            public int Id { get; set; }
            public string Nombre { get; set; } = string.Empty;
        }

        public class StockoutAnalysisDto
        {
            public int MaquinaId { get; set; }
            public string MaquinaNombre { get; set; } = string.Empty;
            public int? ProductoId { get; set; }
            public string ProductoNombre { get; set; } = string.Empty;
            public string NumeroSlot { get; set; } = string.Empty;

            public DateTime? PrimeraVenta { get; set; }
            public DateTime? UltimaVenta { get; set; }
            public DateTime UltimaActividadMaquina { get; set; }
            public DateTime FinReporte { get; set; }

            public List<DateTime> FechasVentas { get; set; } = new();

            public bool PosibleQuiebre { get; set; }
            public double HorasSinStock { get; set; }
            public double DiasSinStock => HorasSinStock / 14.0;

            public int StockInicial { get; set; }
            public int StockActual { get; set; }
            public int CantidadVendida { get; set; }
            public int FillPct { get; set; } = -1;
            public decimal? DiasHastaStockout { get; set; }
            public bool EsDeadSlot { get; set; }
            public double HorasActivas { get; set; }
            public decimal VelocidadPorHora { get; set; }
            public decimal VelocidadDiaria => VelocidadPorHora * VendingManager.Shared.Helpers.HorarioOperativoHelper.HorasOperativasPorDia;

            public decimal PrecioPromedioVenta { get; set; }
            public decimal GananciaPromedio { get; set; }
            public decimal DineroPerdidoEstimado { get; set; }
            public decimal GananciaPerdidaEstimada { get; set; }

            public string NivelAlerta => HorasSinStock switch
            {
                > 72 => "Crítico",
                > 48 => "Alto",
                > 24 => "Medio",
                _ => "Normal"
            };

            public string ColorAlerta => NivelAlerta switch
            {
                "Crítico" => "bg-danger text-white",
                "Alto" => "bg-warning text-dark",
                "Medio" => "bg-info text-dark",
                _ => "bg-light text-muted border"
            };
        }

        public class TemplateRecargaDto
        {
            public int Id { get; set; }
            public string Nombre { get; set; } = string.Empty;
            public string? Descripcion { get; set; }
            public DateTime FechaCreacion { get; set; }
            public List<PeriodoRecargaDto> Periodos { get; set; } = new();
            public int CantidadMaquinas => Periodos.Count;
        }

        public class PeriodoRecargaDto
        {
            public int Id { get; set; }
            public int MaquinaId { get; set; }
            public string MaquinaNombre { get; set; } = string.Empty;
            public string IdInternoMaquina { get; set; } = string.Empty;
            public DateTime FechaRecarga { get; set; }
            public DateTime FechaFin { get; set; }
        }

        public class VentaDiariaDto
        {
            public DateTime Fecha { get; set; }
            public int Cantidad { get; set; }
        }

        /// <summary>
        /// Datos de stockout agrupados por producto (todos los slots combinados).
        /// </summary>
        public class StockoutProductoDto
        {
            public int? ProductoId { get; set; }
            public string ProductoNombre { get; set; } = string.Empty;
            public int CantidadTotalSlots { get; set; }
            public int StockInicialTotal { get; set; }
            public int CantidadVendidaTotal { get; set; }
            public DateTime? PrimeraVenta { get; set; }
            public DateTime? UltimaVenta { get; set; }
            public double HorasSinStock { get; set; }
            public double DiasSinStock => HorasSinStock / 14.0;
            public decimal DineroPerdidoEstimado { get; set; }
            public decimal GananciaPerdidaEstimada { get; set; }
            public decimal VelocidadDiaria { get; set; }
            public bool PosibleQuiebre { get; set; }
            public List<string> Maquinas { get; set; } = new();

            public string MaquinasResumen => Maquinas.Count switch
            {
                0 => "-",
                1 => Maquinas[0],
                <= 3 => string.Join(", ", Maquinas),
                _ => $"{Maquinas.Count} máquinas"
            };

            public string NivelAlerta => HorasSinStock switch
            {
                > 72 => "Crítico",
                > 48 => "Alto",
                > 24 => "Medio",
                _ => "Normal"
            };

            public string ColorAlerta => NivelAlerta switch
            {
                "Crítico" => "bg-danger text-white",
                "Alto" => "bg-warning text-dark",
                "Medio" => "bg-info text-dark",
                _ => "bg-light text-muted border"
            };
        }
    }
}



