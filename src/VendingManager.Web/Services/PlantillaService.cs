using System.Net.Http.Json;
using VendingManager.Shared.DTOs;

namespace VendingManager.Web.Services;

/// <summary>
/// Real implementation backed by the recharge templates API
/// (<c>api/TemplateRecarga</c>). Each recharge template becomes a "plantilla"
/// carrying the machine ids of its periods, so the UI can scope the unit
/// dropdown to the machines the template actually covers.
/// </summary>
public class PlantillaService(HttpClient http) : IPlantillaService
{
    public async Task<IReadOnlyList<Plantilla>> GetPlantillasAsync(CancellationToken ct = default)
    {
        var templates = await http.GetFromJsonAsync<List<TemplateRecargaDto>>("api/TemplateRecarga", ct)
                        ?? [];

        return templates
            .Select(t => new Plantilla(
                t.Id,
                t.Nombre,
                t.Periodos.Select(p => p.MaquinaId).Distinct().ToList(),
                t.FechaRecargaMin,
                t.FechaFinMax))
            .ToList();
    }
}
