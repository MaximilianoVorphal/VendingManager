using Microsoft.EntityFrameworkCore;
using VendingManager.Shared.DTOs;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;

namespace VendingManager.Infrastructure.Services
{
    public class OrdenCargaService : IOrdenCargaService
    {
        private readonly ApplicationDbContext _context;

        public OrdenCargaService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<OrdenCargaDto> CrearOrdenAsync(CrearOrdenDto dto)
        {
            // 1. Validate Machine (Only if not global)
            Maquina? maquina = null;
            if (dto.MaquinaId.HasValue && dto.MaquinaId.Value > 0)
            {
                maquina = await _context.Maquinas.FindAsync(dto.MaquinaId);
                if (maquina == null) throw new Exception("Máquina no encontrada.");
            }

            // 2. Create Order Entity
            var orden = new OrdenCarga
            {
                MaquinaId = (dto.MaquinaId.HasValue && dto.MaquinaId.Value > 0) ? dto.MaquinaId : null,
                Nombre = dto.Nombre, // Save Name
                FechaCreacion = dto.Fecha ?? DateTime.Now,
                Estado = "PENDIENTE"
            };

            // 3. Process Items & Deduct Stock
            foreach (var item in dto.Items)
            {
                if (item.Cantidad <= 0) continue;

                var producto = await _context.Productos.FindAsync(item.ProductoId);
                if (producto == null) continue; // Skip invalid products

                // Check stock availability (unless ignored)
                 if (!dto.IgnorarStock && producto.StockBodega < item.Cantidad)
                 {
                     throw new Exception($"Stock insuficiente para '{producto.Nombre}'. Disponible: {producto.StockBodega}, Solicitado: {item.Cantidad}");
                 }

                // Deduct Stock
                producto.StockBodega -= item.Cantidad;

                // Determine Machine ID for this detail
                int? itemMaquinaId = item.MaquinaId;
                if (!itemMaquinaId.HasValue && orden.MaquinaId.HasValue)
                {
                    itemMaquinaId = orden.MaquinaId;
                }

                // Add Detail
                orden.Detalles.Add(new DetalleOrdenCarga
                {
                    ProductoId = producto.Id,
                    ProductoNombre = producto.Nombre,
                    CantidadSolicitada = item.Cantidad,
                    CantidadRetornada = 0,
                    MaquinaId = itemMaquinaId
                });
            }

            _context.OrdenesCarga.Add(orden);
            await _context.SaveChangesAsync();

            return MapToDto(orden, maquina?.Nombre ?? "RUTA GLOBAL");
        }

