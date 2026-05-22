using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
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
        private readonly IMemoryCache _cache;
        private readonly ITemplateRecargaLifecycleService _lifecycleService;
        private readonly ILogger<PurchasingService> _logger;

        public PurchasingService(
            ApplicationDbContext context,
            IOptions<VendingConfig> config,
            IExcelExportService excelExportService,
            IMemoryCache cache,
            ITemplateRecargaLifecycleService lifecycleService,
            ILogger<PurchasingService> logger)
        {
            _context = context;
            _config = config;
            _excelExportService = excelExportService;
            _cache = cache;
            _lifecycleService = lifecycleService;
            _logger = logger;
        }

        public async Task<List<StockCriticoDto>> GetStockCriticoAsync(int maquinaId)
        {
            if (_config.Value.UseTemplateInventoryForStockCritico)
            {
                // Try template-based inventory first
                var templateSlots = await _lifecycleService.GetLatestTerminadoTemplateSlotsAsync(maquinaId);
                if (templateSlots.Any())
                {
                    _logger.LogInformation(
                        "[GetStockCritico] Using latest completed template inventory for maquina {MaquinaId}: {Count} slots",
                        maquinaId, templateSlots.Count);

                    return await BuildStockCriticoFromTemplateSlots(templateSlots, maquinaId);
                }
                _logger.LogWarning(
                    "[GetStockCritico] No active template for maquina {MaquinaId}; falling back to ConfiguracionSlots",
                    maquinaId);
            }

            // Fallback: use ConfiguracionSlots directly
            return await GetStockCriticoFromConfiguracionSlots(maquinaId);
        }

        private async Task<List<StockCriticoDto>> BuildStockCriticoFromTemplateSlots(List<SnapshotSlotDto> templateSlots, int maquinaId)
        {
            // Get maquina name(s) for display
            var maquinaIds = templateSlots
                .Where(s => s.ProductoId != null)
                .Select(s => maquinaId) // Already filtered by maquina in the query
                .Distinct()
                .ToList();

            // If filtering by maquinaId, get that maquina's name; otherwise use "TODAS"
            string maquinaNombre = maquinaId > 0
                ? await _context.Maquinas.Where(m => m.Id == maquinaId).Select(m => m.Nombre).FirstOrDefaultAsync() ?? "Máquina"
                : "TODAS LAS MÁQUINAS";

            return templateSlots
                .Where(s => s.ProductoId != null && s.CantidadInicial <= 2)
                .Select(s => new StockCriticoDto
                {
                    SlotId = s.Id,
                    Maquina = maquinaNombre,
                    NumeroSlot = s.NumeroSlot,
                    Producto = s.ProductoNombre,
                    ProductoId = s.ProductoId ?? 0,
                    StockActual = s.CantidadInicial,
                    CapacidadMaxima = s.CapacidadSlot,
                    Fuente = "template"
                })
                .OrderBy(s => s.Maquina)
                .ThenBy(s => s.NumeroSlot)
                .ToList();
        }

        private async Task<List<StockCriticoDto>> GetStockCriticoFromConfiguracionSlots(int maquinaId)
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
                    Producto = s.Producto != null ? s.Producto.Nombre : "Sin producto",
                    ProductoId = s.ProductoId ?? 0,
                    StockActual = s.StockActual,
                    CapacidadMaxima = s.CapacidadMaxima,
                    Fuente = "configuracion"
                })
                .OrderBy(s => s.Maquina)
                .ThenBy(s => s.NumeroSlot)
                .ToListAsync();
        }

        public async Task<List<PurchaseSuggestionDto>> GetPurchaseSuggestionAsync(int dias = 0, int maquinaId = 0)
        {
            if (dias <= 0) dias = _config.Value.RotacionStockMinimoDias;
            var key = $"PurchasingService:GetPurchaseSuggestionAsync:{dias}-M{maquinaId}";

            var result = await _cache.GetOrCreateAsync(key, async (entry) =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(5);

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

                var resultInner = new List<PurchaseSuggestionDto>();

                foreach (var p in productos)
                {
                    var ventasPeriodo = ventas.FirstOrDefault(v => v.ProductoId == p.Id)?.Cantidad ?? 0;
                    var stockEnMaquinas = stockMaquinas.FirstOrDefault(s => s.ProductoId == p.Id)?.Stock ?? 0;

                    int sugerido = ventasPeriodo - (stockEnMaquinas + p.StockBodega);
                    if (sugerido < 0) sugerido = 0;

                    resultInner.Add(new PurchaseSuggestionDto
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

                return resultInner.OrderByDescending(x => x.CantidadSugerida).ThenByDescending(x => x.VentasUltimos30Dias).ToList();
            });

            return result!;
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
