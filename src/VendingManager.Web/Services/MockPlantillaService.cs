namespace VendingManager.Web.Services;

public class MockPlantillaService : IPlantillaService
{
    private static readonly IReadOnlyList<Plantilla> Plantillas = new List<Plantilla>
    {
        new(1, "PLANTILLA ESTÁNDAR", new[] { 1, 2 }),
        new(2, "PLANTILLA PREMIUM", new[] { 3 }),
        new(3, "PLANTILLA COMPACTA", new[] { 4 }),
        new(4, "PLANTILLA MAXI", new[] { 5 }),
        new(5, "PLANTILLA OFICINA", new[] { 5 })
    };

    public Task<IReadOnlyList<Plantilla>> GetPlantillasAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Plantillas);
    }
}
