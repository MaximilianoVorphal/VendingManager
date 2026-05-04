using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VendingManager.Core.Configuration;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.DTOs;

namespace VendingManager.Infrastructure.Services
{
    public class PurchasingService : IPurchasingService
    {
        private readonly ApplicationDbContext _context;
        private readonly IOptions<VendingConfig> _config;
        private readonly IExcelExportService _excelExportService;

        public PurchasingService(ApplicationDbContext context, IOptions<VendingConfig> config, IExcelExportService excelExportService)
        {
            _context = context;
            _config = config;
            _excelExportService = excelExportService;
        }

        public async Task<List<StockCriticoDto>> GetStockCriticoAsync(int maquinaId)
        {
            var query = _context.ConfiguracionSlots
                .Include(s => s.Maquina)
                .Include(s => s.Producto)
                .Where(s => s.StockActual <= s.StockMinimo && s.ProductoId != 0);

            if (maquinaId > 0)
            {
                query = query.Where(s => s.MaquinaId == maquinaId);
            }

            return await query
                .Select(s => new StockCriticoDto
                {
                    SlotId = s.Id,
                    Maquina = s.Maquina.Nombre,
                    NumeroSlot = s.NumeroSlot,
                    Producto = s.Producto.Nombre,
                    ProductoId = s.ProductoId ?? 0,
                    StockActual = s.StockActual,
                    CapacidadMaxima = s.CapacidadMaxima
                })
                .OrderBy(s => s.Maquina)
                .ThenBy(s => s.NumeroSlot)
                .ToListAsync();
        }

        public async Task<List<PurchaseSuggestionDto>> GetPurchaseSuggestionAsync(int dias = 0, int maquinaId = 0)
        {
            if (dias <= 0) dias = _config.Value.RotacionStockMinimoDias;
            DateTime fechaInicio = DateTime.Now.Date.AddDays(-dias);
            
            var queryVentas = _context.Ventas
                .Where(v => v.FechaLocal >= fechaInicio)
                .Where(v => v.ProductoId != null && v.ProductoId != 0);
            if (maquinaId > 0) queryVentas = queryVentas.Where(v => v.MaquinaId == maquinaId);

            var ventas = await queryVentas
                .GroupBy(v => v.ProductoId!.Value)
                .Select(g => new { ProductoId = g.Key, Cantidad = g.Count() })
                .ToListAsync();

            var querySlots = _context.ConfiguracionSlots
                .Where(s => s.ProductoId != null && s.ProductoId != 0);
            if (maquinaId > 0) querySlots = querySlots.Where(s => s.MaquinaId == maquinaId);

            var stockMaquinas = await querySlots
                .GroupBy(s => s.ProductoId!.Value)
                .Select(g => new { ProductoId = g.Key, Stock = g.Sum(s => s.StockActual) })
                .ToListAsync();

            var querySlotsAll = _context.ConfiguracionSlots
                .Where(s => s.ProductoId != null && s.ProductoId != 0);
            if (maquinaId > 0) querySlotsAll = querySlotsAll.Where(s => s.MaquinaId == maquinaId);

            var configSlots = await querySlotsAll
                .Select(s => s.ProductoId!.Value)
                .Distinct()
                .ToListAsync();
            var productosEnSlots = new HashSet<int>(configSlots);

            var productos = await _context.Productos.ToListAsync();

            var result = new List<PurchaseSuggestionDto>();

            foreach (var p in productos)
            {
                var ventasPeriodo = ventas.FirstOrDefault(v => v.ProductoId == p.Id)?.Cantidad ?? 0;
                var stockEnMaquinas = stockMaquinas.FirstOrDefault(s => s.ProductoId == p.Id)?.Stock ?? 0;
                
                int sugerido = ventasPeriodo - (stockEnMaquinas + p.StockBodega);
                if (sugerido < 0) sugerido = 0;

                result.Add(new PurchaseSuggestionDto
                {
                    ProductoId = p.Id,
                    NombreProducto = p.Nombre,
                    VentasUltimos30Dias = ventasPeriodo,
                    StockActualMaquinas = stockEnMaquinas,
                    StockBodega = p.StockBodega,
                    CantidadSugerida = sugerido,
                    EnMaquina = productosEnSlots.Contains(p.Id)
                });
            }

            return result.OrderByDescending(x => x.CantidadSugerida).ThenByDescending(x => x.VentasUltimos30Dias).ToList();
        }

        public async Task<(byte[] content, string fileName)> ExportarSugerenciaCompraAsync(int dias = 30, int maquinaId = 0)
        {
            var suggestions = await GetPurchaseSuggestionAsync(dias, maquinaId);

            if (suggestions == null || !suggestions.Any())
                throw new InvalidOperationException("No hay sugerencias para exportar.");

            var bytes = await _excelExportService.ExportPurchasingReportAsync(suggestions, dias, maquinaId > 0 ? maquinaId : null);
            string name = maquinaId > 0
                ? $"Sugerencia_Compra_Maq{maquinaId}_{dias}dias.xlsx"
                : $"Sugerencia_Compra_Global_{dias}dias.xlsx";
            return (bytes, name);
        }
    }
}
