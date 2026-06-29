using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.DTOs;

namespace VendingManager.Infrastructure.Services
{
    public class VentasService(ApplicationDbContext context) : IVentasService
    {
        public async Task<List<MaquinaSimpleDto>> GetMaquinasAsync()
        {
            return await context.Maquinas
                .Select(m => new MaquinaSimpleDto { Id = m.Id, Nombre = m.Nombre })
                .ToListAsync();
        }

        public async Task<List<ProductoSimpleDto>> GetProductosAsync()
        {
            return await context.Productos
                .OrderBy(p => p.Nombre)
                .Select(p => new ProductoSimpleDto { Id = p.Id, Nombre = p.Nombre, PrecioVenta = p.PrecioVenta })
                .ToListAsync();
        }

        public async Task FixDatesAsync()
        {
            var ventas = await context.Ventas.Where(v => v.FechaLocal < new DateTime(2000, 1, 1)).ToListAsync();
            foreach (var v in ventas)
            {
                v.FechaLocal = v.FechaHora.AddHours(-11);
            }
            await context.SaveChangesAsync();
        }

        public async Task DeleteVentasRangoAsync(DateTime inicio, DateTime fin, int maquinaId)
        {
            DateTime finAjustado = fin.Date.AddDays(1).AddTicks(-1);
            DateTime inicioAjustado = inicio.Date;

            var query = context.Ventas.Where(v => v.FechaLocal >= inicioAjustado && v.FechaLocal <= finAjustado);

            if (maquinaId > 0)
            {
                query = query.Where(v => v.MaquinaId == maquinaId);
            }

            context.Ventas.RemoveRange(query);
            await context.SaveChangesAsync();
        }

        [Obsolete("RecalcularCostosHistoricosAsync is deprecated. Use ProductoCosto-based sync instead.", error: false)]
        public async Task RecalcularCostosHistoricosAsync()
        {
            var slots = await context.ConfiguracionSlots
                .Include(s => s.Producto)
                .Where(s => s.ProductoId != 0 && s.Producto != null)
                .ToListAsync();

            var slotMap = slots
                .GroupBy(s => new { s.MaquinaId, s.NumeroSlot })
                .ToDictionary(g => g.Key, g => g.First());

            var ventas = await context.Ventas
                .Include(v => v.Producto)
                .ToListAsync();

            int updated = 0;
            foreach (var v in ventas)
            {
                Producto? p = v.Producto;

                if (p == null)
                {
                    if (slotMap.TryGetValue(new { v.MaquinaId, v.NumeroSlot }, out var config))
                    {
                        p = config.Producto;
                        v.ProductoId = config.ProductoId;
                    }
                }

                if (p != null)
                {
                    if (v.CostoVenta != p.CostoPromedio)
                    {
                        v.CostoVenta = p.CostoPromedio;
                        updated++;
                    }
                }
            }

            if (updated > 0)
            {
                await context.SaveChangesAsync();
            }
        }
    }
}