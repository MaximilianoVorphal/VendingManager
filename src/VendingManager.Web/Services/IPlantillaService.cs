namespace VendingManager.Web.Services;

public interface IPlantillaService
{
    Task<IReadOnlyList<Plantilla>> GetPlantillasAsync(CancellationToken ct = default);
}

public record Plantilla(
    int Id,
    string Nombre,
    IReadOnlyList<int>? MaquinaIds = null,
    DateTime? Desde = null,
    DateTime? Hasta = null);