        public async Task<bool> FinalizarOrdenAsync(FinalizarOrdenDto dto)
        {
            var orden = await _context.OrdenesCarga
                .Include(o => o.Detalles)
                .FirstOrDefaultAsync(o => o.Id == dto.OrdenId);

            if (orden == null) throw new Exception("Orden no encontrada.");
            if (orden.Estado == "FINALIZADA") throw new Exception("La orden ya fue finalizada.");

            // Process Returns
            foreach (var ret in dto.Retornos)
            {
                if (ret.CantidadRetornada <= 0) continue;

                var detalle = orden.Detalles.FirstOrDefault(d => d.Id == ret.DetalleId);
                if (detalle == null) continue;

                if (ret.CantidadRetornada > detalle.CantidadSolicitada)
                {
                    throw new Exception($"Error en '{detalle.ProductoNombre}': Retorno ({ret.CantidadRetornada}) no puede ser mayor a lo solicitado ({detalle.CantidadSolicitada}).");
                }

                // Update Detail
                detalle.CantidadRetornada = ret.CantidadRetornada;

                // Return Stock to Warehouse
                var producto = await _context.Productos.FindAsync(detalle.ProductoId);
                if (producto != null)
                {
                    producto.StockBodega += ret.CantidadRetornada;
                }
            }

            orden.Estado = "FINALIZADA";
            orden.FechaFinalizacion = DateTime.Now;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<OrdenCargaDto>> GetOrdenesAsync(int maquinaId = 0)
        {
            var query = _context.OrdenesCarga
                .Include(o => o.Detalles)
                .AsQueryable();

            if (maquinaId > 0)
            {
                query = query.Where(o => o.MaquinaId == maquinaId);
            }

            var ordenes = await query.OrderByDescending(o => o.FechaCreacion).ToListAsync();
            
            var maquinas = await _context.Maquinas.ToDictionaryAsync(m => m.Id, m => m.Nombre);

            return ordenes.Select(o => MapToDto(o, (o.MaquinaId.HasValue && maquinas.ContainsKey(o.MaquinaId.Value)) ? maquinas[o.MaquinaId.Value] : "RUTA GLOBAL")).ToList();
        }

        public async Task<OrdenCargaDto?> GetOrdenByIdAsync(int id)
        {
             var orden = await _context.OrdenesCarga
                .Include(o => o.Detalles)
                .FirstOrDefaultAsync(o => o.Id == id);
            
            if (orden == null) return null;

            string maquinaName = "RUTA GLOBAL";
            if(orden.MaquinaId.HasValue)
            {
                maquinaName = await _context.Maquinas
                    .Where(m => m.Id == orden.MaquinaId.Value)
                    .Select(m => m.Nombre)
                    .FirstOrDefaultAsync() ?? "Desconocida";
            }

            return MapToDto(orden, maquinaName);
        }

        public async Task<List<StockCriticoDto>> GetSugerenciaCargaAsync(int maquinaId)
        {
             // Returns list of items where StockActual < Capacidad
             var slots = await _context.ConfiguracionSlots
                .Include(s => s.Maquina)
                .Include(s => s.Producto)
                .Where(s => s.MaquinaId == maquinaId && s.ProductoId != null)
                .Where(s => s.StockActual < s.CapacidadMaxima)
                .ToListAsync();

            var dtos = slots.Select(s => new StockCriticoDto
            {
                SlotId = s.Id,
                Maquina = s.Maquina.Nombre,
                NumeroSlot = s.NumeroSlot,
                Producto = s.Producto?.Nombre ?? "Sin Producto",
                ProductoId = s.ProductoId ?? 0,
                StockActual = s.StockActual,
                CapacidadMaxima = s.CapacidadMaxima
            }).ToList();

            // NUMERIC SORT
            return SortSlotsNumerically(dtos);
        }

        public async Task<List<StockCriticoDto>> GetSugerenciaGlobalAsync()
        {
            var slots = await _context.ConfiguracionSlots
                .Include(s => s.Maquina)
                .Include(s => s.Producto)
                .Where(s => s.ProductoId != null)
                .Where(s => s.StockActual < s.CapacidadMaxima)
                .ToListAsync();

            var dtos = slots.Select(s => new StockCriticoDto
            {
                SlotId = s.Id,
                Maquina = s.Maquina.Nombre,
                NumeroSlot = s.NumeroSlot,
                Producto = s.Producto?.Nombre ?? "Sin Producto",
                ProductoId = s.ProductoId ?? 0,
                StockActual = s.StockActual,
                CapacidadMaxima = s.CapacidadMaxima
            }).ToList();

            // Sort by Machine Name then Slot
            var sorted = SortSlotsNumerically(dtos);
            return sorted.OrderBy(x => x.Maquina).ThenBy(x => x.NumeroSlot, new NumericStringComparer()).ToList();
        }

        private List<StockCriticoDto> SortSlotsNumerically(List<StockCriticoDto> list)
        {
            return list.OrderBy(x => x.NumeroSlot, new NumericStringComparer()).ToList();
        }

        // Helper Class for Sorting
        public class NumericStringComparer : IComparer<string>
        {
            public int Compare(string? x, string? y)
            {
                if (x == y) return 0;
                if (string.IsNullOrEmpty(x)) return -1;
                if (string.IsNullOrEmpty(y)) return 1;

                bool isXNumeric = int.TryParse(x, out int xInt);
                bool isYNumeric = int.TryParse(y, out int yInt);

                if (isXNumeric && isYNumeric)
                {
                    return xInt.CompareTo(yInt);
                }
                
                // If one is numeric and the other isn't, usually numbers come first? or last?
                // Let's standard string compare if not both numbers.
                // Or try to parse mixed content? For now simple int parse is enough for "1", "10", etc.
                if (isXNumeric) return -1; // Numbers first
                if (isYNumeric) return 1;

                return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            }
        }

        public async Task<bool> ActualizarNombreOrdenAsync(int ordenId, string nuevoNombre)
        {
            var orden = await _context.OrdenesCarga.FindAsync(ordenId);
            if (orden == null) return false;
            
            orden.Nombre = nuevoNombre;
            await _context.SaveChangesAsync();
            return true;
        }

        private OrdenCargaDto MapToDto(OrdenCarga orden, string maquinaNombre)
        {
            return new OrdenCargaDto
            {
                Id = orden.Id,
                Nombre = orden.Nombre, // Mapped
                FechaCreacion = orden.FechaCreacion,
                Estado = orden.Estado,
                MaquinaId = orden.MaquinaId,
                MaquinaNombre = maquinaNombre,
                Detalles = orden.Detalles.Select(d => new DetalleOrdenDisplayDto
                {
                    Id = d.Id,
                    ProductoId = d.ProductoId,
                    ProductoNombre = d.ProductoNombre,
                    CantidadSolicitada = d.CantidadSolicitada,
                    CantidadRetornada = d.CantidadRetornada,
                    MaquinaId = d.MaquinaId
                }).ToList()
            };
        }
    }
}
