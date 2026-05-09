using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.WebUtilities;

namespace VendingManager.Web.Pages
{
    public partial class Reportes : ComponentBase
    {
        [Inject] protected HttpClient Http { get; set; } = default!;
        [Inject] protected IJSRuntime JS { get; set; } = default!;
        [Inject] protected ILogger<Reportes> Logger { get; set; } = default!;
        [Inject] protected NavigationManager NavManager { get; set; } = default!;

        // --- VARIABLES DE ESTADO ---
        private bool Cargando = false;
        private string MensajeError = "";
        private bool MostrarModalFinanciero = false;
        private bool MostrarModalFantasmas = false; // Nuevo Modal
        private bool MostrarFantasmas = false; // Checkbox Filtro (para tabla principal)
        private bool Sincronizando = false;
        private bool MostrarModalSync = false;
        private DateTime FechaLimiteSync = DateTime.Now;
        private bool MostrarModalFiltroProductos = false;

        // --- ADMIN ---
        private bool MostrarModalAdmin = false;
        private int AdminMaquinaId = 0;
        private DateTime AdminFechaInicio = DateTime.Now.Date;
        private DateTime AdminFechaFin = DateTime.Now.Date.AddDays(1).AddTicks(-1);
        private string AdminConfirmacion = "";
        private bool ProcesandoAdmin = false;

        // --- VARIABLES DE DATOS ---
        private DateTime FechaInicio = DateTime.Today.AddDays(-30);
        private DateTime FechaFin = DateTime.Today;
        private int MaquinaSeleccionada = 0;

        private ReporteDto? Datos;
        private InformeFinancieroDto? Financiero;
        private List<MaquinaSimpleDto>? ListaMaquinas;
        private List<TemplateRecargaDto>? ListaTemplates;

        // --- TEMPLATE ---
        private int TemplateSeleccionado = 0;

        // --- CONTROL DE ORDEN ---
        private string ColumnaOrden = "Fecha";
        private bool Ascendente = false;
        private string FiltroProducto = "";

        // --- FILTRO DE EXCLUSIÓN DE PRODUCTOS ---
        private HashSet<string> ProductosExcluidos = new();
        private List<string> ProductosUnicos => (Datos?.Detalle ?? new List<DetalleVentaDto>())
            .Select(d => d.Producto ?? "")
            .Distinct()
            .ToList();

        // --- PROPIEDAD CALCULADA PARA FILTRO DE PRODUCTOS ---
        private List<DetalleVentaDto> DetalleFiltrado
        {
            get
            {
                var lista = Datos?.Detalle ?? new List<DetalleVentaDto>();

                // Filtro por texto
                if (!string.IsNullOrWhiteSpace(FiltroProducto))
                {
                    lista = lista.Where(d => d.Producto?.Contains(FiltroProducto, StringComparison.OrdinalIgnoreCase) ?? false).ToList();
                }

                // Filtro por exclusión
                if (ProductosExcluidos.Any())
                {
                    lista = lista.Where(d => !ProductosExcluidos.Contains(d.Producto ?? "")).ToList();
                }

                return lista;
            }
        }

        // --- MÉTODOS FILTRO DE PRODUCTOS ---
        private void AbrirModalFiltroProductos() => MostrarModalFiltroProductos = true;
        private void CerrarModalFiltroProductos() => MostrarModalFiltroProductos = false;

        private void ToggleProducto(string producto)
        {
            if (ProductosExcluidos.Contains(producto))
                ProductosExcluidos.Remove(producto);
            else
                ProductosExcluidos.Add(producto);
        }

        private void SeleccionarTodosProductos() => ProductosExcluidos.Clear();

        private void DeseleccionarTodosProductos()
        {
            ProductosExcluidos = ProductosUnicos.ToHashSet();
        }

        private void ExcluirProductosVacios()
        {
            foreach (var p in ProductosUnicos.Where(p => string.IsNullOrWhiteSpace(p)))
            {
                ProductosExcluidos.Add(p);
            }
        }

