using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore; 
using VendingManager.Data;
using VendingManager.Models;
using VendingManager.Services;

namespace VendingManager.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VentasController : ControllerBase
    {
        private readonly ExcelService _excelService;
        private readonly ApplicationDbContext _context; // 1. Nueva variable para la BD

        // 2. Modificamos el constructor para recibir AMBOS servicios
        public VentasController(ExcelService excelService, ApplicationDbContext context)
        {
            _excelService = excelService;
            _context = context;
        }

        [HttpPost("subir-ventas-maquina")]
        public async Task<IActionResult> SubirVentasMaquina(IFormFile file, [FromServices] ExcelService excelService)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Por favor sube un archivo válido.");

            try
            {
                using (var stream = file.OpenReadStream())
                {
                    await excelService.ImportarVentasMaquina(stream, file.FileName);
                }

                return Ok("✅ Archivo procesado correctamente. Revisa la consola para detalles.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error interno: " + ex.Message);
            }
        }

        [HttpPost("subir-transbank")]
        public async Task<IActionResult> SubirTransbank([FromForm] IFormFile archivo)
        {
            if (archivo == null || archivo.Length == 0)
                return BadRequest("Sube el archivo de Transbank.");

            // Validar que no sea el formato antiguo .xls
            var extension = Path.GetExtension(archivo.FileName).ToLower();
            if (extension == ".xls")
            {
                return BadRequest("El formato .xls (antiguo) no es compatible. Por favor abre el archivo en Excel y guárdalo como .xlsx o .csv");
            }

            try
            {
                using (var stream = archivo.OpenReadStream())
                {
                    // CAMBIO AQUÍ: Le pasamos archivo.FileName
                    await _excelService.ImportarTransbank(stream, archivo.FileName);
                }
                return Ok(new { mensaje = "Proceso Transbank finalizado. Revisa la consola para ver los resultados." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error: " + ex.Message);
            }
        }

        [HttpGet("historial")]
        public async Task<IActionResult> ObtenerHistorialVentas()
        {
            var ventas = await _context.Ventas
                .Include(v => v.Maquina)
                .Include(v => v.Producto)
                .OrderByDescending(v => v.FechaHora)
                .Take(100)
                .Select(v => new
                {
                    Id = v.Id,
                    Fecha = v.FechaHora.ToString("yyyy-MM-dd HH:mm"),
                    Maquina = v.Maquina.Nombre,
                    Slot = v.NumeroSlot,
                    Producto = v.Producto != null ? v.Producto.Nombre : "SIN CONFIGURAR",
                    PrecioVenta = v.PrecioVenta,

                    // --- AGREGA ESTA LÍNEA ---
                    Pagado = v.Pagado,
                    // -------------------------

                    Ganancia = v.Producto != null ? (v.PrecioVenta - v.Producto.CostoPromedio) : 0
                })
                .ToListAsync();

            return Ok(ventas);
        }

        [HttpGet("balance-total")]
        public async Task<IActionResult> ObtenerBalance()
        {
            var reporte = await _context.Ventas
                .Include(v => v.Producto)
                .GroupBy(v => 1) // Agrupar todo en un solo paquete
                .Select(g => new
                {
                    TotalVentas = g.Count(), // Cantidad de snacks vendidos
                    IngresoBruto = g.Sum(v => v.PrecioVenta), // Dinero total que entró a la máquina
                    CostoMercaderia = g.Sum(v => v.Producto != null ? v.Producto.CostoPromedio : 0), // Lo que te costó comprar esos snacks
                    // La resta es tu ganancia bruta
                    UtilidadBruta = g.Sum(v => v.PrecioVenta) - g.Sum(v => v.Producto != null ? v.Producto.CostoPromedio : 0)
                })
                .FirstOrDefaultAsync();

            return Ok(reporte);
        }


        [HttpGet("reporte-rango")]
        public async Task<IActionResult> ObtenerReporteRango(DateTime? inicio, DateTime? fin, int? maquinaId)
        {
            var fechaInicio = inicio ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var fechaFin = fin ?? DateTime.Now;

            // 1. Empezamos la consulta
            var query = _context.Ventas
                .Include(v => v.Maquina)
                .Where(v => v.FechaHora >= fechaInicio && v.FechaHora <= fechaFin);

            // 2. APLICAMOS EL FILTRO SI SELECCIONARON UNA MÁQUINA
            if (maquinaId.HasValue && maquinaId.Value > 0)
            {
                query = query.Where(v => v.MaquinaId == maquinaId.Value);
            }

            var ventas = await query.ToListAsync();

            // 3. Generamos el resumen (igual que antes)
            var resumen = new
            {
                TotalVentas = ventas.Count,
                MontoTotal = ventas.Sum(v => v.PrecioVenta),
                MontoPagado = ventas.Where(v => v.Pagado).Sum(v => v.PrecioVenta),
                MontoPendiente = ventas.Where(v => !v.Pagado).Sum(v => v.PrecioVenta),
                Detalle = ventas.OrderByDescending(v => v.FechaHora).Select(v => new
                {
                    Fecha = v.FechaHora.ToString("dd/MM/yyyy HH:mm"), // Para mostrar (Texto)
                    FechaRaw = v.FechaHora,                           // <--- AGREGA ESTO (Para ordenar)
                    Monto = v.PrecioVenta,
                    Estado = v.Pagado ? "Pagado" : "Pendiente",
                    Slot = v.NumeroSlot,
                    Maquina = v.Maquina.Nombre
                })
            };

            return Ok(resumen);
        }
        [HttpGet("lista-maquinas")]
        public async Task<IActionResult> ObtenerMaquinas()
        {
            var maquinas = await _context.Maquinas
                .Select(m => new { m.Id, m.Nombre, m.Ubicacion }) // ¡Ahora sí funciona!
                .ToListAsync();
            return Ok(maquinas);
        }

        // DTO para enviar los 3 bloques de datos
        public class DashboardStats
        {
            // Agregamos "= new();" para que nazcan inicializadas
            public PeriodoStats Hoy { get; set; } = new();
            public PeriodoStats Semana { get; set; } = new();
            public PeriodoStats Mes { get; set; } = new();
        }

        public class PeriodoStats
        {
            public decimal VentaTotal { get; set; }
            public decimal PagadoTB { get; set; }
            public decimal Pendiente { get; set; }
            public int CantidadVentas { get; set; }
        }

        [HttpGet("dashboard-stats")]
        public async Task<IActionResult> ObtenerDashboard(int? maquinaId)
        {
            // 1. Base de la consulta (Filtro por máquina si existe)
            var query = _context.Ventas.AsQueryable();
            if (maquinaId.HasValue && maquinaId.Value > 0)
            {
                query = query.Where(v => v.MaquinaId == maquinaId.Value);
            }

            var hoy = DateTime.Today;

            // Calcular inicio de semana (Lunes)
            var diaSemana = (int)hoy.DayOfWeek; // 0 = Domingo, 1 = Lunes...
            if (diaSemana == 0) diaSemana = 7; // Ajuste para que Domingo sea el último
            var inicioSemana = hoy.AddDays(-(diaSemana - 1));

            var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);

            // 2. Traemos los datos en memoria (Eficiente para volúmenes medios)
            // Nota: Si tienes millones de ventas, esto se optimiza con GroupBy en SQL.
            var ventasMes = await query.Where(v => v.FechaHora >= inicioMes).ToListAsync();

            // 3. Filtramos en memoria para llenar las cajas
            var ventasHoy = ventasMes.Where(v => v.FechaHora.Date == hoy).ToList();
            var ventasSemana = ventasMes.Where(v => v.FechaHora.Date >= inicioSemana).ToList();

            // Función local para calcular resumen
            PeriodoStats Calcular(List<Venta> lista) => new PeriodoStats
            {
                CantidadVentas = lista.Count,
                VentaTotal = lista.Sum(v => v.PrecioVenta),
                PagadoTB = lista.Where(v => v.Pagado).Sum(v => v.PrecioVenta),
                Pendiente = lista.Where(v => !v.Pagado).Sum(v => v.PrecioVenta)
            };

            var resultado = new DashboardStats
            {
                Hoy = Calcular(ventasHoy),
                Semana = Calcular(ventasSemana),
                Mes = Calcular(ventasMes)
            };

            return Ok(resultado);
        }

        [HttpPost("sincronizar-nube")]
        public async Task<IActionResult> SincronizarNube(DateTime? fecha, [FromServices] SeleniumSyncService bot, [FromServices] ExcelService excel)
        {
            DateTime inicio = fecha ?? DateTime.Now;
            DateTime fin = inicio;

            using (bot)
            {
                var streamExcel = await bot.DescargarReporte(inicio, fin);

                if (streamExcel == null) return NotFound("Error Selenium.");

                try
                {
                    // CORREGIDO: Pasamos las 4 cosas para activar el filtro
                    await excel.ImportarVentasMaquina(streamExcel, "selenium.xls");
                    return Ok("✅ Listo.");
                }
                catch (Exception ex)
                {
                    return StatusCode(500, ex.Message);
                }
            }
        }
    }
}