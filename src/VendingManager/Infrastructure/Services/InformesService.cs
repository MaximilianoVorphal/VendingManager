using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;

namespace VendingManager.Infrastructure.Services
{
    public class InformesService : IInformesService
    {
        private readonly ApplicationDbContext _context;

        public InformesService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Informe> SubirInformeAsync(Informe informe)
        {
            _context.Informes.Add(informe);
            await _context.SaveChangesAsync();
            return informe;
        }

        public async Task<List<Informe>> ObtenerTodosSinContenidoAsync()
        {
            // Proyectamos para no traer el campo Contenido (varbinary) que es pesado
            return await _context.Informes
                .Select(i => new Informe
                {
                    Id = i.Id,
                    Nombre = i.Nombre,
                    Extension = i.Extension,
                    TipoContenido = i.TipoContenido,
                    Carpeta = i.Carpeta,
                    FechaSubida = i.FechaSubida,
                    Contenido = new byte[0] // Asignamos array vacío o null
                })
                .OrderByDescending(i => i.FechaSubida)
                .ToListAsync();
        }

        public async Task<Informe?> ObtenerPorIdAsync(int id)
        {
            return await _context.Informes.FindAsync(id);
        }

        public async Task EliminarInformeAsync(int id)
        {
            var informe = await _context.Informes.FindAsync(id);
            if (informe != null)
            {
                _context.Informes.Remove(informe);
                await _context.SaveChangesAsync();
            }
        }
    }
}
