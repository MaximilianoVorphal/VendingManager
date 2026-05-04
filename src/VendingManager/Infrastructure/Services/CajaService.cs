using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using System.IO;
using VendingManager.Core.Configuration;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

namespace VendingManager.Infrastructure.Services
{
    public class CajaService : ICajaService
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly IInformesService _informesService;
        private readonly IOptions<VendingConfig> _config;
        private readonly IExcelExportService _excelExportService;
        private readonly IVentaRepository _ventaRepository;

        public CajaService(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            IInformesService informesService,
            IOptions<VendingConfig> config,
            IExcelExportService excelExportService,
            IVentaRepository ventaRepository)
        {
            _context = context;
            _environment = environment;
            _informesService = informesService;
            _config = config;
            _excelExportService = excelExportService;
            _ventaRepository = ventaRepository;
        }

        // 2. UploadComprobanteAsync
        public async Task<string> UploadComprobanteAsync(Stream fileStream, string fileName, string? webRootPath = null, string? category = null)
        {
            using (var memoryStream = new MemoryStream())
            {
                await fileStream.CopyToAsync(memoryStream);
                var content = memoryStream.ToArray();

                string extension = Path.GetExtension(fileName).ToLower();
                string contentType = "application/octet-stream";

                if (extension == ".pdf") contentType = "application/pdf";
                else if (extension == ".jpg" || extension == ".jpeg") contentType = "image/jpeg";
                else if (extension == ".png") contentType = "image/png";

                string folder = "Caja";
                if (!string.IsNullOrEmpty(category))
                {
                    folder = $"Caja/{category}";
                }

                var informe = new Informe
                {
                    Nombre = Path.GetFileNameWithoutExtension(fileName) + "_CAJA",
                    Extension = extension,
                    Carpeta = folder,
                    TipoContenido = contentType,
                    Contenido = content,
                    FechaSubida = DateTime.Now
                };

                // Create a more robust MIME type detection if needed, but for now extension based is likely fine or passed in? 
                // The filename has extension.

                // Actually, I can just use a generic image type or try to get it. 
                // But let's check what Controller passes. It passes file.FileName.

                var saved = await _informesService.SubirInformeAsync(informe);
                // Append extension as query param to allow client-side type detection
                return $"api/informes/{saved.Id}?ext={extension}";
            }
        }

        public async Task<CajaResumenDto> GetResumenAsync(int month, int year)
        {
            DateTime startOfMonth = new DateTime(year, month, 1);
            DateTime endOfMonth = startOfMonth.AddMonths(1).AddSeconds(-1);

            // 1. SALDO ANTERIOR
            // Se considera todo lo anterior al inicio de mes, PERO respetando la fecha global de inicio
            var prevIngresosVentas = await _ventaRepository.SumPrecioVentaPaidInRangeAsync(
                _config.Value.CajaStartDate, startOfMonth.AddSeconds(-1));

            var prevMovimientos = await _context.MovimientosCaja
                .Where(m => m.Fecha < startOfMonth && m.Fecha >= _config.Value.CajaStartDate)
                .SumAsync(m => m.Monto);

            decimal saldoAnterior = prevIngresosVentas + prevMovimientos;

            // 2. MOVIMIENTOS DEL MES
            // Solo considerar si están dentro del rango global
            var monthIngresosVentas = await _ventaRepository.SumPrecioVentaPaidInRangeAsync(startOfMonth, endOfMonth);

            var monthGastos = await _context.MovimientosCaja
                .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate && m.Monto < 0)
                .SumAsync(m => m.Monto);

