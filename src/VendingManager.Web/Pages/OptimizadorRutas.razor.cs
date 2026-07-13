using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using VendingManager.Shared.DTOs;

namespace VendingManager.Web.Pages
{
    public partial class OptimizadorRutas : ComponentBase
    {
        [Inject] protected HttpClient Http { get; set; } = default!;
        [Inject] protected ILogger<OptimizadorRutas> Logger { get; set; } = default!;

        private List<LogisticaZonaDto>? Zonas;
        private LogisticaZonaDto? ZonaSeleccionada;
        private bool Cargando;
        private bool Generando;
        private string Mensaje = "";
        private bool MensajeEsError;

        private int DiasHistorial = 14;
        private int VentanaDias = 3;

        protected override async Task OnInitializedAsync() => await Cargar();

        private async Task Cargar()
        {
            Cargando = true;
            Mensaje = "";
            try
            {
                Zonas = await Http.GetFromJsonAsync<List<LogisticaZonaDto>>(
                    $"api/LogisticaPredictiva/zonas?diasHistorial={DiasHistorial}&ventanaDias={VentanaDias}");

                // Mantener la selección si la zona sigue existiendo; si no, tomar la primera.
                ZonaSeleccionada = Zonas?.FirstOrDefault(z => z.ZonaLogisticaId == ZonaSeleccionada?.ZonaLogisticaId)
                    ?? Zonas?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Mensaje = "Error cargando análisis de zonas: " + ex.Message;
                MensajeEsError = true;
                Logger.LogError(ex, "Error cargando análisis de zonas");
            }
            finally
            {
                Cargando = false;
            }
        }

        private void SeleccionarZona(LogisticaZonaDto zona) => ZonaSeleccionada = zona;

        private static int CriticosDe(LogisticaZonaDto zona) =>
            zona.Maquinas.Sum(m => m.Slots.Count(s => s.EsCritico && s.UnidadesFaltantes > 0));

        private async Task GenerarOrden(LogisticaZonaDto zona)
        {
            if (Generando || CriticosDe(zona) == 0) return;

            Generando = true;
            Mensaje = "";
            try
            {
                var url = $"api/LogisticaPredictiva/generar-orden?diasHistorial={DiasHistorial}&ventanaDias={VentanaDias}";
                if (zona.ZonaLogisticaId.HasValue)
                    url += $"&zonaLogisticaId={zona.ZonaLogisticaId.Value}";

                var resp = await Http.PostAsync(url, null);
                if (resp.IsSuccessStatusCode)
                {
                    var ordenId = await resp.Content.ReadFromJsonAsync<int>();
                    Mensaje = $"Orden de carga #{ordenId} generada como borrador para {zona.ZonaNombre}. Revisá y confirmá en Cargas.";
                    MensajeEsError = false;
                }
                else
                {
                    Mensaje = "Error al generar orden: " + await resp.Content.ReadAsStringAsync();
                    MensajeEsError = true;
                }
            }
            catch (Exception ex)
            {
                Mensaje = "Error crítico: " + ex.Message;
                MensajeEsError = true;
                Logger.LogError(ex, "Error generando orden de carga de zona");
            }
            finally
            {
                Generando = false;
            }
        }

        // Formato es-CL: $ con separador de miles con punto
        private static readonly System.Globalization.NumberFormatInfo Nfi =
            new() { NumberGroupSeparator = ".", NumberDecimalDigits = 0 };
        private static string Money(decimal n) => "$" + Math.Round(n).ToString("N0", Nfi);

        private static string FmtDias(double? dias) =>
            dias.HasValue ? dias.Value.ToString("0.0") : "—";

        private static string DeltaTexto(LogisticaZonaDto z) =>
            (z.LcpTotal - z.CostoBaseViaje >= 0 ? "+" : "−") + Money(Math.Abs(z.LcpTotal - z.CostoBaseViaje));
    }
}
