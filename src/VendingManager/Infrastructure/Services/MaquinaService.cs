using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VendingManager.Core.Configuration;

namespace VendingManager.Infrastructure.Services
{

    public class MaquinaService : IMaquinaService
    {
        private readonly ApplicationDbContext _context;
        private readonly VendingConfig _config;

        public MaquinaService(ApplicationDbContext context, IOptions<VendingConfig> config)
        {
            _context = context;
            _config = config.Value;
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
            if (slot.Id > 0)
            {
                // MODO EDICIÓN (Actualizar existente)
                var existing = await _context.ConfiguracionSlots.FindAsync(slot.Id);
                if (existing != null)
                {
                    // Verificar si el número de slot objetivo ya está tomado por OTRO slot en la misma máquina
                    var collision = await _context.ConfiguracionSlots
                        .FirstOrDefaultAsync(s => s.MaquinaId == slot.MaquinaId &&
                                                  s.NumeroSlot == slot.NumeroSlot &&
                                                  s.Id != slot.Id);

                    if (collision != null)
                    {
                        // LÓGICA DE SWAP: Intercambiar Números de Slot
                        // Guardar el número "viejo" localmente para dárselo a la colisión
                        string oldNumber = existing.NumeroSlot;

                        // Darle a la colisión el número viejo (swap completo)
                        collision.NumeroSlot = oldNumber;
                        _context.Entry(collision).State = EntityState.Modified;
                    }

                    // Aplicar actualizaciones al objetivo
                    existing.NumeroSlot = slot.NumeroSlot;
                    existing.ProductoId = (slot.ProductoId == 0) ? null : slot.ProductoId;
                    existing.PrecioVenta = slot.PrecioVenta;
                    existing.StockActual = slot.StockActual;
                    existing.CapacidadMaxima = slot.CapacidadMaxima;

                    _context.Entry(existing).State = EntityState.Modified;
                }
            }
            else
            {
                // MODO CREACIÓN (Nuevo Slot)
                // Verificar si el número de slot ya existe
                var collision = await _context.ConfiguracionSlots
                       .FirstOrDefaultAsync(s => s.MaquinaId == slot.MaquinaId && s.NumeroSlot == slot.NumeroSlot);

                if (collision != null)
                {
                    throw new InvalidOperationException($"El slot {slot.NumeroSlot} ya existe. Editalo en lugar de crear uno nuevo.");
                }

                var newSlot = new ConfiguracionSlot
                {
                    MaquinaId = slot.MaquinaId,
                    NumeroSlot = slot.NumeroSlot,
                    ProductoId = (slot.ProductoId == 0) ? null : slot.ProductoId,
                    PrecioVenta = slot.PrecioVenta,
                    StockActual = slot.StockActual,
                    CapacidadMaxima = slot.CapacidadMaxima
                };
                _context.ConfiguracionSlots.Add(newSlot);
            }

            await _context.SaveChangesAsync();
        }


