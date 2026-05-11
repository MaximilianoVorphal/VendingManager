using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace VendingManager.Web.Pages
{
    public partial class StockoutDashboard : ComponentBase
    {
        [Inject] protected HttpClient Http { get; set; } = default!;
        [Inject] protected ILogger<StockoutDashboard> Logger { get; set; } = default!;
        [Inject] protected NavigationManager NavManager { get; set; } = default!;
        [Inject] protected IJSRuntime JS { get; set; } = default!;
        private bool Cargando = false;
        private string MensajeError = "";
        private DateTime FechaInicio = DateTime.Today.AddDays(-7);
        private DateTime FechaFin = DateTime.Today;
        private int MaquinaSeleccionada = 0;
        private double UmbralHoras = 24;
        private bool SoloConQuiebre = true;
        private bool soloDeadSlots = false;
        private bool verAgrupado = true; // Vista agrupada por producto por defecto

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

        // Sorting
        private string ColumnaOrden = "DineroPerdido";
        private bool Ascendente = false;

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
                var filtrados = DatosFiltrados;
                if (filtrados == null || filtrados.Count == 0) return new();

                return filtrados
                    .GroupBy(d => new { d.ProductoId, d.ProductoNombre })
                    .Select(g =>
                    {
                        var todasFechasVentas = g.SelectMany(d => d.FechasVentas).OrderBy(f => f).ToList();
                        var ultimaVenta = todasFechasVentas.LastOrDefault();
                        var stockTotal = g.Sum(d => d.StockInicial);
                        var vendidoTotal = g.Sum(d => d.CantidadVendida);
                        var finPeriodo = g.Max(d => d.FinReporte);
                        double horasSinStock;
                        if (ultimaVenta == default || stockTotal == 0)
                        {
                            horasSinStock = 0;
                        }
                        else
                        {
                            horasSinStock = vendidoTotal >= stockTotal && stockTotal > 0
                                ? (finPeriodo - ultimaVenta).TotalHours
                                : 0.0;
                        }
                        // Si ningún slot vendió nada, no hay quiebre real
                        var posibleQuiebre = vendidoTotal >= 0.8 * stockTotal && stockTotal > 0;

                        return new StockoutProductoDto
                        {
                            ProductoId = g.Key.ProductoId,
                            ProductoNombre = g.Key.ProductoNombre,
                            CantidadTotalSlots = g.Count(),
                            StockInicialTotal = stockTotal,
                            CantidadVendidaTotal = vendidoTotal,
                            PrimeraVenta = g.Min(d => d.PrimeraVenta),
                            UltimaVenta = g.Max(d => d.UltimaVenta),
                            HorasSinStock = Math.Max(0, horasSinStock),
                            DineroPerdidoEstimado = g.Sum(d => d.DineroPerdidoEstimado),
                            GananciaPerdidaEstimada = g.Sum(d => d.GananciaPerdidaEstimada),
                            VelocidadDiaria = g.Average(d => d.VelocidadDiaria),
                            PosibleQuiebre = posibleQuiebre,
                            Maquinas = g.Select(d => d.MaquinaNombre).Distinct().ToList()
                        };
                    })
                    .OrderByDescending(p => p.GananciaPerdidaEstimada)
                    .ToList();
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

            await CargarDatos();
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

            Cargando = true;
            MensajeError = "";
            Datos = null;
            VentasDiarias = null;
            ProductoMaquinaSeleccionado = "";
            StateHasChanged();

            try
            {
                string url = $"api/TemplateRecarga/{TemplateSeleccionado}/analyze?umbralHoras={UmbralHoras}";
                var todosLosDatos = await Http.GetFromJsonAsync<List<StockoutAnalysisDto>>(url);

                // Filtrar por máquina si hay una seleccionada
                if (MaquinaFiltroTemplate > 0 && todosLosDatos != null)
                {
                    Datos = todosLosDatos.Where(d => d.MaquinaId == MaquinaFiltroTemplate).ToList();

                    // Actualizar fechas del timeline basado en la máquina específica
                    var periodoMaquina = TemplateActual?.Periodos.FirstOrDefault(p => p.MaquinaId == MaquinaFiltroTemplate);
                    if (periodoMaquina != null)
                    {
                        FechaInicio = periodoMaquina.FechaInicio;
                        FechaFin = periodoMaquina.FechaFin;
                    }
                }
                else
                {
                    Datos = todosLosDatos;

                    // Actualizar fechas del timeline basado en el template completo
                    if (TemplateActual != null && TemplateActual.Periodos.Any())
                    {
                        FechaInicio = TemplateActual.Periodos.Min(p => p.FechaInicio);
                        FechaFin = TemplateActual.Periodos.Max(p => p.FechaFin);
                    }
                }

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
                Cargando = false;
                StateHasChanged();
            }
        }

        private async Task CargarDatos()
        {
            Cargando = true;
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
                AplicarOrdenamiento();
            }
            catch (Exception ex)
            {
                MensajeError = "Error al cargar análisis: " + ex.Message;
                Logger.LogError(ex, "Error cargando stockout analysis");
            }
            finally
            {
                Cargando = false;
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
                    if (periodo != null) fechaInicio = periodo.FechaInicio; // Fecha + Hora exacta
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

        // View State
        private bool ShowDetailView = false;
        private bool _timelineVisible = false;

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
            if (IsPlaying)
            {
                PlaybackTimer = new System.Threading.Timer(async _ =>
                {
                    if (CurrentStepIndex >= TotalSteps)
                    {
                        IsPlaying = false;
                        await InvokeAsync(StateHasChanged);
                        PlaybackTimer?.Dispose();
                        return;
                    }

                    CurrentStepIndex++;
                    await InvokeAsync(StateHasChanged);
                }, null, 0, 500); // Update every 500ms
            }
            else
            {
                PlaybackTimer?.Dispose();
            }
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
            public double DiasSinStock => HorasSinStock / 24.0;

            public int StockInicial { get; set; }
            public int StockActual { get; set; }
            public int CantidadVendida { get; set; }
            public int FillPct { get; set; } = -1;
            public decimal? DiasHastaStockout { get; set; }
            public bool EsDeadSlot { get; set; }
            public double HorasActivas { get; set; }
            public decimal VelocidadPorHora { get; set; }
            public decimal VelocidadDiaria => VelocidadPorHora * 24;

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
            public DateTime FechaInicio { get; set; }
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
            public double DiasSinStock => HorasSinStock / 24.0;
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