        // --- ADMIN METHODS ---
        private void AbrirModalAdmin() 
        {
            MostrarModalAdmin = true;
            AdminMaquinaId = MaquinaSeleccionada; // Default to current selection
            AdminConfirmacion = "";
        }
        private void CerrarModalAdmin() => MostrarModalAdmin = false;

        private async Task EjecutarBorradoAdmin()
        {
            if (AdminMaquinaId == 0) return;
            if (AdminConfirmacion != "BORRAR") return;

            ProcesandoAdmin = true;
            try
            {
                string inicio = AdminFechaInicio.ToString("yyyy-MM-ddTHH:mm:ss");
                string fin = AdminFechaFin.ToString("yyyy-MM-ddTHH:mm:ss");

                var response = await Http.DeleteAsync($"api/Ventas/borrar-rango?inicio={inicio}&fin={fin}&maquinaId={AdminMaquinaId}");

                if (response.IsSuccessStatusCode)
                {
                    var msg = await response.Content.ReadAsStringAsync();
                    MensajeError = ""; // Clear errors
                    // Show success via JS or just reload (simple approach: reload report if it matches)
                    await CargarReporte(); // Refresh data
                    CerrarModalAdmin();
                    await JS.InvokeVoidAsync("alert", msg); // Simple alert for admin success
                }
                else
                {
                    string err = await response.Content.ReadAsStringAsync();
                    MensajeError = "ERROR ADMIN: " + err;
                }
            }
            catch (Exception ex)
            {
                MensajeError = "EXCEPCIÓN ADMIN: " + ex.Message;
            }
            finally
            {
                ProcesandoAdmin = false;
            }
        }

