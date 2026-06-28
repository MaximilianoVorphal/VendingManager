namespace VendingManager.Web.Services;

public interface IMachineOnlineService
{
    Task<IReadOnlyList<MachineOnlineStatus>> GetOnlineMachinesAsync(CancellationToken ct = default);
}

public record MachineOnlineStatus(int MachineId, string Name, bool IsOnline, DateTime LastSeen);
