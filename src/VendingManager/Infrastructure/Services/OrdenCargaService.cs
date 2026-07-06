using Microsoft.EntityFrameworkCore;
using VendingManager.Shared.DTOs;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;

namespace VendingManager.Infrastructure.Services
{
    public class OrdenCargaService(ApplicationDbContext context) : IOrdenCargaService
    {
        public async Task<OrdenCargaDto> CrearOrdenAsync(CrearOrdenDto dto)
        {
            // 1. Validate Machine (Only if not global)
            Maquina? maquina = null;
            if (dto.MaquinaId.HasValue && dto.MaquinaId.Value > 0)
            {
                maquina = await context.Maquinas.FindAsync(dto.MaquinaId);
                if (maquina == null) throw new Exception("Máquina no encontrada.");
            }

            // 2. Create Order Entity
            var orden = new OrdenCarga
            {
                MaquinaId = (dto.MaquinaId.HasValue && dto.MaquinaId.Value > 0) ? dto.MaquinaId : null,
                Nombre = dto.Nombre,
                FechaCreacion = dto.Fecha ?? DateTime.Now,
                Estado = "PENDIENTE"
            };

            // 3. Process Items & Deduct Stock
            foreach (var item in dto.Items)
            {
                if (item.Cantidad <= 0) continue;

                var producto = await context.Productos.FindAsync(item.ProductoId);
                if (producto == null) continue;

                if (!dto.IgnorarStock && producto.StockBodega < item.Cantidad)
                {
                    throw new Exception($"Stock insuficiente para '{producto.Nombre}'. Disponible: {producto.StockBodega}, Solicitado: {item.Cantidad}");
                }

                producto.StockBodega -= item.Cantidad;

                int? itemMaquinaId = item.MaquinaId;
                if (!itemMaquinaId.HasValue && orden.MaquinaId.HasValue)
                {
                    itemMaquinaId = orden.MaquinaId;
                }

                orden.Detalles.Add(new DetalleOrdenCarga
                {
                    ProductoId = producto.Id,
                    ProductoNombre = producto.Nombre,
                    CantidadSolicitada = item.Cantidad,
                    CantidadRetornada = 0,
                    CostoUnitario = producto.CostoPromedio,
                    MaquinaId = itemMaquinaId
                });
            }

            context.OrdenesCarga.Add(orden);
            await context.SaveChangesAsync();

            return MapToDto(orden, maquina?.Nombre ?? "RUTA GLOBAL", maquina?.IdInternoMaquina ?? "");
        }

        public async Task<bool> FinalizarOrdenAsync(FinalizarOrdenDto dto)
        {
            var orden = await context.OrdenesCarga
                .Include(o => o.Detalles)
                .FirstOrDefaultAsync(o => o.Id == dto.OrdenId);

            if (orden == null) throw new Exception("Orden no encontrada.");
            if (orden.Estado == "FINALIZADA") throw new Exception("La orden ya fue finalizada.");

            foreach (var ret in dto.Retornos)
            {
                if (ret.CantidadRetornada <= 0) continue;

                var detalle = orden.Detalles.FirstOrDefault(d => d.Id == ret.DetalleId);
                if (detalle == null) continue;

                if (ret.CantidadRetornada > detalle.CantidadSolicitada)
                {
                    throw new Exception($"Error en '{detalle.ProductoNombre}': Retorno ({ret.CantidadRetornada}) no puede ser mayor a lo solicitado ({detalle.CantidadSolicitada}).");
                }

                detalle.CantidadRetornada = ret.CantidadRetornada;

                var producto = await context.Productos.FindAsync(detalle.ProductoId);
                if (producto != null)
                {
                    producto.StockBodega += ret.CantidadRetornada;
                }
            }

            foreach (var detalle in orden.Detalles)
            {
                int cantidadCargada = detalle.CantidadSolicitada - detalle.CantidadRetornada;
                int? maqId = detalle.MaquinaId ?? orden.MaquinaId;
                if (maqId.HasValue && cantidadCargada > 0)
                {
                    var slot = await context.ConfiguracionSlots
                        .FirstOrDefaultAsync(s => s.MaquinaId == maqId.Value
                                               && s.ProductoId == detalle.ProductoId);
                    if (slot != null)
                    {
                        slot.StockActual += cantidadCargada;
                        if (slot.StockActual > slot.CapacidadMaxima)
                            slot.StockActual = slot.CapacidadMaxima;
                    }
                }
            }

            decimal costoTotal = orden.Detalles.Sum(d =>
                (d.CantidadSolicitada - d.CantidadRetornada) * d.CostoUnitario);

            if (costoTotal > 0)
            {
                string maquinaNombre = "RUTA GLOBAL";
                if (orden.MaquinaId.HasValue)
                {
                    maquinaNombre = await context.Maquinas
                        .Where(m => m.Id == orden.MaquinaId.Value)
                        .Select(m => m.Nombre)
                        .FirstOrDefaultAsync() ?? "Desconocida";
                }

                context.MovimientosCaja.Add(new MovimientoCaja
                {
                    Fecha = DateTime.Now,
                    Descripcion = $"Carga #{orden.Id} — {(string.IsNullOrEmpty(orden.Nombre) ? maquinaNombre : orden.Nombre)}",
                    Monto = -costoTotal,
                    Tipo = "GASTO",
                    Categoria = "MERCADERIA",
                    OrdenCargaId = orden.Id
                });
            }

            orden.Estado = "FINALIZADA";
            orden.FechaFinalizacion = DateTime.Now;

            await context.SaveChangesAsync();
            return true;
        }