            var monthAportes = await _context.MovimientosCaja
                .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate && m.Monto > 0)
                .SumAsync(m => m.Monto);

            // 3. UTILIDAD (PrecioVenta - CostoVenta)
            var monthPrecioSum = await _ventaRepository.SumPrecioVentaPaidInRangeAsync(startOfMonth, endOfMonth);
            var monthCostoSum = await _ventaRepository.SumCostoVentaPaidInRangeAsync(startOfMonth, endOfMonth);
            var monthUtilidad = monthPrecioSum - monthCostoSum;

            // 4. GASTOS MERCADERIA (Categoria "MERCADERIA")
            var monthGastosMercaderia = await _context.MovimientosCaja
                .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate && m.Monto < 0 && m.Categoria == "MERCADERIA")
                .SumAsync(m => m.Monto);

            // [NEW] COSTO DE VENTA (Suma de los costos de los productos vendidos)
            var monthCostoVenta = monthCostoSum;

            // [NEW] MERMAS (Category "MERMA") - Treated as negative revenue or expense? User pic says "(-) Mermas".
            // Typically Mermas are negative amounts in MovimientosCaja.
            var monthMermas = await _context.MovimientosCaja
                .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate && m.Monto < 0 && m.Categoria == "MERMA")
                .SumAsync(m => m.Monto);
            decimal mermasAbs = Math.Abs(monthMermas);
            
            // Re-adding missing variable
            decimal gastosMercaderiaAbs = Math.Abs(monthGastosMercaderia); 

            // [NEW] GASTOS VARIABLES (Logística)
            // Categories: LOGISTICA, PEAJES, INSUMOS, MANTENCION
            var categoriesVariables = new[] { "LOGISTICA", "PEAJES", "INSUMOS", "MANTENCION" };
            var monthGastosVariables = await _context.MovimientosCaja
                 .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate && m.Monto < 0 && categoriesVariables.Contains(m.Categoria))
                 .SumAsync(m => m.Monto);
            decimal gastosVariablesAbs = Math.Abs(monthGastosVariables);

            // [NEW] GASTOS FIJOS (Estructurales)
            // Categories: INFRA, ARRIENDO_POS, INTERNET, COMISIONES, OTROS
            var categoriesFijos = new[] { "INFRA", "ARRIENDO_POS", "INTERNET", "COMISIONES", "OTROS" };
             var monthGastosFijos = await _context.MovimientosCaja
                 .Where(m => m.Fecha >= startOfMonth && m.Fecha <= endOfMonth && m.Fecha >= _config.Value.CajaStartDate && m.Monto < 0 && categoriesFijos.Contains(m.Categoria))
                 .SumAsync(m => m.Monto);
            decimal gastosFijosAbs = Math.Abs(monthGastosFijos);

            // NOTE: GastosOperativos in DTO was previously "Fijos". Now strictly separated.
            // The user wants "Gastos Operativos" block split in two.
            
            // [NEW] UTILIDAD OPERACIONAL (EBITDA)
            // Formulas from user Image:
            // 1. Bloque Ventas: Ventas Brutas - Mermas
            decimal ventasNetas = monthIngresosVentas - mermasAbs; 

            // 2. Bloque Costo Directo:
            // Margen Bruto = (Ventas - CostoVenta). 
            // Note: Does "Ventas" here imply Net? Usually CostoVenta matches generated sales.
            // Let's stick to standard: Margen = (Ventas Brutas - Costo Venta) - Mermas? 
            // User pic: "(-) Costo de Venta". "(=) Margen Bruto".
            // Let's assume Mermas is a deduction from Gross Sales top level.
            decimal margenBruto = (monthIngresosVentas - monthCostoVenta); 

            // 3. Gastos
            decimal totalGastosOps = gastosVariablesAbs + gastosFijosAbs;

            decimal utilidadOperacional = margenBruto - mermasAbs - totalGastosOps;

            // 4. Resultado Final
            // decimal sueldoEsperado = 600000m; // REMOVED per user request
            decimal utilidadNetaReal = utilidadOperacional; // - sueldoEsperado;

            // [NEW] TRANSBANK (Estimado)
            // Contamos ventas PAGADAS (asumimos Transbank) y que NO sean fantasmas
            var excludedOrdenIds = new[] { "TB-EXTRA", "TB-SIN-VENTA" };
            var cantVentasTB = await _ventaRepository.CountPaidInRangeExcludingAsync(startOfMonth, endOfMonth, excludedOrdenIds);
            
            decimal costoTransbank = cantVentasTB * _config.Value.TransbankFee;

            return new CajaResumenDto
            {
                SaldoAnterior = saldoAnterior,
                IngresosVentas = monthIngresosVentas,
                
                // En el reporte, "Gastos Operativos" será la suma de Fijos + Variables para retrocompatibilidad
                GastosOperativos = totalGastosOps, 
                
                AportesExtra = monthAportes,
                SaldoFinal = saldoAnterior + monthIngresosVentas + monthAportes + monthGastos, 
                
                UtilidadTotal = margenBruto, 
                GastosMercaderia = gastosMercaderiaAbs,
                
                // Nuevos campos
                TotalCostoVenta = monthCostoVenta,
                Mermas = mermasAbs,
                GastosVariables = gastosVariablesAbs,
                GastosFijos = gastosFijosAbs,
                UtilidadOperacional = utilidadOperacional,
                UtilidadNeta = utilidadNetaReal,
                
                CantidadVentasTransbank = cantVentasTB,
                CostoTransbank = costoTransbank,

                IsLocked = IsMonthLocked(month, year)
            };
        }

        public async Task<List<MovimientoCaja>> GetMovimientosAsync(int month, int year)
        {
            return await _context.MovimientosCaja
                .Where(m => m.Fecha.Month == month && m.Fecha.Year == year && m.Fecha >= _config.Value.CajaStartDate)
                .OrderByDescending(m => m.Fecha)
                .ToListAsync();
        }

        public async Task RegistrarMovimientoAsync(MovimientoCaja mov)
        {
            // Allow 0 only if it is a MERMA (will be calculated later)
            if (mov.Monto == 0 && mov.Categoria != "MERMA") throw new ArgumentException("El monto no puede ser cero.");

            if (IsMonthLocked(mov.Fecha.Month, mov.Fecha.Year))
            {
                throw new InvalidOperationException($"El mes {mov.Fecha:MM/yyyy} está cerrado y no se puede modificar.");
            }

            // Validar que no se registren movimientos antes de la fecha global
            if (mov.Fecha < _config.Value.CajaStartDate)
            {
                // Opcional: Permitir registro pero advertir, o bloquear duro. 
                // Dado el requerimiento "desde el dia 18... hacer el cuadre", 
                // bloquear registros antiguos parece coherente para mantener la integridad.
                // Sin embargo, el usuario podría querer registrar algo antiguo como "histórico".
                // Pero si el sistema filtra por _config.Value.CajaStartDate, ese registro no se vería nunca.
                // Mejor lo dejamos pasar pero sabiendo que no sumará, O lanzamos error.
                // Voy a lanzar error para evitar data "fantasma".
                 throw new InvalidOperationException($"No se pueden registrar movimientos anteriores al inicio del cuadre ({_config.Value.CajaStartDate:dd/MM/yyyy}).");
            }

            if (mov.Fecha == DateTime.MinValue) mov.Fecha = DateTime.Now;

            // --- LOGICA DE MERMAS CON PRODUCTO ---
            if (mov.Categoria == "MERMA" && mov.ProductoId.HasValue && mov.ProductoId > 0)
            {
                var producto = await _context.Productos.FindAsync(mov.ProductoId);
                if (producto != null)
                {
                    // 1. Auto-Calculate Amount if not provided (or overwrite to ensure accuracy)
                    // Merma Amount = Costo Promedio * Cantidad
                    // We assume the user might input Qty but not accurate Cost Amount, so we calculate it.
                    // If user provided a specific Amount manually, we could respect it, but let's automate for consistency.
                    
                    decimal costoTotal = producto.CostoPromedio * mov.Cantidad;
                    
                    // Asegurar que sea negativo porque es GASTO
                    mov.Monto = -Math.Abs(costoTotal);
                    
                    // Update description to include details if not present
                    if (!mov.Descripcion.Contains(producto.Nombre))
                    {
                        mov.Descripcion = $"{mov.Descripcion} - {producto.Nombre} x{mov.Cantidad}";
                    }

                    // 2. Deduct Stock from Bodega
                    // We simply reduce the stock.
                    producto.StockBodega -= mov.Cantidad;
                    
                    // Optional: Prevent negative stock? 
                    // No, allow negative for correction flexibility, or clamp to 0? 
                    // Let's allow it for now to highlight discrepancies.
                    
                    _context.Productos.Update(producto);
                }
            }
            else
            {
                // Normal logic for other movements
                if (mov.Tipo == "GASTO" || mov.Tipo == "RETIRO")
                {
                    if (mov.Monto > 0) mov.Monto = -mov.Monto;
                }
                else
                {
                    if (mov.Monto < 0) mov.Monto = -mov.Monto;
                }
            }

            _context.MovimientosCaja.Add(mov);
            await _context.SaveChangesAsync();
        }



        public bool IsMonthLocked(int month, int year)
        {
            DateTime now = DateTime.Now;
            DateTime targetDateEnd = new DateTime(year, month, 1).AddMonths(1).AddSeconds(-1);

            if (targetDateEnd >= now) return false;

            DateTime lockDate = targetDateEnd.AddDays(5);
            // return now > lockDate; // Deshabilitado temporalmente en original
            return false;
        }

        public async Task<(byte[] content, string fileName)> ExportarCajaAsync(int month, int year)
        {
            DateTime startOfMonth = new DateTime(year, month, 1);
            DateTime endOfMonth = startOfMonth.AddMonths(1).AddSeconds(-1);

            var resumen = await GetResumenAsync(month, year);
            var movimientos = await _context.MovimientosCaja
                .Where(m => m.Fecha.Month == month && m.Fecha.Year == year && m.Fecha >= _config.Value.CajaStartDate)
                .OrderBy(m => m.Fecha)
                .ToListAsync();

            var ventas = await _ventaRepository.GetPaidInRangeAsync(startOfMonth, endOfMonth);

            var bytes = await _excelExportService.ExportCajaReportAsync(resumen, movimientos, ventas, month, year);
            return (bytes, $"Reporte_{month}_{year}.xlsx");
        }

        public async Task<(byte[] content, string fileName)> ExportarMovimientosAsync(int month, int year)
        {
            var movimientos = await _context.MovimientosCaja
                .Where(m => m.Fecha.Month == month && m.Fecha.Year == year && m.Fecha >= _config.Value.CajaStartDate)
                .OrderBy(m => m.Fecha)
                .ToListAsync();

            var bytes = await _excelExportService.ExportMovimientosAsync(movimientos, month, year);
            return (bytes, $"Caja_{month}_{year}.xlsx");
        }
        public async Task<ValorizacionStockDto> GetValorizacionStockAsync()
        {
            // 1. Bodega: Sum(StockBodega * CostoPromedio)
            var valorBodega = await _context.Productos
                .SumAsync(p => p.StockBodega * p.CostoPromedio);

            // 2. Maquinas: Sum(StockActual * CostoPromedio)
            var valorMaquinas = await _context.ConfiguracionSlots
                .Include(s => s.Producto)
                .Where(s => s.ProductoId != null && s.Producto != null)
                .SumAsync(s => s.StockActual * s.Producto!.CostoPromedio);

            return new ValorizacionStockDto
            {
                ValorBodega = valorBodega,
                ValorMaquinas = valorMaquinas
            };
        }
    }
}
