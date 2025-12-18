using Microsoft.EntityFrameworkCore;

namespace VendingManager.Infrastructure.Services
{
    using Core.DTOs; // Helper using

    public class MaquinaService : IMaquinaService
    {
        private readonly ApplicationDbContext _context;

        public MaquinaService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<Maquina>> GetMaquinasAsync()
        {
            return await _context.Maquinas.ToListAsync();
        }

        public async Task<Maquina?> GetMaquinaAsync(int id)
        {
            return await _context.Maquinas.FindAsync(id);
        }

        public async Task<Maquina> CreateMaquinaAsync(Maquina maquina)
        {
            _context.Maquinas.Add(maquina);
            await _context.SaveChangesAsync();
            return maquina;
        }

        public async Task UpdateMaquinaAsync(int id, Maquina maquina)
        {
            _context.Entry(maquina).State = EntityState.Modified;
            await _context.SaveChangesAsync();
        }

        public async Task DeleteMaquinaAsync(int id)
        {
            var maquina = await _context.Maquinas.FindAsync(id);
            if (maquina != null)
            {
                _context.Maquinas.Remove(maquina);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<List<ConfiguracionSlotDto>> GetSlotsAsync(int maquinaId)
        {
            return await _context.ConfiguracionSlots
                .Include(s => s.Producto)
                .Where(s => s.MaquinaId == maquinaId)
                .OrderBy(s => s.NumeroSlot)
                .Select(s => new ConfiguracionSlotDto
                {
                    Id = s.Id,
                    MaquinaId = s.MaquinaId,
                    NumeroSlot = s.NumeroSlot,
                    ProductoId = s.ProductoId,
                    StockActual = s.StockActual,
                    CapacidadMaxima = s.CapacidadMaxima,
                    PrecioVenta = s.PrecioVenta,

                    Producto = s.Producto == null ? null : new ProductoSlotDto
                    {
                        Id = s.Producto.Id,
                        Nombre = s.Producto.Nombre,
                        CodigoBarras = s.Producto.CodigoBarras ?? "S/C",
                        StockBodega = s.Producto.StockBodega
                    }
                })
                .ToListAsync();
        }

        public async Task UpdateSlotAsync(ConfiguracionSlotDto slot)
        {
            var existing = await _context.ConfiguracionSlots
                .FirstOrDefaultAsync(s => s.MaquinaId == slot.MaquinaId && s.NumeroSlot == slot.NumeroSlot);

            if (existing == null)
            {
                // Create
                var newSlot = new ConfiguracionSlot
                {
                    MaquinaId = slot.MaquinaId,
                    NumeroSlot = slot.NumeroSlot,
                    ProductoId = slot.ProductoId,
                    PrecioVenta = slot.PrecioVenta,
                    StockActual = slot.StockActual,
                    CapacidadMaxima = slot.CapacidadMaxima
                };
                _context.ConfiguracionSlots.Add(newSlot);
            }
            else
            {
                // Update
                existing.ProductoId = slot.ProductoId;
                existing.PrecioVenta = slot.PrecioVenta;
                existing.StockActual = slot.StockActual;
                existing.CapacidadMaxima = slot.CapacidadMaxima;
            }

            await _context.SaveChangesAsync();
        }
    }
}