        // --- MÉTODOS ---
        protected override async Task OnInitializedAsync()
        {
            // 1. PROCESAR QUERY PARAMS
            try
            {
                var uri = NavManager.ToAbsoluteUri(NavManager.Uri);
                if (QueryHelpers.ParseQuery(uri.Query).TryGetValue("start", out var startParam))
                {
                    if (DateTime.TryParse(startParam, out DateTime dtStart)) FechaInicio = dtStart;
                }
                if (QueryHelpers.ParseQuery(uri.Query).TryGetValue("end", out var endParam))
                {
                    if (DateTime.TryParse(endParam, out DateTime dtEnd)) FechaFin = dtEnd;
                }
                if (QueryHelpers.ParseQuery(uri.Query).TryGetValue("maquinaId", out var maqParam))
                {
                    if (int.TryParse(maqParam, out int mId)) MaquinaSeleccionada = mId;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error parsing query params");
            }

            // 2. LANZAR PETICIONES EN PARALELO
            var taskMaquinas = CargarMaquinas();
            var taskTemplates = CargarTemplates();
            var taskReporte = CargarReporte();

            await Task.WhenAll(taskMaquinas, taskTemplates, taskReporte);
        }

        private async Task CargarMaquinas()
        {
            try
            {
                ListaMaquinas = await Http.GetFromJsonAsync<List<MaquinaSimpleDto>>("api/Ventas/lista-maquinas");
            }
            catch (Exception ex)
            {
                MensajeError = "ERROR AL CARGAR LISTA DE UNIDADES: " + ex.Message;
            }
        }

        private async Task CargarTemplates()
        {
            try
            {
                ListaTemplates = await Http.GetFromJsonAsync<List<TemplateRecargaDto>>("api/TemplateRecarga");
            }
            catch (Exception ex)
            {
                MensajeError = "ERROR AL CARGAR TEMPLATES: " + ex.Message;
            }
        }

        private async Task CargarReporte()
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
                $"api/Ventas/reporte-rango?inicio={inicio}&fin={fin}&maquinaId={MaquinaSeleccionada}&includePhantom={MostrarFantasmas}&templateId={TemplateSeleccionado}";

                Logger.LogDebug("Reportes: CargarReporte inicio. Url={Url}", url);

                Datos = await Http.GetFromJsonAsync<ReporteDto>(url);

                if (Datos == null)
                {
                    MensajeError = "El servidor no retornó datos válidos.";
                    Logger.LogWarning("Reportes: respuesta nula del servidor para {Url}", url);
                    return;
                }

                AplicarOrdenamiento();
            }
            catch (Exception ex)
            {
                MensajeError = $"ERROR: {ex.Message}";
                Logger.LogError(ex, "Reportes: excepción en CargarReporte");
            }
            finally
            {
                Cargando = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task VerInformeFinanciero()
        {
            try
            {
                string inicio = FechaInicio.ToString("yyyy-MM-dd");
                string fin = FechaFin.ToString("yyyy-MM-dd");
                string url = $"api/Ventas/informe-financiero?inicio={inicio}&fin={fin}&maquinaId={MaquinaSeleccionada}";

                Financiero = await Http.GetFromJsonAsync<InformeFinancieroDto>(url);
                MostrarModalFinanciero = true;
            }
            catch (Exception ex)
            {
                MensajeError = "Error al cargar informe financiero: " + ex.Message;
            }
        }

        private void CerrarModalFinanciero() => MostrarModalFinanciero = false;

        // NUEVO: CONTROL MODAL FANTASMAS
        private void VerFantasmas() => MostrarModalFantasmas = true;
        private void CerrarModalFantasmas() => MostrarModalFantasmas = false;

        private void OrdenarPor(string columna)
        {
            try
            {
                if (ColumnaOrden == columna) Ascendente = !Ascendente;
                else
                {
                    ColumnaOrden = columna;
                    Ascendente = true;
                }
                AplicarOrdenamiento();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Reportes: excepción en OrdenarPor columna={Columna}", columna);
                MensajeError = "ERROR AL ORDENAR: " + ex.Message;
            }
        }

        private void AplicarOrdenamiento()
        {
            if (Datos?.Detalle == null || Datos.Detalle.Count == 0) return;

            switch (ColumnaOrden)
            {
                case "Fecha":
                    Datos.Detalle = Ascendente
                    ? Datos.Detalle.OrderBy(x => x.FechaRaw).ToList()
                    : Datos.Detalle.OrderByDescending(x => x.FechaRaw).ToList();
                    break;
                case "Monto":
                    Datos.Detalle = Ascendente
                    ? Datos.Detalle.OrderBy(x => x.Monto).ToList()
                    : Datos.Detalle.OrderByDescending(x => x.Monto).ToList();
                    break;
                case "Maquina":
                    Datos.Detalle = Ascendente
                    ? Datos.Detalle.OrderBy(x => x.Maquina ?? string.Empty).ToList()
                    : Datos.Detalle.OrderByDescending(x => x.Maquina ?? string.Empty).ToList();
                    break;
                case "Slot":
                    Datos.Detalle = Ascendente
                    ? Datos.Detalle.OrderBy(x => x.Slot).ToList()
                    : Datos.Detalle.OrderByDescending(x => x.Slot).ToList();
                    break;
                case "Estado":
                    Datos.Detalle = Ascendente
                    ? Datos.Detalle.OrderBy(x => x.Estado ?? string.Empty).ToList()
                    : Datos.Detalle.OrderByDescending(x => x.Estado ?? string.Empty).ToList();
                    break;
            }
            StateHasChanged();
        }

        private string RenderIcono(string columna)
        {
            if (ColumnaOrden != columna) return "";
            return Ascendente ? "▲" : "▼";
        }

        // Helper to extract short machine code (last 4 digits, e.g., "0012" from "MAQUINA 2410280012...")
        private string ExtractMachineCode(string? machineName)
        {
            if (string.IsNullOrEmpty(machineName)) return "---";
            var parts = machineName.Replace("MAQUINA", "").Trim().Split(' ');
            if (parts.Length > 0 && parts[0].Length >= 4)
            {
                // Return last 4 digits
                var code = parts[0];
                return code.Substring(Math.Max(0, code.Length - 4));
            }
            return machineName.Length > 6 ? machineName.Substring(0, 6) : machineName;
        }

        private async Task ExportarExcel()
        {
            try
            {
                string inicio = FechaInicio.ToString("yyyy-MM-dd");
                string fin = FechaFin.ToString("yyyy-MM-dd");
                string url =
                $"api/Ventas/exportar?inicio={inicio}&fin={fin}&maquinaId={MaquinaSeleccionada}&includePhantom={MostrarFantasmas}&templateId={TemplateSeleccionado}";

                Logger.LogDebug("Exportando: {Url}", url);

                // Descargar el archivo
                var response = await Http.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsByteArrayAsync();
                    var fileName = $"Reporte_{FechaInicio:ddMMyy}_{FechaFin:ddMMyy}.xlsx";

                    // Usar JS para descargar (soporta byte[] -> Uint8Array)
                    await JS.InvokeVoidAsync("descargarArchivo", content, fileName);
                }
                else
                {
                    MensajeError = "Error al exportar: " + response.StatusCode;
                    Logger.LogError("Error exportando: {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                MensajeError = "Error: " + ex.Message;
                Logger.LogError(ex, "Error en ExportarExcel");
            }
        }

        private void AbrirModalSync()
        {
            FechaLimiteSync = DateTime.Now; // Default: Ahora
            MostrarModalSync = true;
        }

        private void CerrarModalSync() => MostrarModalSync = false;

        private async Task EjecutarSync()
        {
            MostrarModalSync = false;
            await SincronizarPortal();
        }

        private async Task SincronizarPortal()
        {
            if (MaquinaSeleccionada <= 0)
            {
                 // Opcional: Avisar al usuario que será una operación larga
            }

            try
            {
                Sincronizando = true;

                string fechaParam = FechaLimiteSync.ToString("yyyy-MM-ddTHH:mm:ss");
                var response = await Http.PostAsync($"api/Ventas/sync-portal?maquinaId={MaquinaSeleccionada}&fechaLimite={fechaParam}", null);
                if (response.IsSuccessStatusCode)
                {
                    var msg = await response.Content.ReadAsStringAsync();
                    await JS.InvokeVoidAsync("alert", "Sincronización Completada: " + msg);
                    await CargarReporte();
                }
                else
                {
                    MensajeError = "Error al sincronizar: " + await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                MensajeError = "Error crítico de sync: " + ex.Message;
            }
            finally
            {
                Sincronizando = false;
                StateHasChanged();
            }
        }
        // --- CLASES DTO LOCALES (Espejo del Backend) ---
        public class MaquinaSimpleDto
        {
            public int Id { get; set; }
            public string Nombre { get; set; } = string.Empty;
            public string Ubicacion { get; set; } = string.Empty;
        }

        public class ReporteDto
        {
            public int TotalVentas { get; set; }
            public decimal MontoTotal { get; set; }
            public decimal MontoPagado { get; set; }
            public decimal MontoPendiente { get; set; }
            public decimal MontoPhantom { get; set; }

            public decimal GananciaTotal { get; set; }
            public List<DetalleVentaDto> Detalle { get; set; } = new();
            public List<DetalleVentaDto> Fantasmas { get; set; } = new();
        }

        public class TemplateRecargaDto
        {
            public int Id { get; set; }
            public string Nombre { get; set; } = string.Empty;
        }


        public class DetalleVentaDto
        {
            public DateTime FechaRaw { get; set; }
            public string Maquina { get; set; } = string.Empty;
            public decimal Monto { get; set; }
            public string Estado { get; set; } = string.Empty;
            public string Slot { get; set; } = string.Empty;
            public string Producto { get; set; } = string.Empty;
            public decimal Ganancia { get; set; }
        }

        public class InformeFinancieroDto
        {
            public decimal VentasTotales { get; set; }
            public decimal CostoVentas { get; set; }
            public decimal MargenBruto { get; set; }
            public decimal GastosOperativos { get; set; }
            public decimal UtilidadNeta { get; set; }
            public decimal MargenPorcentaje { get; set; }
        }
    }
}

