namespace VendingManager.Core.Interfaces;

public interface IAuditService
{
    Task RegistrarAccionAsync(string usuario, string accion, string detalle);
}
