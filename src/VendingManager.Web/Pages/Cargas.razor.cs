using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;

namespace VendingManager.Web.Pages
{
    public partial class Cargas : ComponentBase
    {
        [Inject] protected HttpClient Http { get; set; } = default!;
        [Inject] protected IJSRuntime JS { get; set; } = default!;
        [Inject] protected NavigationManager NavManager { get; set; } = default!;
        [Inject] protected ILogger<Cargas> Logger { get; set; } = default!;

        [SupplyParameterFromQuery(Name = "maquinaId")]
        public int? QueryMaquinaId { get; set; }

        private List<MaquinaDto>? ListaMaquinas;
        private int MaquinaActualId = 0;
        private string Mensaje = "";
        private string AlertClass = "alert-info";

        private bool CargandoSugerencia = false;
        private List<SugerenciaItemDto>? SugerenciaItems;
        private List<OrdenCargaDto>? HistorialOrdenes;
        private List<OrdenCargaDto>? HistorialGlobal;

        // Modal Finalizar
        private bool MostrarModalFinalizar = false;
        private bool EsModoLectura = false;
        private OrdenCargaDto? OrdenSeleccionada;

        protected override async Task OnInitializedAsync()
        {
            try { 
                ListaMaquinas = await Http.GetFromJsonAsync<List<MaquinaDto>>("api/Ventas/lista-maquinas"); 

                if (QueryMaquinaId.HasValue && QueryMaquinaId.Value > 0)
                {
                    MaquinaActualId = QueryMaquinaId.Value;
                    await CargarDatos();
                }
                else
                {
                    // Load global history by default
                    await CargarHistorialGlobal();
                }
            } catch { }
        }

        private async Task CambiarMaquina(ChangeEventArgs e)
        {
            if (int.TryParse(e.Value?.ToString(), out int id))
            {
                MaquinaActualId = id;
                if (MaquinaActualId > 0)
                {
                    await CargarDatos();
                }
                else
                {
                    await CargarHistorialGlobal();
                }
            }
        }

        private async Task CargarHistorialGlobal()
        {
            try
            {
                HistorialGlobal = await Http.GetFromJsonAsync<List<OrdenCargaDto>>("api/OrdenCarga/historial?maquinaId=0");
            }
            catch (Exception ex)
            {
                Mensaje = "Error cargando historial global: " + ex.Message;
                AlertClass = "alert-danger";
            }
        }

        private async Task CargarDatos()
        {
            CargandoSugerencia = true;
            try
            {
                var rawSugerencias = await Http.GetFromJsonAsync<List<StockCriticoDto>>($"api/OrdenCarga/sugerencia?maquinaId={MaquinaActualId}");
                if (rawSugerencias != null)
                {
                    SugerenciaItems = rawSugerencias.Select(x => new SugerenciaItemDto
                    {
                        ProductoId = ObtenerProductoId(x), 
                        SlotId = x.SlotId,
                        Producto = x.Producto,
                        NumeroSlot = x.NumeroSlot,
                        StockActual = x.StockActual,
                        CapacidadMaxima = x.CapacidadMaxima,
                        CantidadSolicitada = Math.Max(0, x.CapacidadMaxima - x.StockActual) 
                    }).ToList();
                }

                HistorialOrdenes = await Http.GetFromJsonAsync<List<OrdenCargaDto>>($"api/OrdenCarga/historial?maquinaId={MaquinaActualId}");
            }
            catch(Exception ex) 
            { 
                Mensaje = "Error cargando datos: " + ex.Message; 
                AlertClass = "alert-danger";
            }
            finally { CargandoSugerencia = false; }
        }

        private int ObtenerProductoId(StockCriticoDto dto) => dto.ProductoId; 


        private async Task GenerarOrden()
        {
            if (SugerenciaItems == null) return;
            var itemsToOrder = SugerenciaItems.Where(x => x.CantidadSolicitada > 0).ToList();
            if(!itemsToOrder.Any()) return;

            var dto = new CrearOrdenDto
            {
                MaquinaId = MaquinaActualId,
                Items = itemsToOrder.Select(x => new DetalleOrdenCargaItemDto
                {
                    ProductoId = x.ProductoId,
                    Cantidad = x.CantidadSolicitada
                }).ToList()
            };

            try
            {
                var resp = await Http.PostAsJsonAsync("api/OrdenCarga", dto);
                if (resp.IsSuccessStatusCode)
                {
                    Mensaje = "Orden generada correctamente. Inventario descontado.";
                    AlertClass = "alert-success";
                    await CargarDatos(); // Refresh
                }
                else
                {
                    Mensaje = "Error al crear orden: " + await resp.Content.ReadAsStringAsync();
                    AlertClass = "alert-danger";
                }
            }
            catch(Exception ex)
            {
                 Mensaje = "Error crítico: " + ex.Message;
                 AlertClass = "alert-danger";
            }
        }

