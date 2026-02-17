using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;

namespace VendingManager.Infrastructure.Services;

public class AuditService : IAuditService
{
    private readonly ApplicationDbContext _context;

    public AuditService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task RegistrarAccionAsync(string usuario, string accion, string detalle)
    {
        var auditoria = new Auditoria
        {
            Usuario = usuario,
            Accion = accion,
            Detalle = detalle,
            Fecha = DateTime.Now
        };

        _context.Auditoria.Add(auditoria);
        await _context.SaveChangesAsync();
    }
}