        public async Task<List<OrdenCargaDto>> GetOrdenesAsync(int maquinaId = 0)
        {
            var query = context.OrdenesCarga
                .Include(o => o.Detalles)
                .AsQueryable();

            if (maquinaId > 0)
            {
                query = query.Where(o => o.MaquinaId == maquinaId);
            }

            var ordenes = await query.OrderByDescending(o => o.FechaCreacion).ToListAsync();

            var maquinas = await context.Maquinas.ToDictionaryAsync(m => m.Id, m => new { m.Nombre, m.IdInternoMaquina });

            return ordenes.Select(o =>
            {
                var info = (o.MaquinaId.HasValue && maquinas.ContainsKey(o.MaquinaId.Value))
                    ? maquinas[o.MaquinaId.Value]
                    : new { Nombre = "RUTA GLOBAL", IdInternoMaquina = "" };
                return MapToDto(o, info.Nombre, info.IdInternoMaquina);
            }).ToList();
        }

        public async Task<OrdenCargaDto?> GetOrdenByIdAsync(int id)
        {
            var orden = await context.OrdenesCarga
                .Include(o => o.Detalles)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (orden == null) return null;

            string maquinaName = "RUTA GLOBAL";
            string idInternoMaquina = "";
            if (orden.MaquinaId.HasValue)
            {
                var maquina = await context.Maquinas
                    .Where(m => m.Id == orden.MaquinaId.Value)
                    .Select(m => new { m.Nombre, m.IdInternoMaquina })
                    .FirstOrDefaultAsync();
                maquinaName = maquina?.Nombre ?? "Desconocida";
                idInternoMaquina = maquina?.IdInternoMaquina ?? "";
            }

            return MapToDto(orden, maquinaName, idInternoMaquina);
        }

        public async Task<List<StockCriticoDto>> GetSugerenciaCargaAsync(int maquinaId)
        {
            var slots = await context.ConfiguracionSlots
               .Include(s => s.Maquina)
               .Include(s => s.Producto)
               .Where(s => s.MaquinaId == maquinaId && s.ProductoId != null)
               .Where(s => s.StockActual < s.CapacidadMaxima)
               .ToListAsync();

            var dtos = slots.Select(s => new StockCriticoDto
            {
                SlotId = s.Id,
                Maquina = s.Maquina.Nombre,
                IdInternoMaquina = s.Maquina.IdInternoMaquina,
                NumeroSlot = s.NumeroSlot,
                Producto = s.Producto?.Nombre ?? "Sin Producto",
                ProductoId = s.ProductoId ?? 0,
                StockActual = s.StockActual,
                CapacidadMaxima = s.CapacidadMaxima
            }).ToList();

            return SortSlotsNumerically(dtos);
        }

        public async Task<List<StockCriticoDto>> GetSugerenciaGlobalAsync()
        {
            var slots = await context.ConfiguracionSlots
                .Include(s => s.Maquina)
                .Include(s => s.Producto)
                .Where(s => s.ProductoId != null)
                .Where(s => s.StockActual < s.CapacidadMaxima)
                .ToListAsync();

            var dtos = slots.Select(s => new StockCriticoDto
            {
                SlotId = s.Id,
                Maquina = s.Maquina.Nombre,
                IdInternoMaquina = s.Maquina.IdInternoMaquina,
                NumeroSlot = s.NumeroSlot,
                Producto = s.Producto?.Nombre ?? "Sin Producto",
                ProductoId = s.ProductoId ?? 0,
                StockActual = s.StockActual,
                CapacidadMaxima = s.CapacidadMaxima
            }).ToList();

            var sorted = SortSlotsNumerically(dtos);
            return sorted.OrderBy(x => x.Maquina).ThenBy(x => x.NumeroSlot, new NumericStringComparer()).ToList();
        }

        private List<StockCriticoDto> SortSlotsNumerically(List<StockCriticoDto> list)
        {
            return list.OrderBy(x => x.NumeroSlot, new NumericStringComparer()).ToList();
        }

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

                if (isXNumeric) return -1;
                if (isYNumeric) return 1;

                return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            }
        }

        public async Task<bool> ActualizarNombreOrdenAsync(int ordenId, string nuevoNombre)
        {
            var orden = await context.OrdenesCarga.FindAsync(ordenId);
            if (orden == null) return false;

            orden.Nombre = nuevoNombre;
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ActualizarOrdenAsync(int ordenId, ActualizarOrdenRequestDto dto)
        {
            var orden = await context.OrdenesCarga.FindAsync(ordenId);
            if (orden == null) return false;

            orden.Nombre = dto.Nombre;
            orden.FechaCreacion = dto.FechaCreacion;

            await context.SaveChangesAsync();
            return true;
        }

        private OrdenCargaDto MapToDto(OrdenCarga orden, string maquinaNombre, string idInternoMaquina)
        {
            var detalles = orden.Detalles.Select(d => new DetalleOrdenDisplayDto
            {
                Id = d.Id,
                ProductoId = d.ProductoId,
                ProductoNombre = d.ProductoNombre,
                CantidadSolicitada = d.CantidadSolicitada,
                CantidadRetornada = d.CantidadRetornada,
                CostoUnitario = d.CostoUnitario,
                MaquinaId = d.MaquinaId,
                IdInternoMaquina = idInternoMaquina
            }).ToList();

            return new OrdenCargaDto
            {
                Id = orden.Id,
                Nombre = orden.Nombre,
                FechaCreacion = orden.FechaCreacion,
                Estado = orden.Estado,
                MaquinaId = orden.MaquinaId,
                MaquinaNombre = maquinaNombre,
                IdInternoMaquina = idInternoMaquina,
                CostoTotal = detalles.Sum(d => (d.CantidadSolicitada - d.CantidadRetornada) * d.CostoUnitario),
                Detalles = detalles
            };
        }
    }
}