        private void ExportarReporte()
        {
            if (MaquinaActualId > 0)
                NavManager.NavigateTo($"api/OrdenCarga/exportar-sugerencia?maquinaId={MaquinaActualId}", true);
            else
                NavManager.NavigateTo("api/OrdenCarga/exportar-consolidado", true);
        }

        private async Task AbrirFinalizar(OrdenCargaDto orden)
        {
            await JS.InvokeVoidAsync("modalScrollLock.lock");
            OrdenSeleccionada = orden;
            EsModoLectura = false;
            MostrarModalFinalizar = true;
        }

        private async Task AbrirDetalle(OrdenCargaDto orden)
        {
            await JS.InvokeVoidAsync("modalScrollLock.lock");
            OrdenSeleccionada = orden;
            EsModoLectura = true;
            MostrarModalFinalizar = true;
        }

        private async Task CerrarModal()
        {
            await JS.InvokeVoidAsync("modalScrollLock.unlock");
            MostrarModalFinalizar = false;
            OrdenSeleccionada = null;
        }

        // Modal Manual
        private bool MostrarModalManual = false;
        private int MaquinaManualId = 0;
        private string NombreManual = ""; // New Variable
        private DateTime FechaManual = DateTime.Now;
        private bool CargarSugerenciaManual = false;

        private async Task CerrarModalManual()
        {
            await JS.InvokeVoidAsync("modalScrollLock.unlock");
            MostrarModalManual = false;
        }

        private async Task AbrirModalManual()
        {
            await JS.InvokeVoidAsync("modalScrollLock.lock");
            MaquinaManualId = MaquinaActualId > 0 ? MaquinaActualId : 0;
            NombreManual = ""; // Reset
            FechaManual = DateTime.Now;
            CargarSugerenciaManual = false;
            MostrarModalManual = true;
        }

        private async Task CrearOrdenManual()
        {
            if (MaquinaManualId == 0) return;

            // BULK CREATION LOGIC (Consolidated)
            if (MaquinaManualId == -1)
            {
                if (ListaMaquinas == null || !ListaMaquinas.Any()) return;

                var consolidatedItems = new List<DetalleOrdenCargaItemDto>();

                // If Suggestion is required, fetch for ALL machines
                if (CargarSugerenciaManual)
                {
                    foreach(var maquina in ListaMaquinas)
                    {
                        try 
                        {
                            var rawSugerencias = await Http.GetFromJsonAsync<List<StockCriticoDto>>($"api/OrdenCarga/sugerencia?maquinaId={maquina.Id}");
                            if (rawSugerencias != null)
                            {
                                var itemsMaquina = rawSugerencias
                                    .Where(x => (x.CapacidadMaxima - x.StockActual) > 0)
                                    .Select(x => new DetalleOrdenCargaItemDto
                                    {
                                        ProductoId = x.ProductoId,
                                        Cantidad = x.CapacidadMaxima - x.StockActual,
                                        MaquinaId = maquina.Id // Tag item with machine
                                    }).ToList();

                                consolidatedItems.AddRange(itemsMaquina);
                            }
                        }
                        catch { /* Continue if one machine fails? */ }
                    }
                }

                // Create GLOBAL order (Null MaquinaId)
                var dto = new CrearOrdenDto
                {
                    MaquinaId = null, // Global
                    Nombre = NombreManual, // Pass Name
                    Items = consolidatedItems,
                    Fecha = FechaManual,
                    IgnorarStock = CargarSugerenciaManual
                };

                try
                {
                    var resp = await Http.PostAsJsonAsync("api/OrdenCarga", dto);
                    if (resp.IsSuccessStatusCode)
                    {
                        Mensaje = $"Ruta Global '{(string.IsNullOrEmpty(NombreManual) ? "Sin Nombre" : NombreManual)}' creada exitosamente con {consolidatedItems.Count} items.";
                        AlertClass = "alert-success";
                        MostrarModalManual = false;
                        await CargarHistorialGlobal();
                    }
                    else
                    {
                        Mensaje = "Error al crear ruta global: " + await resp.Content.ReadAsStringAsync();
                        AlertClass = "alert-danger";
                    }
                }
                catch(Exception ex)
                {
                     Mensaje = "Error crítico: " + ex.Message;
                     AlertClass = "alert-danger";
                }
                return;
            }

            // SINGLE CREATION LOGIC
            if (await CrearOrdenInterna(MaquinaManualId))
            {
                Mensaje = "Orden Manual creada correctamente. " + (CargarSugerenciaManual ? "(Stock Ignorado)" : "");
                AlertClass = "alert-success";
                MostrarModalManual = false;

                if (MaquinaActualId > 0 && MaquinaActualId == MaquinaManualId)
                    await CargarDatos();
                else if (MaquinaActualId == 0)
                    await CargarHistorialGlobal();
            }
        }

