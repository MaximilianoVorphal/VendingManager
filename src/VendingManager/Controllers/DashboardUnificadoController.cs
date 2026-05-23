using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

namespace VendingManager.Controllers;

[Route("api/dashboard")]
[ApiController]
[Microsoft.AspNetCore.Authorization.Authorize]
public class DashboardUnificadoController(
    ISalesAnalyticsService salesAnalyticsService,
    ITransferenciaService transferenciaService,
    ICompraService compraService,
    IGastoRecurrenteService gastoRecurrenteService,
    ICajaService cajaService,
    IPurchasingService purchasingService,
    IVentaRepository ventaRepository,
    IMemoryCache memoryCache) : ControllerBase
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Endpoint único que agrega stats, alertas y actividad reciente de los 6 servicios.
    /// Optional maquinaId (default=0 → todas las máquinas).
    /// Optional count para cantidad de entradas de actividad reciente (default=20).
    /// Respuesta cacheada 60s por maquinaId.
    /// </summary>
    [HttpGet("unificado")]
    public async Task<IActionResult> GetUnificado([FromQuery] int maquinaId = 0, [FromQuery] int count = 20)
    {
        var cacheKey = $"dashboard_{maquinaId}";

        if (memoryCache.TryGetValue(cacheKey, out DashboardUnificadoDto? cached) && cached != null)
        {
            return Ok(cached);
        }

        var today = DateTime.Now;
        var startOfMonth = new DateTime(today.Year, today.Month, 1);

        // Fire all queries in parallel — each wrapped in try/catch for partial resilience
        var statsTask = TryGetDashboardStats(maquinaId);
        var transferenciasTask = TryGetTransferenciasPendientes();
        var comprasTask = TryGetCompras();
        var gastosTask = TryGetGastosPendientesDelMes(today.Month, today.Year);
        var movimientosTask = TryGetMovimientosCaja(today.Month, today.Year);
        var stockCriticoTask = TryGetStockCritico(maquinaId);
        var transferenciasNoVinculadasTask = TryGetTransferenciasNoVinculadas();
        var comprasNoVinculadasTask = TryGetComprasNoVinculadas();
        var gastosNoVinculadosTask = TryGetGastosNoVinculados();
        var ventasTask = TryGetVentasRecientes(maquinaId, count);

        await Task.WhenAll(
            statsTask,
            transferenciasTask,
            comprasTask,
            gastosTask,
            movimientosTask,
            stockCriticoTask,
            transferenciasNoVinculadasTask,
            comprasNoVinculadasTask,
            gastosNoVinculadosTask,
            ventasTask);

        var stats = statsTask.Result;
        var transferencias = transferenciasTask.Result;
        var compras = comprasTask.Result;
        var gastosPendientes = gastosTask.Result;
        var movimientos = movimientosTask.Result;
        var stockCritico = stockCriticoTask.Result;
        var transferenciasNoVinculadas = transferenciasNoVinculadasTask.Result;
        var comprasNoVinculadas = comprasNoVinculadasTask.Result;
        var gastosNoVinculados = gastosNoVinculadosTask.Result;
        var ventas = ventasTask.Result;

        // ── Pipeline ──────────────────────────────────────────────────────
        var transferenciasActivas = transferencias.Sum(t => t.Monto);
        var comprasVinculadas = compras
            .Where(c => c.TransferenciaId != null && transferencias.Any(t => t.Id == c.TransferenciaId))
            .Sum(c => c.MontoTotal);
        var gastosVinculados = gastosPendientes.Sum(g => g.MontoEstimado);
        var conciliacion = transferenciasActivas - comprasVinculadas - gastosVinculados;

        var pipeline = new PipelineFinancieroDto
        {
            VentasMes = stats?.Mes?.VentaTotal ?? 0m,
            CantidadVentasMes = stats?.Mes?.CantidadVentas ?? 0,
            TransferenciasActivas = transferenciasActivas,
            CantidadTransferencias = transferencias.Count,
            ComprasVinculadas = comprasVinculadas,
            GastosVinculados = gastosVinculados,
            Conciliacion = conciliacion,
            IsPositiva = conciliacion >= 0
        };

        // ── Alertas ──────────────────────────────────────────────────────
        var alertas = BuildAlertas(transferenciasNoVinculadas, stockCritico, gastosPendientes, comprasNoVinculadas);

        // ── Actividad Reciente ───────────────────────────────────────────
        var actividad = BuildActividadReciente(transferencias, compras, movimientos, ventas, maquinaId, count);

        var dto = new DashboardUnificadoDto
        {
            Pipeline = pipeline,
            Alertas = alertas,
            Actividad = actividad
        };

        memoryCache.Set(cacheKey, dto, CacheDuration);
        return Ok(dto);
    }

    private async Task<DashboardStats?> TryGetDashboardStats(int maquinaId)
    {
        try { return await salesAnalyticsService.GetDashboardStatsAsync(maquinaId); }
        catch { return null; }
    }

    private async Task<IReadOnlyList<Transferencia>> TryGetTransferenciasPendientes()
    {
        try { return (await transferenciaService.GetTransferenciasPendientesAsync()).ToList(); }
        catch { return Array.Empty<Transferencia>(); }
    }

    private async Task<IReadOnlyList<Compra>> TryGetCompras()
    {
        try { return (await compraService.GetComprasAsync(200)).ToList(); }
        catch { return Array.Empty<Compra>(); }
    }

    private async Task<IReadOnlyList<GastoPendienteDto>> TryGetGastosPendientesDelMes(int month, int year)
    {
        try { return await gastoRecurrenteService.GetPendientesDelMesAsync(month, year); }
        catch { return Array.Empty<GastoPendienteDto>(); }
    }

    private async Task<IReadOnlyList<MovimientoCaja>> TryGetMovimientosCaja(int month, int year)
    {
        try { return (await cajaService.GetMovimientosAsync(month, year)).ToList(); }
        catch { return Array.Empty<MovimientoCaja>(); }
    }

    private async Task<IReadOnlyList<StockCriticoDto>> TryGetStockCritico(int maquinaId)
    {
        try { return await purchasingService.GetStockCriticoAsync(maquinaId); }
        catch { return Array.Empty<StockCriticoDto>(); }
    }

    private async Task<IReadOnlyList<Transferencia>> TryGetTransferenciasNoVinculadas()
    {
        try { return (await transferenciaService.GetTransferenciasNoVinculadasAsync()).ToList(); }
        catch { return Array.Empty<Transferencia>(); }
    }

    private async Task<IReadOnlyList<Compra>> TryGetComprasNoVinculadas()
    {
        try { return (await compraService.GetComprasNoVinculadasAsync()).ToList(); }
        catch { return Array.Empty<Compra>(); }
    }

    private async Task<IReadOnlyList<MovimientoCaja>> TryGetGastosNoVinculados()
    {
        try { return await cajaService.GetGastosNoVinculadosAsync(); }
        catch { return Array.Empty<MovimientoCaja>(); }
    }

    private async Task<IReadOnlyList<Venta>> TryGetVentasRecientes(int maquinaId, int count)
    {
        try
        {
            int? maquinaFilter = maquinaId > 0 ? maquinaId : null;
            return await ventaRepository.GetRecentAsync(count, maquinaFilter);
        }
        catch { return Array.Empty<Venta>(); }
    }

    private static AlertaConsolidadaDto BuildAlertas(
        IReadOnlyList<Transferencia> transferenciasNoVinculadas,
        IReadOnlyList<StockCriticoDto> stockCritico,
        IReadOnlyList<GastoPendienteDto> gastosPendientes,
        IReadOnlyList<Compra> comprasNoVinculadas)
    {
        var items = new List<ItemAlertaDto>();

        // Stock crítico
        foreach (var s in stockCritico)
        {
            items.Add(new ItemAlertaDto
            {
                Tipo = "stock-critico",
                Mensaje = $"{s.Producto} en {s.Maquina} slot {s.NumeroSlot} — stock {s.StockActual}/{s.CapacidadMaxima}",
                Severidad = "danger",
                LinkUrl = "/productos"
            });
        }

        // Transferencias sin rendir
        if (transferenciasNoVinculadas.Count > 0)
        {
            items.Add(new ItemAlertaDto
            {
                Tipo = "transferencias-sin-rendir",
                Mensaje = $"{transferenciasNoVinculadas.Count} transferencia(s) pendiente(s) sin rendición",
                Severidad = "danger",
                LinkUrl = "/rendiciones"
            });
        }

        // Gastos fijos no registrados
        foreach (var g in gastosPendientes)
        {
            items.Add(new ItemAlertaDto
            {
                Tipo = "gastos-fijos-no-registrados",
                Mensaje = $"Gasto recurrente no registrado: {g.Descripcion} (estimado ${g.MontoEstimado:N0})",
                Severidad = "warning",
                LinkUrl = "/gastos-recurrentes"
            });
        }

        // Compras sin factura
        var sinFactura = comprasNoVinculadas.Where(c => string.IsNullOrEmpty(c.TipoFactura)).ToList();
        if (sinFactura.Count > 0)
        {
            items.Add(new ItemAlertaDto
            {
                Tipo = "compras-sin-factura",
                Mensaje = $"{sinFactura.Count} compra(s) sin tipo de factura",
                Severidad = "info",
                LinkUrl = "/compras"
            });
        }

        return new AlertaConsolidadaDto
        {
            Total = items.Count,
            Items = items
        };
    }

    private static List<ActividadRecienteDto> BuildActividadReciente(
        IReadOnlyList<Transferencia> transferencias,
        IReadOnlyList<Compra> compras,
        IReadOnlyList<MovimientoCaja> movimientos,
        IReadOnlyList<Venta> ventas,
        int maquinaId,
        int count)
    {
        var entries = new List<ActividadRecienteDto>();

        // Ventas — filtradas por maquinaId cuando > 0
        foreach (var v in ventas)
        {
            if (maquinaId > 0 && v.MaquinaId != maquinaId) continue;
            entries.Add(new ActividadRecienteDto
            {
                Fecha = v.FechaLocal,
                Tipo = "venta",
                Monto = v.PrecioVenta,
                Descripcion = $"Venta slot {v.NumeroSlot} — ${v.PrecioVenta:N0}",
                LinkUrl = "/informe-ventas",
                MaquinaId = v.MaquinaId
            });
        }

        // Transferencias — globales (no filtradas por máquina)
        foreach (var t in transferencias)
        {
            entries.Add(new ActividadRecienteDto
            {
                Fecha = t.Fecha,
                Tipo = "transferencia",
                Monto = t.Monto,
                Descripcion = $"Transferencia a {t.Trabajador} — {t.Estado}",
                LinkUrl = "/rendiciones"
            });
        }

        // Compras — globales
        foreach (var c in compras)
        {
            entries.Add(new ActividadRecienteDto
            {
                Fecha = c.FechaCompra,
                Tipo = "compra",
                Monto = c.MontoTotal,
                Descripcion = $"Compra a {c.Proveedor} — {c.Estado}",
                LinkUrl = "/compras"
            });
        }

        // Movimientos de caja — globales
        foreach (var m in movimientos)
        {
            entries.Add(new ActividadRecienteDto
            {
                Fecha = m.Fecha,
                Tipo = "movimiento_caja",
                Monto = m.Monto,
                Descripcion = m.Descripcion,
                LinkUrl = "/caja"
            });
        }

        return entries
            .OrderByDescending(e => e.Fecha)
            .Take(count)
            .ToList();
    }
}