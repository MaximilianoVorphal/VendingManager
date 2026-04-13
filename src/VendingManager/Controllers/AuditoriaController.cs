using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.Constants;

namespace VendingManager.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = Roles.Admin)] // Solo administradores pueden ver esto
    public class AuditoriaController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AuditoriaController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Auditoria>>> GetAuditoria([FromQuery] string? usuario = null, [FromQuery] string? accion = null)
        {
            var query = _context.Auditoria.AsQueryable();

            if (!string.IsNullOrEmpty(usuario))
            {
                query = query.Where(a => a.Usuario.Contains(usuario));
            }

            if (!string.IsNullOrEmpty(accion))
            {
                query = query.Where(a => a.Accion.Contains(accion));
            }

            // Ordenamos por fecha descendente (lo más nuevo primero)
            var logs = await query
                .OrderByDescending(a => a.Fecha)
                .Select(a => new VendingManager.Shared.DTOs.AuditoriaDto
                {
                    Id = a.Id,
                    Usuario = a.Usuario,
                    Accion = a.Accion,
                    Detalle = a.Detalle,
                    Fecha = a.Fecha
                })
                .ToListAsync();

            return Ok(logs);
        }
    }
}