        private async Task<bool> CrearOrdenInterna(int maquinaId)
        {
            var items = new List<DetalleOrdenCargaItemDto>();

            if (CargarSugerenciaManual)
            {
                try 
                {
                    var rawSugerencias = await Http.GetFromJsonAsync<List<StockCriticoDto>>($"api/OrdenCarga/sugerencia?maquinaId={maquinaId}");
                    if (rawSugerencias != null)
                    {
                        items = rawSugerencias
                            .Where(x => (x.CapacidadMaxima - x.StockActual) > 0)
                            .Select(x => new DetalleOrdenCargaItemDto
                            {
                                ProductoId = x.ProductoId,
                                Cantidad = x.CapacidadMaxima - x.StockActual
                            }).ToList();
                    }
                }
                catch (Exception ex)
                {
                     if(MaquinaManualId != -1) // Only show specific error if single mode
                     {
                         Mensaje = "Error obteniendo sugerencia: " + ex.Message;
                         AlertClass = "alert-danger";
                     }
                     return false;
                }
            }

            var dto = new CrearOrdenDto
            {
                MaquinaId = maquinaId,
                Nombre = NombreManual, // Pass Name
                Items = items,
                Fecha = FechaManual,
                IgnorarStock = CargarSugerenciaManual
            };

            try
            {
                var resp = await Http.PostAsJsonAsync("api/OrdenCarga", dto);
                if (!resp.IsSuccessStatusCode)
                {
                    if(MaquinaManualId != -1)
                    {
                        Mensaje = "Error al crear orden: " + await resp.Content.ReadAsStringAsync();
                        AlertClass = "alert-danger";
                    }
                    return false;
                }
                return true;
            }
            catch(Exception ex)
            {
                 if(MaquinaManualId != -1)
                 {
                     Mensaje = "Error crítico: " + ex.Message;
                     AlertClass = "alert-danger";
                 }
                 return false;
            }
        }

        private async Task ConfirmarBorrador(OrdenCargaDto orden)
        {
            try
            {
                var resp = await Http.PostAsync($"api/OrdenCarga/{orden.Id}/confirmar", null);
                if (resp.IsSuccessStatusCode)
                {
                    Mensaje = $"Orden #{orden.Id} confirmada. Stock descontado.";
                    AlertClass = "alert-success";
                    // Refresh the current view
                    if (MaquinaActualId > 0)
                        await CargarDatos();
                    else
                        await CargarHistorialGlobal();
                }
                else
                {
                    Mensaje = "Error al confirmar: " + await resp.Content.ReadAsStringAsync();
                    AlertClass = "alert-danger";
                }
            }
            catch (Exception ex)
            {
                Mensaje = "Error crítico: " + ex.Message;
                AlertClass = "alert-danger";
            }
        }

