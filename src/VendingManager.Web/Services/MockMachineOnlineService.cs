namespace VendingManager.Web.Services;

public class MockMachineOnlineService : IMachineOnlineService
{
    private static readonly IReadOnlyList<MachineOnlineStatus> Machines = new List<MachineOnlineStatus>
    {
        new(1, "Máquina 01 — Lobby Principal", true, DateTime.Now.AddMinutes(-3)),
        new(2, "Máquina 02 — Piso 3", false, DateTime.Now.AddHours(-2).AddMinutes(-15)),
        new(3, "Máquina 03 — Bodega", true, DateTime.Now.AddMinutes(-20)),
        new(4, "Máquina 04 — Oficinas Norte", true, DateTime.Now.AddHours(-1)),
        new(5, "Máquina 05 — Patio", false, DateTime.Now.AddHours(-5)),
        new(6, "Máquina 06 — Sala de Reuniones", true, DateTime.Now.AddMinutes(-45))
    };

    public Task<IReadOnlyList<MachineOnlineStatus>> GetOnlineMachinesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Machines);
    }
}