        public async Task ProcesarMovimientosLoteAsync(int maquinaId, List<SlotActionDto> acciones)
        {
            if (acciones == null || !acciones.Any()) return;

            var maquina = await _context.Maquinas.FindAsync(maquinaId);
            string nombreMaquina = maquina?.Nombre ?? $"ID {maquinaId}";
            var logDetalle = new List<string>();

            foreach (var accion in acciones)
            {
                var slot = await _context.ConfiguracionSlots
                    .Include(s => s.Producto)
                    .FirstOrDefaultAsync(s => s.Id == accion.SlotId);

                if (slot == null) continue;

                // --- ACCIÓN: VACIAR ---
                // --- ACCIÓN: VACIAR ---
                if (accion.ActionType == "EMPTY")
                {
                    // Siempre limpiar, aunque stock sea 0, porque el usuario quiere desasignar.
                    string prod = slot.Producto?.Nombre ?? "VACÍO";
                    logDetalle.Add($"[VACIADO] Slot {slot.NumeroSlot} ({prod}): {slot.StockActual} -> 0 (Desasignado)");
                    
                    slot.StockActual = 0;
                    slot.ProductoId = null; // Quitar asignación de producto
                    _context.Entry(slot).State = EntityState.Modified;
                    
                    continue; // Listo con este slot
                }

                // --- ACCIÓN: CAMBIAR PRODUCTO ---
                if (accion.ActionType == "SWAP")
                {
                    if (accion.NewProductoId.HasValue && accion.NewProductoId > 0)
                    {
                        var nuevoProd = await _context.Productos.FindAsync(accion.NewProductoId.Value);
                        if (nuevoProd != null)
                        {
                            string ant = slot.Producto?.Nombre ?? "VACÍO";
                            logDetalle.Add($"[CAMBIO] Slot {slot.NumeroSlot}: {ant} -> {nuevoProd.Nombre}");
                            
                            slot.ProductoId = nuevoProd.Id;
                            slot.StockActual = 0; 
                            
                            // Actualizar precio si se entrega
                            if (accion.NewPrecioVenta.HasValue)
                            {
                                slot.PrecioVenta = accion.NewPrecioVenta.Value;
                                logDetalle.Add($"[PRECIO] Slot {slot.NumeroSlot}: {slot.PrecioVenta:C0}");
                            }
                            
                            _context.Entry(slot).State = EntityState.Modified;
                        }
                    }
                    else if (accion.NewProductoId == null || accion.NewProductoId == 0)
                    {
                        // Limpiando producto
                         logDetalle.Add($"[BORRADO] Slot {slot.NumeroSlot}: {slot.Producto?.Nombre} -> VACÍO");
                         slot.ProductoId = null;
                         slot.StockActual = 0;
                         _context.Entry(slot).State = EntityState.Modified;
                    }
                    continue;
                }

                // --- ACCIÓN: RELLENAR (DEFAULT) ---
                if (accion.Cantidad <= 0) continue;
                if (slot.Producto == null) continue;

                // Lógica original de Reposición
                slot.Producto.StockBodega -= accion.Cantidad;
                _context.Entry(slot.Producto).State = EntityState.Modified;

                slot.StockActual += accion.Cantidad;
                if (slot.StockActual > slot.CapacidadMaxima) slot.StockActual = slot.CapacidadMaxima;
                _context.Entry(slot).State = EntityState.Modified;

                logDetalle.Add($"Slot {slot.NumeroSlot}: +{accion.Cantidad} ({slot.Producto.Nombre})");
            }

            // 4. Crear Movimiento de Caja 
            if (logDetalle.Any())
            {
                var movimiento = new MovimientoCaja
                {
                    Fecha = DateTime.Now,
                    Tipo = "OPERACIONAL", 
                    Categoria = "LOTES",
                    Monto = 0, 
                    Descripcion = $"Lote {nombreMaquina}: " + string.Join(", ", logDetalle)
                };
                _context.MovimientosCaja.Add(movimiento);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<List<OffsetDriftDto>> GetOffsetDriftAsync()
        {
            var threshold = _config.OffsetDriftThresholdHours;
            var minSamples = _config.OffsetDriftMinSamples;

            var query = from ds in _context.OffsetDriftStates
                        join m in _context.Maquinas on ds.MaquinaId equals m.Id
                        select new OffsetDriftDto
                        {
                            MaquinaId = m.Id,
                            Nombre = m.Nombre,
                            IdInternoMaquina = m.IdInternoMaquina,
                            ConfiguredOffsetHours = m.TimezoneOffsetHours,
                            ImpliedOffsetHours = ds.ImpliedOffsetHours,
                            SampleCount = ds.SampleCount,
                            MeasuredAtUtc = ds.MeasuredAtUtc,
                            IsFirstTimeProposal = m.TimezoneOffsetHours == null
                        };

            var all = await query.ToListAsync();

            return all.Where(d => d.IsFirstTimeProposal
                || (d.ConfiguredOffsetHours.HasValue
                    && Math.Abs(d.ImpliedOffsetHours - d.ConfiguredOffsetHours.Value) >= threshold
                    && d.SampleCount >= minSamples))
                .ToList();
        }

        public async Task UpdateTimezoneOffsetAsync(int id, int offsetHours)
        {
            var maquina = await _context.Maquinas.FindAsync(id);
            if (maquina == null)
                throw new KeyNotFoundException($"Maquina with ID {id} not found.");

            maquina.TimezoneOffsetHours = offsetHours;
            _context.Entry(maquina).State = EntityState.Modified;
            await _context.SaveChangesAsync();
        }
    }
}