        private async Task ConfirmarFinalizacion()
        {
            if (OrdenSeleccionada == null) return;

            var dto = new FinalizarOrdenDto
            {
                OrdenId = OrdenSeleccionada.Id,
                Retornos = OrdenSeleccionada.Detalles.Select(d => new DetalleOrdenRetornoDto
                {
                    DetalleId = d.Id,
                    CantidadRetornada = d.CantidadRetornada
                }).ToList()
            };

            try
            {
                var resp = await Http.PostAsJsonAsync("api/OrdenCarga/finalizar", dto);
                 if (resp.IsSuccessStatusCode)
                {
                    Mensaje = "Orden finalizada. Stock retornado a bodega.";
                    AlertClass = "alert-success";
                    await CerrarModal();
                    await CargarDatos(); // Refresh
                }
                 else
                {
                    Mensaje = "Error al finalizar: " + await resp.Content.ReadAsStringAsync();
                    AlertClass = "alert-danger";
                }
            }
            catch(Exception ex)
            {
                 Mensaje = "Error crítico: " + ex.Message;
                 AlertClass = "alert-danger";
            }
        }

        private async Task GuardarEdicionOrden()
        {
            if (OrdenSeleccionada == null) return;

            var dto = new VendingManager.Shared.DTOs.ActualizarOrdenRequestDto
            {
                Nombre = OrdenSeleccionada.Nombre,
                FechaCreacion = OrdenSeleccionada.FechaCreacion
            };

            try
            {
                var resp = await Http.PutAsJsonAsync($"api/OrdenCarga/{OrdenSeleccionada.Id}", dto);

                if (resp.IsSuccessStatusCode)
                {
                    // Refresh list
                    if (MaquinaActualId > 0) await CargarDatos();
                    else await CargarHistorialGlobal();
                }
                else
                {
                    Mensaje = "Error al guardar cambios: " + await resp.Content.ReadAsStringAsync();
                    AlertClass = "alert-danger";
                }
            }
            catch(Exception ex)
            {
                 Mensaje = "Error: " + ex.Message;
                 AlertClass = "alert-danger";
            }
        }


        // DTOS LOCALES (Mirroring Backend)
        public class MaquinaDto { public int Id { get; set; } public string Nombre { get; set; } = ""; }

        public class StockCriticoDto
        {
            public int SlotId { get; set; }
            public string Maquina { get; set; } = "";
            public string IdInternoMaquina { get; set; } = "";
            public string NumeroSlot { get; set; } = "";
            public string Producto { get; set; } = "";
            public int ProductoId { get; set; } 
            public int StockActual { get; set; }
            public int CapacidadMaxima { get; set; }
        }

        public class SugerenciaItemDto
        {
            public int SlotId { get; set; }
            public int ProductoId { get; set; }
            public string Producto { get; set; } = "";
            public string NumeroSlot { get; set; } = "";
            public int StockActual { get; set; }
            public int CapacidadMaxima { get; set; }
            public int CantidadSolicitada { get; set; }
        }

        public class CrearOrdenDto
        {
            public string? Nombre { get; set; } // Added
            public int? MaquinaId { get; set; }
            public List<DetalleOrdenCargaItemDto> Items { get; set; } = new();
            public DateTime? Fecha { get; set; }
            public bool IgnorarStock { get; set; }
        }
        public class DetalleOrdenCargaItemDto 
        { 
            public int ProductoId { get; set; } 
            public int Cantidad { get; set; } 
            public int? MaquinaId { get; set; } // Required if OrdenCarga.MaquinaId is null
        }

        public class OrdenCargaDto
        {
            public int Id { get; set; }
            public string? Nombre { get; set; } // Added
            public DateTime FechaCreacion { get; set; }
            public string Estado { get; set; } = "";
            public int? MaquinaId { get; set; } // Added nullable
            public string MaquinaNombre { get; set; } = "";
            public decimal CostoTotal { get; set; }
            public List<DetalleOrdenDisplayDto> Detalles { get; set; } = new();
        }
        public class DetalleOrdenDisplayDto
        {
            public int Id { get; set; }
            public int ProductoId { get; set; }
            public string ProductoNombre { get; set; } = "";
            public int CantidadSolicitada { get; set; }
            public int CantidadRetornada { get; set; }
            public decimal CostoUnitario { get; set; }
            public int? MaquinaId { get; set; } // Added
            public string MaquinaNombre { get; set; } = ""; // Added
        }

        public class FinalizarOrdenDto
        {
            public int OrdenId { get; set; }
            public List<DetalleOrdenRetornoDto> Retornos { get; set; } = new();
        }
        public class DetalleOrdenRetornoDto { public int DetalleId { get; set; } public int CantidadRetornada { get; set; } }

    }
}

