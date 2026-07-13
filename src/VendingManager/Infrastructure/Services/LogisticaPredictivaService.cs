using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.DTOs;

namespace VendingManager.Infrastructure.Services
{
    public class LogisticaPredictivaService(
        ApplicationDbContext context,
        IOrdenCargaService ordenCargaService) : ILogisticaPredictivaService
    {
        private const double UmbralCriticoDias = 2.0; // Quiebre proyectado < 48h

        /// <summary>
        /// Calcula, por zona logística, el lucro cesante proyectado (LCP) de los
        /// próximos <paramref name="ventanaProyeccionDias"/> días si no se recarga.
        ///
        /// Velocidad diaria: unidades vendidas del producto en la máquina durante los
        /// últimos <paramref name="diasHistorial"/> días (por Venta.FechaLocal) dividido
        /// por diasHistorial. Las ventas no tienen slot asociado: cuando un mismo producto
        /// ocupa varios slots de la misma máquina, la velocidad máquina-producto se
        /// reparte en partes iguales entre esos slots (supuesto simplificador y
        /// determinista; no se pondera por capacidad ni por posición).
        ///
        /// Las máquinas sin zona se agrupan en un bucket "Sin zona" con CostoBaseViaje 0
        /// y EsRentableViajar siempre false; las zonas reales (incluso con CostoBaseViaje 0)
        /// siguen el contrato general: EsRentableViajar cuando LcpTotal > CostoBaseViaje.
        /// </summary>
        public async Task<List<LogisticaZonaDto>> GetAnalisisZonasAsync(int diasHistorial = 14, int ventanaProyeccionDias = 3)
        {
            if (diasHistorial <= 0) throw new ArgumentOutOfRangeException(nameof(diasHistorial));
            if (ventanaProyeccionDias <= 0) throw new ArgumentOutOfRangeException(nameof(ventanaProyeccionDias));

            var desde = DateTime.Now.AddDays(-diasHistorial);

            var slots = await context.ConfiguracionSlots
                .Include(s => s.Maquina).ThenInclude(m => m.Zona)
                .Include(s => s.Producto)
                .Where(s => s.ProductoId != null && s.Producto != null)
                .ToListAsync();

            // Unidades vendidas por (máquina, producto) en la ventana de historial.
            // Cada fila de Venta representa una unidad.
            var ventas = await context.Ventas
                .Where(v => v.FechaLocal >= desde && v.ProductoId != null)
                .GroupBy(v => new { v.MaquinaId, v.ProductoId })
                .Select(g => new { g.Key.MaquinaId, g.Key.ProductoId, Unidades = g.Count() })
                .ToListAsync();

            var unidadesVendidas = ventas.ToDictionary(
                x => (x.MaquinaId, ProductoId: x.ProductoId!.Value),
                x => x.Unidades);

            var zonas = slots
                .GroupBy(s => s.Maquina.ZonaLogisticaId)
                .Select(zonaGroup =>
                {
                    var zona = zonaGroup.First().Maquina.Zona;

                    var maquinas = zonaGroup
                        .GroupBy(s => s.Maquina)
                        .Select(maqGroup =>
                        {
                            // Cantidad de slots que comparten producto dentro de la máquina,
                            // para repartir la velocidad máquina-producto en partes iguales.
                            var slotsPorProducto = maqGroup
                                .GroupBy(s => s.ProductoId!.Value)
                                .ToDictionary(g => g.Key, g => g.Count());

                            var slotDtos = maqGroup
                                .Select(s => BuildSlotDto(
                                    s,
                                    unidadesVendidas.GetValueOrDefault((maqGroup.Key.Id, s.ProductoId!.Value)),
                                    slotsPorProducto[s.ProductoId!.Value],
                                    diasHistorial,
                                    ventanaProyeccionDias))
                                .ToList();

                            return new LogisticaMaquinaDto
                            {
                                MaquinaId = maqGroup.Key.Id,
                                MaquinaNombre = maqGroup.Key.Nombre,
                                Ubicacion = maqGroup.Key.Ubicacion,
                                LcpMaquina = slotDtos.Sum(d => d.LcpSlot),
                                Slots = slotDtos
                            };
                        })
                        .OrderByDescending(m => m.LcpMaquina)
                        .ToList();

                    var lcpTotal = maquinas.Sum(m => m.LcpMaquina);
                    var costoBase = zona?.CostoBaseViaje ?? 0m;

                    return new LogisticaZonaDto
                    {
                        ZonaLogisticaId = zona?.Id,
                        ZonaNombre = zona?.Nombre ?? "Sin zona",
                        CostoBaseViaje = costoBase,
                        LcpTotal = lcpTotal,
                        // Solo el bucket "Sin zona" queda forzado a false; una zona real con
                        // costo base 0 sigue el contrato general (LcpTotal > CostoBaseViaje).
                        EsRentableViajar = zona != null && lcpTotal > costoBase,
                        Maquinas = maquinas
                    };
                })
                .OrderByDescending(z => z.LcpTotal - z.CostoBaseViaje)
                .ToList();

            return zonas;
        }

        private static LogisticaSlotDto BuildSlotDto(
            ConfiguracionSlot slot,
            int unidadesVendidasMaquinaProducto,
            int slotsConMismoProducto,
            int diasHistorial,
            int ventanaProyeccionDias)
        {
            // Velocidad máquina-producto repartida en partes iguales entre los slots
            // que comparten el producto (ver doc del método público).
            decimal velocidad = slotsConMismoProducto > 0
                ? (decimal)unidadesVendidasMaquinaProducto / diasHistorial / slotsConMismoProducto
                : 0m;

            decimal margen = Math.Max(0m, slot.PrecioVenta - (slot.Producto?.CostoPromedio ?? 0m));

            double? diasHastaQuiebre = null;
            decimal lcp = 0m;
            bool esCritico = false;

            if (velocidad > 0)
            {
                double t = slot.StockActual <= 0
                    ? 0
                    : slot.StockActual / (double)velocidad;
                diasHastaQuiebre = t;
                esCritico = t < UmbralCriticoDias;

                double diasVacios = Math.Clamp(ventanaProyeccionDias - t, 0, ventanaProyeccionDias);
                lcp = margen * velocidad * (decimal)diasVacios;
            }

            return new LogisticaSlotDto
            {
                SlotId = slot.Id,
                NumeroSlot = slot.NumeroSlot,
                ProductoId = slot.ProductoId!.Value,
                ProductoNombre = slot.Producto?.Nombre ?? "Sin Producto",
                StockActual = slot.StockActual,
                CapacidadMaxima = slot.CapacidadMaxima,
                UnidadesFaltantes = Math.Max(0, slot.CapacidadMaxima - slot.StockActual),
                VelocidadDiaria = velocidad,
                DiasHastaQuiebre = diasHastaQuiebre,
                EsCritico = esCritico,
                MargenUnitario = margen,
                LcpSlot = lcp
            };
        }

        public async Task<int> GenerarOrdenCargaBorradorAsync(int? zonaLogisticaId, int diasHistorial = 14, int ventanaProyeccionDias = 3)
        {
            var zonas = await GetAnalisisZonasAsync(diasHistorial, ventanaProyeccionDias);
            var zona = zonas.FirstOrDefault(z => z.ZonaLogisticaId == zonaLogisticaId)
                ?? throw new InvalidOperationException("Zona no encontrada o sin máquinas con slots configurados.");

            // Slots críticos (quiebre proyectado < 48h) con unidades por reponer,
            // consolidados por máquina+producto (una orden global por zona; cada
            // detalle lleva su MaquinaId, igual que las órdenes consolidadas existentes).
            var items = zona.Maquinas
                .SelectMany(m => m.Slots
                    .Where(s => s.EsCritico && s.UnidadesFaltantes > 0)
                    .Select(s => new { m.MaquinaId, s.ProductoId, s.UnidadesFaltantes }))
                .GroupBy(x => new { x.MaquinaId, x.ProductoId })
                .Select(g => new DetalleOrdenCargaItemDto
                {
                    ProductoId = g.Key.ProductoId,
                    MaquinaId = g.Key.MaquinaId,
                    Cantidad = g.Sum(x => x.UnidadesFaltantes)
                })
                .ToList();

            if (items.Count == 0)
                throw new InvalidOperationException($"No hay slots críticos en '{zona.ZonaNombre}'.");

            var orden = await ordenCargaService.CrearOrdenAsync(new CrearOrdenDto
            {
                Nombre = $"Rescate {zona.ZonaNombre} {DateTime.Now:dd/MM/yyyy}",
                MaquinaId = null, // Orden consolidada de la zona
                Items = items
            });

            return orden.Id;
        }
    }
}
