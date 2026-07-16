using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using VendingManager.Core.Configuration;
using VendingManager.Core.Domain;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;

namespace VendingManager.Infrastructure.Services;

public class ContabilidadService : IContabilidadService
{
    private readonly ApplicationDbContext _context;
    private readonly IAccountingPeriodRepository _periodRepository;
    private readonly IMemoryCache? _cache;
    private readonly VendingConfig _vendingConfig;

    public ContabilidadService(ApplicationDbContext context, IAccountingPeriodRepository periodRepository)
        : this(context, periodRepository, null, Options.Create(new VendingConfig()))
    {
    }

    public ContabilidadService(
        ApplicationDbContext context,
        IAccountingPeriodRepository periodRepository,
        IMemoryCache? cache,
        IOptions<VendingConfig> config)
    {
        _context = context;
        _periodRepository = periodRepository;
        _cache = cache;
        _vendingConfig = config?.Value ?? new VendingConfig();
    }

    public async Task<Transferencia> CrearTransferenciaConMovimientoAsync(
        TransferenciaConMovimientoRequest request,
        CancellationToken ct = default)
    {
        await using var transaction = await _context.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        try
        {
            // 1. Get or auto-create Rendicion
            Rendicion rendicion;
            if (request.RendicionId > 0)
            {
                rendicion = await _context.Rendiciones
                    .FirstOrDefaultAsync(r => r.Id == request.RendicionId, ct)
                    ?? throw new KeyNotFoundException($"Rendicion {request.RendicionId} no encontrada.");
            }
            else
            {
                // Auto-create a generic rendicion for period-based transfers
                rendicion = new Rendicion
                {
                    Trabajador = !string.IsNullOrWhiteSpace(request.Trabajador) ? request.Trabajador : "General",
                    FechaInicio = request.Fecha,
                    Observaciones = "Auto-creada para transferencia sin rendición"
                };
                _context.Rendiciones.Add(rendicion);
                await _context.SaveChangesAsync(ct);
            }

            var rendicionId = rendicion.Id;

            // 2. Create Transferencia
            var transferencia = new Transferencia
            {
                Fecha = request.Fecha,
                Monto = request.Monto,
                Descripcion = request.Descripcion,
                Trabajador = request.Trabajador,
                Estado = TransferenciaEstado.Pendiente,
                RendicionId = rendicionId,
                PeriodoId = request.PeriodoId
            };
            _context.Transferencias.Add(transferencia);
            await _context.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);
            return transferencia;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Crea un cuadre completo: un AccountingPeriod con una única Transferencia (1:1),
    /// más su Rendicion auto-creada para colgar gastos. Cada cuadre es una hoja
    /// independiente. Todo en una transacción.
    /// </summary>
    public async Task<CuadreCreadoDto> CrearCuadreAsync(
        CrearCuadreRequest request,
        CancellationToken ct = default)
    {
        if (request.Monto <= 0)
            throw new InvalidOperationException("El monto debe ser mayor a cero.");

        var trabajador = string.IsNullOrWhiteSpace(request.Trabajador)
            ? "General"
            : request.Trabajador.Trim();

        await using var transaction = await _context.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        try
        {
            // 1. Período propio del cuadre (1:1 con la transferencia)
            var period = new AccountingPeriod
            {
                Name = $"Rendición {trabajador} {request.Fecha:dd/MM/yyyy}",
                FechaInicio = request.Fecha,
                FechaFin = request.Fecha,
                Trabajador = trabajador,
                Estado = AccountingPeriodEstado.Abierto
            };
            _context.AccountingPeriods.Add(period);
            await _context.SaveChangesAsync(ct);

            // 2. Rendicion auto-creada para vincular gastos a esta transferencia
            var rendicion = new Rendicion
            {
                Trabajador = trabajador,
                FechaInicio = request.Fecha,
                Observaciones = "Auto-creada para cuadre de transferencia"
            };
            _context.Rendiciones.Add(rendicion);
            await _context.SaveChangesAsync(ct);

            // 3. Transferencia única, ligada al período y a su rendición
            var transferencia = new Transferencia
            {
                Fecha = request.Fecha,
                Monto = request.Monto,
                Descripcion = request.Descripcion,
                Trabajador = trabajador,
                Estado = TransferenciaEstado.Pendiente,
                RendicionId = rendicion.Id,
                PeriodoId = period.Id
            };
            _context.Transferencias.Add(transferencia);
            await _context.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);
            return new CuadreCreadoDto { PeriodoId = period.Id, TransferenciaId = transferencia.Id };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<Compra> CrearCompraVinculadaAsync(
        CompraVinculadaRequest request,
        CancellationToken ct = default)
    {
        await using var transaction = await _context.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        try
        {
            // 1. Verify Transferencia exists
            var transferencia = await _context.Transferencias
                .FirstOrDefaultAsync(t => t.Id == request.TransferenciaId, ct)
                ?? throw new KeyNotFoundException($"Transferencia {request.TransferenciaId} no encontrada.");

            if (transferencia.Estado == TransferenciaEstado.Conciliado)
                throw new InvalidOperationException("No se puede vincular una compra a una transferencia ya conciliada.");

            // 2. Create Compra entity from request
            var compra = new Compra
            {
                FechaCompra = request.FechaCompra,
                Proveedor = request.Proveedor,
                NumeroDocumento = request.NumeroDocumento,
                Estado = request.Estado,
                TipoFactura = request.TipoFactura,
                PagadaCaja = request.PagadaCaja,
                Trabajador = request.Trabajador,
                TransferenciaId = request.TransferenciaId,
                Detalles = request.Detalles.Select(d => new DetalleCompra
                {
                    ProductoId = d.ProductoId,
                    DescripcionItem = d.DescripcionItem,
                    Cantidad = d.Cantidad,
                    CostoUnitario = d.CostoUnitario,
                    Subtotal = d.Cantidad * d.CostoUnitario,
                    EsPendiente = d.EsPendiente
                }).ToList()
            };
            compra.MontoTotal = compra.Detalles.Where(d => !d.EsPendiente).Sum(d => d.Subtotal);

            _context.Compras.Add(compra);
            await _context.SaveChangesAsync(ct);

            // 3. Update Transferencia estado: Pendiente → EnUso
            if (transferencia.Estado == TransferenciaEstado.Pendiente)
            {
                transferencia.Estado = TransferenciaEstado.EnUso;
                _context.Transferencias.Update(transferencia);
            }

            // 4. Update Producto stock + CostoPromedio (CPP formula from CompraService)
            foreach (var detalle in compra.Detalles.Where(d => d.ProductoId.HasValue && !d.EsPendiente))
            {
                var producto = await _context.Productos.FindAsync(new object[] { detalle.ProductoId!.Value }, ct);
                if (producto != null)
                {
                    producto.CostoPromedio = CalculadoraCostos.ApplyPurchase(
                        producto.StockBodega, producto.CostoPromedio,
                        detalle.Cantidad, detalle.CostoUnitario,
                        out int nuevoStockTotal);
                    producto.StockBodega = nuevoStockTotal;
                    _context.Productos.Update(producto);
                }
            }

            // 5. Insert ProductoCosto rows (close open rows, create new)
            foreach (var item in compra.Detalles.Where(d => d.ProductoId.HasValue && !d.EsPendiente).GroupBy(d => d.ProductoId))
            {
                var productoId = item.Key!.Value;
                var costoUnitario = item.First().CostoUnitario;

                // Close open rows
                var openRows = await _context.ProductoCostos
                    .Where(pc => pc.ProductoId == productoId && pc.FechaHasta == null)
                    .ToListAsync(ct);
                foreach (var row in openRows)
                    row.FechaHasta = compra.FechaCompra;

                // Create new row
                _context.ProductoCostos.Add(new ProductoCosto
                {
                    ProductoId = productoId,
                    Costo = costoUnitario,
                    FechaDesde = compra.FechaCompra,
                    FechaHasta = null
                });
            }

            // 6. Crear MovimientoCaja si PagadaCaja
            if (request.PagadaCaja)
            {
                var movimiento = new MovimientoCaja
                {
                    Fecha = compra.FechaCompra,
                    Descripcion = $"Factura/Boleta Nº {compra.NumeroDocumento} - {compra.Proveedor}",
                    Monto = -compra.MontoTotal,
                    Tipo = "GASTO",
                    Categoria = compra.TipoFactura == "MERCADERIA" ? "MERCADERIA" : "GASTOS GENERALES",
                    CompraId = compra.Id,
                    Trabajador = request.Trabajador,
                    RendicionId = request.RendicionId
                };
                _context.MovimientosCaja.Add(movimiento);
            }

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return compra;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<MovimientoCaja> CrearGastoVinculadoAsync(
        GastoVinculadoRequest request,
        CancellationToken ct = default)
    {
        await using var transaction = await _context.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        try
        {
            // 1. Verify Rendicion exists and is open
            var rendicion = await _context.Rendiciones
                .FirstOrDefaultAsync(r => r.Id == request.RendicionId, ct)
                ?? throw new KeyNotFoundException($"Rendicion {request.RendicionId} no encontrada.");

            if (rendicion.Estado == RendicionEstado.Cerrada)
                throw new InvalidOperationException("No se puede agregar un gasto a una rendición cerrada.");

            // 2. Create MovimientoCaja (GASTO)
            var movimiento = new MovimientoCaja
            {
                Fecha = request.Fecha,
                Descripcion = request.Descripcion,
                Monto = -Math.Abs(request.Monto),
                Tipo = "GASTO",
                Categoria = request.Categoria.ToUpperInvariant(),
                Trabajador = request.Trabajador,
                RendicionId = request.RendicionId
            };
            _context.MovimientosCaja.Add(movimiento);
            await _context.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);
            return movimiento;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<TrabajadorActivoDto>> GetTrabajadoresActivosAsync(
        CancellationToken ct = default)
    {
        var trabajadores = await _context.Rendiciones
            .Where(r => r.Estado == RendicionEstado.Abierta)
            .Select(r => r.Trabajador)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(ct);

        var result = new List<TrabajadorActivoDto>();
        foreach (var nombre in trabajadores)
        {
            var rendicionesAbiertas = await _context.Rendiciones
                .CountAsync(r => r.Trabajador == nombre && r.Estado == RendicionEstado.Abierta, ct);

            var rendicionActivaId = await _context.Rendiciones
                .Where(r => r.Trabajador == nombre && r.Estado == RendicionEstado.Abierta)
                .OrderByDescending(r => r.FechaInicio)
                .Select(r => (int?)r.Id)
                .FirstOrDefaultAsync(ct);

            result.Add(new TrabajadorActivoDto
            {
                Nombre = nombre,
                RendicionActivaId = rendicionActivaId,
                RendicionesAbiertas = rendicionesAbiertas
            });
        }

        return result;
    }

    public async Task DesvincularTransferenciaAsync(
        int transferenciaId,
        CancellationToken ct = default)
    {
        await using var transaction = await _context.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        try
        {
            var transferencia = await _context.Transferencias
                .Include(t => t.Compras)
                .FirstOrDefaultAsync(t => t.Id == transferenciaId, ct)
                ?? throw new KeyNotFoundException($"Transferencia {transferenciaId} no encontrada.");

            // Unlink from rendicion
            transferencia.RendicionId = null;

            // Update estado: if no linked compras remain → Pendiente, else keep EnUso
            transferencia.Estado = transferencia.Compras.Count == 0
                ? TransferenciaEstado.Pendiente
                : TransferenciaEstado.EnUso;

            _context.Transferencias.Update(transferencia);
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<EliminarTransferenciaResultDto> EliminarTransferenciaCuadreAsync(
        int transferenciaId, CancellationToken ct = default)
    {
        await using var transaction = await _context.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        int? periodoId = null;
        int comprasUnlinked = 0;

        try
        {
            // (a) Load Transferencia with Compras / AccountingPeriod / Rendicion
            var transferencia = await _context.Transferencias
                .Include(t => t.Compras)
                .FirstOrDefaultAsync(t => t.Id == transferenciaId, ct)
                ?? throw new KeyNotFoundException($"Transferencia {transferenciaId} no encontrada.");

            // (b) State guard — reject Conciliado
            if (transferencia.Estado == TransferenciaEstado.Conciliado)
                throw new InvalidOperationException("No se puede eliminar una transferencia ya conciliada.");

            // (c) Unlink all Compras
            comprasUnlinked = transferencia.Compras.Count;
            foreach (var compra in transferencia.Compras)
            {
                compra.TransferenciaId = null;
                _context.Compras.Update(compra);
            }

            // (d) If PeriodoId set → delete AccountingPeriod + Rendicion
            if (transferencia.PeriodoId.HasValue)
            {
                periodoId = transferencia.PeriodoId.Value;
                var period = await _context.AccountingPeriods
                    .FirstOrDefaultAsync(p => p.Id == periodoId.Value, ct);
                if (period is not null)
                {
                    _context.AccountingPeriods.Remove(period);
                }

                if (transferencia.RendicionId.HasValue)
                {
                    var rendicion = await _context.Rendiciones
                        .FirstOrDefaultAsync(r => r.Id == transferencia.RendicionId.Value, ct);
                    if (rendicion is not null)
                    {
                        _context.Rendiciones.Remove(rendicion);
                    }
                }
            }
            else
            {
                // (f) Legacy: PeriodoId == null → clear RendicionId so source Rendicion survives
                transferencia.RendicionId = null;
            }

            // (g) Delete the Transferencia row
            _context.Transferencias.Remove(transferencia);

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return new EliminarTransferenciaResultDto
        {
            ComprasUnlinked = comprasUnlinked,
            PeriodoId = periodoId
        };
    }

    public async Task VincularCompraExistenteAsync(
        int compraId,
        int transferenciaId,
        CancellationToken ct = default)
    {
        await using var transaction = await _context.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        try
        {
            var compra = await _context.Compras
                .FirstOrDefaultAsync(c => c.Id == compraId, ct)
                ?? throw new KeyNotFoundException($"Compra {compraId} no encontrada.");

            if (compra.TransferenciaId is not null)
                throw new InvalidOperationException("La compra ya está vinculada a una transferencia.");

            var transferencia = await _context.Transferencias
                .FirstOrDefaultAsync(t => t.Id == transferenciaId, ct)
                ?? throw new KeyNotFoundException($"Transferencia {transferenciaId} no encontrada.");

            if (transferencia.Estado == TransferenciaEstado.Conciliado)
                throw new InvalidOperationException("No se puede vincular una compra a una transferencia ya conciliada.");

            // Only link the existing purchase — stock/costs were already applied when it
            // was originally registered, so we must NOT re-apply them here.
            compra.TransferenciaId = transferenciaId;
            _context.Compras.Update(compra);

            if (transferencia.Estado == TransferenciaEstado.Pendiente)
            {
                transferencia.Estado = TransferenciaEstado.EnUso;
                _context.Transferencias.Update(transferencia);
            }

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task ActualizarMontoTransferenciaAsync(
        int transferenciaId,
        decimal nuevoMonto,
        CancellationToken ct = default)
    {
        await using var transaction = await _context.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        try
        {
            var transferencia = await _context.Transferencias
                .FirstOrDefaultAsync(t => t.Id == transferenciaId, ct)
                ?? throw new KeyNotFoundException($"Transferencia {transferenciaId} no encontrada.");

            if (transferencia.Estado == TransferenciaEstado.Conciliado)
                throw new InvalidOperationException("No se puede modificar una transferencia ya conciliada.");

            transferencia.Monto = nuevoMonto;
            _context.Transferencias.Update(transferencia);
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            throw new InvalidOperationException(
                "Otro usuario modificó esta transferencia. Recargá la página.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // ========== Edit Methods ==========

    public async Task<CompraDto> UpdateCompraAsync(
        int compraId, UpdateCompraRequest req, CancellationToken ct = default)
    {
        await using var transaction = await _context.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        try
        {
            var compra = await _context.Compras
                .Include(c => c.Detalles)
                .Include("Transferencia.AccountingPeriod")
                .FirstOrDefaultAsync(c => c.Id == compraId, ct)
                ?? throw new KeyNotFoundException($"Compra {compraId} no encontrada.");

            // Check period is not closed
            if (compra.Transferencia?.AccountingPeriod?.Estado == AccountingPeriodEstado.Cerrado)
                throw new InvalidOperationException("No se puede editar una compra de un período cerrado.");

            // Update scalar fields
            if (req.Proveedor != null)
                compra.Proveedor = req.Proveedor;
            if (req.NumeroDocumento != null)
                compra.NumeroDocumento = req.NumeroDocumento;
            if (req.Fecha.HasValue)
                compra.FechaCompra = req.Fecha.Value;
            if (req.Tipo != null)
                compra.TipoFactura = req.Tipo;

            // Replace detalles if provided
            if (req.Detalles != null)
            {
                _context.DetallesCompra.RemoveRange(compra.Detalles);
                compra.Detalles = req.Detalles.Select(d => new DetalleCompra
                {
                    CompraId = compra.Id,
                    ProductoId = d.ProductoId,
                    DescripcionItem = d.DescripcionItem,
                    Cantidad = d.Cantidad,
                    CostoUnitario = d.CostoUnitario,
                    EsPendiente = d.EsPendiente,
                    Subtotal = d.Cantidad * d.CostoUnitario
                }).ToList();
            }

            // Recalculate total (exclude pending items)
            compra.MontoTotal = compra.Detalles.Where(d => !d.EsPendiente).Sum(d => d.Subtotal);

            _context.Compras.Update(compra);
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return MapToCompraDto(compra);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<MovimientoCajaDto> UpdateGastoAsync(
        int gastoId, UpdateGastoRequest req, CancellationToken ct = default)
    {
        await using var transaction = await _context.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        try
        {
            var gasto = await _context.MovimientosCaja
                .Include("Rendicion.Transferencias.AccountingPeriod")
                .FirstOrDefaultAsync(m => m.Id == gastoId && m.Tipo == "GASTO", ct)
                ?? throw new KeyNotFoundException($"Gasto {gastoId} no encontrado.");

            // Check if any linked period is closed
            var hasClosedPeriod = gasto.Rendicion?.Transferencias
                ?.Any(t => t.AccountingPeriod?.Estado == AccountingPeriodEstado.Cerrado)
                ?? false;

            if (hasClosedPeriod)
                throw new InvalidOperationException("No se puede editar un gasto de un período cerrado.");

            // Update fields
            if (req.Descripcion != null)
                gasto.Descripcion = req.Descripcion;
            if (req.Monto.HasValue)
                gasto.Monto = -Math.Abs(req.Monto.Value); // keep negative convention
            if (req.Categoria != null)
                gasto.Categoria = req.Categoria.ToUpperInvariant();

            _context.MovimientosCaja.Update(gasto);
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return MapToMovimientoDto(gasto);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // ========== AccountingPeriod Methods ==========

    public async Task<List<AccountingPeriodDto>> GetPeriodosAsync(
        DateTime? desde, DateTime? hasta, CancellationToken ct = default)
    {
        // O-07: Cache-aside pattern for period list
        if (_cache is not null && _vendingConfig.UsePeriodCache)
        {
            var cacheKey = $"periods:{desde?.ToString("yyyyMMdd")}:{hasta?.ToString("yyyyMMdd")}";
            if (_cache.TryGetValue(cacheKey, out List<AccountingPeriodDto>? cached))
                return cached!;
        }

        var periods = await _periodRepository.GetByDateRangeAsync(desde, hasta, ct);
        var dtos = periods.Select(MapToDto).ToList();

        if (dtos.Count == 0)
            return dtos;

        // WARNING-1 fix: aggregate Devoluciones.Sum per period in ONE extra query (no N+1).
        // GetByDateRangeAsync does not Include Devoluciones, so MapToDto always yields Devuelto=0.
        // We merge the sums here so SaldoADevolver on the list is correct.
        var periodIds = dtos.Select(d => d.Id).ToList();
        var devueltoByPeriodo = await _context.Devoluciones
            .Where(d => d.PeriodoId != null && periodIds.Contains(d.PeriodoId!.Value))
            .GroupBy(d => d.PeriodoId!.Value)
            .Select(g => new { PeriodoId = g.Key, Total = g.Sum(d => d.Monto) })
            .ToListAsync(ct);

        if (devueltoByPeriodo.Count > 0)
        {
            var lookup = devueltoByPeriodo.ToDictionary(x => x.PeriodoId, x => x.Total);
            foreach (var dto in dtos)
            {
                if (lookup.TryGetValue(dto.Id, out var total))
                    dto.Devuelto = total;
            }
        }

        // O-01: Aggregate Transferencias totals per period
        var transferenciasByPeriodo = await _context.Transferencias
            .Where(t => t.PeriodoId != null && periodIds.Contains(t.PeriodoId!.Value))
            .GroupBy(t => t.PeriodoId!.Value)
            .Select(g => new { PeriodoId = g.Key, Total = g.Sum(t => t.Monto) })
            .ToListAsync(ct);

        if (transferenciasByPeriodo.Count > 0)
        {
            var lookup = transferenciasByPeriodo.ToDictionary(x => x.PeriodoId, x => x.Total);
            foreach (var dto in dtos)
            {
                if (lookup.TryGetValue(dto.Id, out var total))
                    dto.TotalTransferido = total;
            }
        }

        // O-01: Aggregate Compras totals per period (through Transferencias)
        var comprasByPeriodo = await _context.Compras
            .Where(c => c.Transferencia != null && c.Transferencia.PeriodoId != null && periodIds.Contains(c.Transferencia.PeriodoId!.Value))
            .GroupBy(c => c.Transferencia!.PeriodoId!.Value)
            .Select(g => new { PeriodoId = g.Key, Total = g.Sum(c => c.MontoTotal) })
            .ToListAsync(ct);

        if (comprasByPeriodo.Count > 0)
        {
            var lookup = comprasByPeriodo.ToDictionary(x => x.PeriodoId, x => x.Total);
            foreach (var dto in dtos)
            {
                if (lookup.TryGetValue(dto.Id, out var total))
                    dto.TotalCompras = total;
            }
        }

        // O-01: Aggregate Gastos totals per period (through Transferencias → Rendicion → Gastos)
        var gastosByPeriodo = await _context.MovimientosCaja
            .Where(g => g.Tipo == "GASTO" && g.Rendicion != null
                && g.Rendicion.Transferencias.Any(t => t.PeriodoId != null && periodIds.Contains(t.PeriodoId!.Value))
                && !CategoriasGasto.Estructurales.Contains(g.Categoria ?? string.Empty))
            .GroupBy(g => g.Rendicion!.Transferencias
                .Where(t => t.PeriodoId != null && periodIds.Contains(t.PeriodoId!.Value))
                .Select(t => t.PeriodoId!.Value)
                .FirstOrDefault())
            .Select(g => new { PeriodoId = g.Key, Total = g.Sum(x => Math.Abs(x.Monto)) })
            .ToListAsync(ct);

        if (gastosByPeriodo.Count > 0)
        {
            var lookup = gastosByPeriodo.ToDictionary(x => x.PeriodoId, x => x.Total);
            foreach (var dto in dtos)
            {
                if (lookup.TryGetValue(dto.Id, out var total))
                    dto.TotalGastos = total;
            }
        }

        // O-07: Store in cache after all aggregates are computed
        if (_cache is not null && _vendingConfig.UsePeriodCache)
        {
            var cacheKey = $"periods:{desde?.ToString("yyyyMMdd")}:{hasta?.ToString("yyyyMMdd")}";
            _cache.Set(cacheKey, dtos, TimeSpan.FromMinutes(_vendingConfig.PeriodCacheDurationMinutes));
        }

        return dtos;
    }

    public async Task<AccountingPeriodFullDto?> GetPeriodoFullAsync(
        int id, CancellationToken ct = default)
    {
        var period = await _periodRepository.GetFullByIdAsync(id, ct);
        if (period == null) return null;

        // TASK-10: query Devuelto directly to avoid Include nav-property edge cases
        var devuelto = await _context.Devoluciones
            .Where(d => d.PeriodoId == id)
            .SumAsync(d => d.Monto, ct);

        var dto = MapToFullDto(period);
        dto.Devuelto = devuelto;
        return dto;
    }

    public async Task<AccountingPeriodDto> CreatePeriodoAsync(
        CreatePeriodoRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new ArgumentException("El nombre del período es obligatorio.");

        if (req.FechaFin == default)
            req.FechaFin = req.FechaInicio.AddMonths(1).AddDays(-1);

        var period = new AccountingPeriod
        {
            Name = req.Name,
            FechaInicio = req.FechaInicio,
            FechaFin = req.FechaFin,
            Trabajador = req.Trabajador,
            Estado = AccountingPeriodEstado.Abierto
        };

        period = await _periodRepository.CreateAsync(period, ct);
        return MapToDto(period);
    }

    public async Task<AccountingPeriodDto> UpdatePeriodoAsync(
        int id, UpdatePeriodoRequest req, CancellationToken ct = default)
    {
        var period = await _periodRepository.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Período {id} no encontrado.");

        if (period.Estado == AccountingPeriodEstado.Cerrado)
            throw new InvalidOperationException("No se puede modificar un período cerrado.");

        if (req.Name != null) period.Name = req.Name;
        if (req.FechaInicio.HasValue) period.FechaInicio = req.FechaInicio.Value;
        if (req.FechaFin.HasValue) period.FechaFin = req.FechaFin.Value;
        if (req.Trabajador != null) period.Trabajador = req.Trabajador;

        await _periodRepository.UpdateAsync(period, ct);
        return MapToDto(period);
    }

    public async Task ClosePeriodoAsync(int id, CancellationToken ct = default)
    {
        var period = await _periodRepository.GetFullByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Período {id} no encontrado.");

        if (period.Estado == AccountingPeriodEstado.Cerrado)
            throw new InvalidOperationException("El período ya está cerrado.");

        // Close-gate validation delegated to CierreValidator (SDD endurecimiento-dominio Slice 2).
        // Collect operativo-real gastos from all rendiciones linked to this period's transfers.
        var gastosOperativos = period.Transferencias
            .Where(t => t.Rendicion != null)
            .SelectMany(t => t.Rendicion!.Gastos)
            .Where(m => CategoriasGasto.EsGastoOperativoReal(m.Categoria))
            .DistinctBy(g => g.Id)
            .ToList();

        var devuelto = await _context.Devoluciones
            .Where(d => d.PeriodoId == id)
            .SumAsync(d => d.Monto, ct);

        var validation = CierreValidator.Validate(
            period.Transferencias.ToList(),
            gastosOperativos,
            devuelto,
            "período");

        if (!validation.Valid)
            throw new InvalidOperationException(validation.ErrorMessage!);

        period.Estado = AccountingPeriodEstado.Cerrado;
        await _periodRepository.UpdateAsync(period, ct);
    }

    // ========== Delete AccountingPeriod ==========

    public async Task DeletePeriodoAsync(int id, CancellationToken ct = default)
    {
        await using var transaction = await _context.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        try
        {
            var period = await _context.AccountingPeriods
                .Include(p => p.Transferencias)
                .Include(p => p.Devoluciones)
                .FirstOrDefaultAsync(p => p.Id == id, ct)
                ?? throw new KeyNotFoundException($"Período {id} no encontrado.");

            // Unlink Transferencias (set PeriodoId = null, do NOT delete)
            foreach (var t in period.Transferencias)
            {
                t.PeriodoId = null;
            }

            // Unlink Devoluciones (set PeriodoId = null, do NOT touch MovimientoCajaId)
            foreach (var d in period.Devoluciones)
            {
                d.PeriodoId = null;
            }

            _context.AccountingPeriods.Remove(period);
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // ========== Slice 2: Verification (TASK-08) ==========

    public async Task MarcarTransferenciaVerificadaAsync(
        int transferenciaId,
        bool verificada,
        CancellationToken ct = default)
    {
        var transferencia = await _context.Transferencias
            .FirstOrDefaultAsync(t => t.Id == transferenciaId, ct)
            ?? throw new KeyNotFoundException($"Transferencia {transferenciaId} no encontrada.");

        transferencia.Verificada = verificada;
        _context.Transferencias.Update(transferencia);
        await _context.SaveChangesAsync(ct);
    }

    public async Task MarcarCompraVerificadaAsync(
        int compraId,
        bool verificada,
        CancellationToken ct = default)
    {
        var compra = await _context.Compras
            .FirstOrDefaultAsync(c => c.Id == compraId, ct)
            ?? throw new KeyNotFoundException($"Compra {compraId} no encontrada.");

        compra.Verificada = verificada;
        _context.Compras.Update(compra);
        await _context.SaveChangesAsync(ct);
    }

    // ========== Slice 2: Devolución (TASK-09) ==========

    public async Task<DevolucionDto> RegistrarDevolucionAsync(
        RegistrarDevolucionRequest request,
        CancellationToken ct = default)
    {
        // Validate: at least one target must be specified
        if (request.PeriodoId == null && request.RendicionId == null)
            throw new InvalidOperationException(
                "Debe especificar al menos un PeriodoId o RendicionId para la devolución.");

        // Validate Monto
        if (request.Monto <= 0)
            throw new InvalidOperationException("El monto de la devolución debe ser mayor a cero.");

        await using var transaction = await _context.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        try
        {
            // Validate target period is open (when provided)
            if (request.PeriodoId.HasValue)
            {
                var period = await _context.AccountingPeriods
                    .Include(p => p.Transferencias)
                        .ThenInclude(t => t.Compras)
                    .Include(p => p.Transferencias)
                        .ThenInclude(t => t.Rendicion)
                            .ThenInclude(r => r!.Gastos)
                    .FirstOrDefaultAsync(p => p.Id == request.PeriodoId.Value, ct)
                    ?? throw new KeyNotFoundException($"Período {request.PeriodoId.Value} no encontrado.");

                if (period.Estado == AccountingPeriodEstado.Cerrado)
                    throw new InvalidOperationException(
                        "No se puede registrar una devolución para un período cerrado.");

                // Idempotency: only one Devolucion per open period
                var existingAny = await _context.Devoluciones
                    .AnyAsync(d => d.PeriodoId == request.PeriodoId.Value, ct);
                if (existingAny)
                    throw new InvalidOperationException(
                        "Ya existe una devolución registrada para este período. Solo se permite una devolución por período en esta versión.");

                // Guard: Monto must not exceed SaldoADevolver for the period
                // (saldo calculation mirrors CierreValidator G3 — operativos reales only)
                var periodoTotalTransferido = period.Transferencias.Sum(t => t.Monto);
                var periodoTotalCompras = period.Transferencias
                    .SelectMany(t => t.Compras)
                    .Sum(c => c.MontoTotal);
                var periodoTotalGastos = period.Transferencias
                    .Where(t => t.Rendicion != null)
                    .SelectMany(t => t.Rendicion!.Gastos)
                    .Where(m => CategoriasGasto.EsGastoOperativoReal(m.Categoria))
                    .DistinctBy(g => g.Id)
                    .Sum(g => Math.Abs(g.Monto));
                var periodoDiferencia = periodoTotalTransferido - periodoTotalCompras - periodoTotalGastos;
                var periodoDevuelto = await _context.Devoluciones
                    .Where(d => d.PeriodoId == request.PeriodoId.Value)
                    .SumAsync(d => d.Monto, ct);
                var saldoDisponible = periodoDiferencia - periodoDevuelto;
                if (request.Monto > saldoDisponible)
                    throw new InvalidOperationException(
                        $"El monto de la devolución (${request.Monto:N2}) supera el saldo a devolver disponible (${saldoDisponible:N2}).");
            }

            // Validate target rendición is open (when provided)
            if (request.RendicionId.HasValue)
            {
                var rendicion = await _context.Rendiciones
                    .Include(r => r.Transferencias)
                        .ThenInclude(t => t.Compras)
                    .Include(r => r.Gastos)
                    .FirstOrDefaultAsync(r => r.Id == request.RendicionId.Value, ct)
                    ?? throw new KeyNotFoundException($"Rendición {request.RendicionId.Value} no encontrada.");

                if (rendicion.Estado == RendicionEstado.Cerrada)
                    throw new InvalidOperationException(
                        "No se puede registrar una devolución para una rendición cerrada.");

                // Idempotency: only one Devolucion per open rendicion
                var existing = await _context.Devoluciones
                    .AnyAsync(d => d.RendicionId == request.RendicionId.Value, ct);
                if (existing)
                    throw new InvalidOperationException(
                        "Ya existe una devolución registrada para esta rendición. Solo se permite una devolución por rendición en esta versión.");

                // Guard: Monto must not exceed SaldoADevolver for the rendición
                // (saldo calculation mirrors CierreValidator G3 — operativos reales only)
                var rendTotalTransferido = rendicion.Transferencias.Sum(t => t.Monto);
                var rendTotalCompras = rendicion.Transferencias
                    .SelectMany(t => t.Compras)
                    .Sum(c => c.MontoTotal);
                var rendTotalGastos = rendicion.Gastos?.Where(m => CategoriasGasto.EsGastoOperativoReal(m.Categoria)).Sum(g => Math.Abs(g.Monto)) ?? 0m;
                var rendDiferencia = rendTotalTransferido - rendTotalCompras - rendTotalGastos;
                var rendDevuelto = await _context.Devoluciones
                    .Where(d => d.RendicionId == request.RendicionId.Value)
                    .SumAsync(d => d.Monto, ct);
                var rendSaldoDisponible = rendDiferencia - rendDevuelto;
                if (request.Monto > rendSaldoDisponible)
                    throw new InvalidOperationException(
                        $"El monto de la devolución (${request.Monto:N2}) supera el saldo a devolver disponible (${rendSaldoDisponible:N2}).");
            }

            // 1. Create the Devolucion row
            var devolucion = new Devolucion
            {
                Monto = request.Monto,
                Fecha = request.Fecha,
                Trabajador = request.Trabajador,
                PeriodoId = request.PeriodoId,
                RendicionId = request.RendicionId,
                Observaciones = request.Observaciones
            };
            _context.Devoluciones.Add(devolucion);
            await _context.SaveChangesAsync(ct);

            // 2. Create the inverse (positive) MovimientoCaja: money returned to caja
            var movimiento = new MovimientoCaja
            {
                Fecha = request.Fecha,
                Descripcion = $"Devolución rendición/período: saldo devuelto por {request.Trabajador}",
                Monto = Math.Abs(request.Monto), // positive = money back into caja
                Tipo = "APORTE",
                Categoria = "DEVOLUCION_RENDICION",
                Trabajador = request.Trabajador,
                RendicionId = request.RendicionId
            };
            _context.MovimientosCaja.Add(movimiento);
            await _context.SaveChangesAsync(ct);

            // 3. Link Devolucion → MovimientoCaja
            devolucion.MovimientoCajaId = movimiento.Id;
            _context.Devoluciones.Update(devolucion);
            await _context.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);

            return new DevolucionDto
            {
                Id = devolucion.Id,
                Monto = devolucion.Monto,
                Fecha = devolucion.Fecha,
                Trabajador = devolucion.Trabajador,
                PeriodoId = devolucion.PeriodoId,
                RendicionId = devolucion.RendicionId,
                Observaciones = devolucion.Observaciones
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // ========== GetConciliacionGlobalAsync ==========

    public async Task<ConciliacionGlobalDto> GetConciliacionGlobalAsync(
        string trabajador, CancellationToken ct = default)
    {
        // Load all periods for the worker with their transferencias, compras, and gastos
        var periods = await _context.AccountingPeriods
            .Where(p => p.Trabajador == trabajador)
            .Include(p => p.Transferencias)
                .ThenInclude(t => t.Compras)
            .Include(p => p.Transferencias)
                .ThenInclude(t => t.Rendicion)
                    .ThenInclude(r => r!.Gastos)
            .OrderBy(p => p.FechaInicio)
            .ToListAsync(ct);

        if (periods.Count == 0)
        {
            return new ConciliacionGlobalDto();
        }

        // Build semana columns
        var semanas = periods.Select((p, i) => new SemanaColumnaDto
        {
            Id = p.Id,
            Numero = i + 1,
            FechaInicio = p.FechaInicio,
            FechaFin = p.FechaFin,
            EstaCerrada = p.Estado == AccountingPeriodEstado.Cerrado,
            TotalTransferido = p.Transferencias
                .Where(t => t.PeriodoId != null)
                .Sum(t => t.Monto),
            TotalCompras = p.Transferencias
                .Where(t => t.PeriodoId != null)
                .SelectMany(t => t.Compras)
                .Sum(c => c.MontoTotal),
            TotalGastos = p.Transferencias
                .Where(t => t.PeriodoId != null && t.Rendicion != null)
                .SelectMany(t => t.Rendicion!.Gastos)
                .Where(m => CategoriasGasto.EsGastoOperativoReal(m.Categoria))
                .Sum(g => Math.Abs(g.Monto))
        }).ToList();

        // Collect all compras with their period context for provider grouping
        var comprasConPeriodo = periods
            .SelectMany(p => p.Transferencias
                .Where(t => t.PeriodoId != null)
                .SelectMany(t => t.Compras.Select(c => new
                {
                    Compra = c,
                    PeriodoId = p.Id,
                    PeriodoName = p.Name
                })))
            .ToList();

        // Group compras by normalized provider slug
        var proveedorGroups = comprasConPeriodo
            .GroupBy(x => NormalizeProveedorSlug(x.Compra.Proveedor))
            .ToList();

        var proveedores = proveedorGroups.Select(g =>
        {
            var firstCompra = g.First().Compra;
            var celdas = semanas.Select(s =>
        {
            var comprasEnCelda = g.Where(x => x.PeriodoId == s.Id).ToList();
            return new CeldaSemanaDto
            {
                SemanaId = s.Id,
                Monto = comprasEnCelda.Sum(x => x.Compra.MontoTotal),
                Estado = ResolveCeldaEstado(comprasEnCelda.Select(x => x.Compra).ToList()),
                Comprobantes = comprasEnCelda
                    .Select(x => new ComprobanteItemDto
                    {
                        Id = x.Compra.Id,
                        Tipo = "Compra",
                        NumeroDocumento = x.Compra.NumeroDocumento ?? string.Empty,
                        Fecha = x.Compra.FechaCompra,
                        Monto = x.Compra.MontoTotal,
                        Verificada = x.Compra.Verificada,
                        Proveedor = x.Compra.Proveedor
                    })
                    .ToList()
            };
        }).ToList();

            return new FilaProveedorDto
            {
                ProveedorSlug = g.Key,
                ProveedorNombre = firstCompra.Proveedor,
                Celdas = celdas,
                TotalProveedor = g.Sum(x => x.Compra.MontoTotal)
            };
        }).ToList();

        // Compute totals for resumen
        var totalTransferencias = periods
            .SelectMany(p => p.Transferencias.Where(t => t.PeriodoId != null))
            .Sum(t => t.Monto);
        var totalCompras = comprasConPeriodo.Sum(x => x.Compra.MontoTotal);
        var totalGastos = periods
            .SelectMany(p => p.Transferencias
                .Where(t => t.PeriodoId != null && t.Rendicion != null))
            .SelectMany(t => t.Rendicion!.Gastos)
            .Where(m => CategoriasGasto.EsGastoOperativoReal(m.Categoria))
            .Sum(g => Math.Abs(g.Monto));

        // Saldo inicial: balance from periods before the first loaded period.
        // Since we load ALL periods for the worker, this is 0 by default.
        var saldoInicial = 0m;

        // Semanas verificadas: count periods where all transfers + compras are verified
        var semanasVerificadas = periods.Count(p =>
            p.Transferencias.Where(t => t.PeriodoId != null).All(t => t.Verificada) &&
            p.Transferencias.Where(t => t.PeriodoId != null)
                .SelectMany(t => t.Compras)
                .All(c => c.Verificada));

        return new ConciliacionGlobalDto
        {
            Semanas = semanas,
            Proveedores = proveedores,
            Resumen = new ResumenConciliacionDto
            {
                TotalTransferencias = totalTransferencias,
                TotalCompras = totalCompras,
                TotalGastos = totalGastos,
                SaldoTotal = totalTransferencias - totalCompras - totalGastos,
                SemanasTotales = periods.Count,
                SemanasVerificadas = semanasVerificadas
            },
            SaldoInicial = saldoInicial
        };
    }

    /// <summary>
    /// Normalizes a provider name to a slug for grouping.
    /// Lowercase, removes diacritics/accents, non-alphanumeric chars removed.
    /// </summary>
    private static string NormalizeProveedorSlug(string proveedor)
    {
        if (string.IsNullOrWhiteSpace(proveedor))
            return string.Empty;

        // Remove diacritics (accents)
        var formD = proveedor.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(proveedor.Length);
        foreach (var c in formD)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        var withoutDiacritics = sb.ToString().Normalize(NormalizationForm.FormC);

        // Lowercase, keep only alphanumeric
        return string.Concat(withoutDiacritics.ToLowerInvariant()
            .Where(char.IsLetterOrDigit));
    }

    /// <summary>
    /// Resolves the cell estado based on comprobantes in the cell.
    /// </summary>
    private static string ResolveCeldaEstado(List<Compra> comprasEnCelda)
    {
        if (comprasEnCelda.Count == 0)
            return "Vacio";

        var allVerified = comprasEnCelda.All(c => c.Verificada);
        if (allVerified)
            return "Justificado";

        var anyVerified = comprasEnCelda.Any(c => c.Verificada);
        if (anyVerified)
            return "Observado";

        return "Pendiente";
    }

    // ========== Mapping Helpers ==========

    private static CompraDto MapToCompraDto(Compra c)
    {
        return new CompraDto
        {
            Id = c.Id,
            FechaCompra = c.FechaCompra,
            Proveedor = c.Proveedor,
            NumeroDocumento = c.NumeroDocumento,
            MontoTotal = c.MontoTotal,
            Estado = c.Estado,
            TipoFactura = c.TipoFactura,
            PagadaCaja = c.PagadaCaja,
            FacturaImagenPath = c.FacturaImagenPath,
            TransferenciaId = c.TransferenciaId,
            Detalles = c.Detalles?.Select(d => new DetalleCompraDto
            {
                Id = d.Id,
                CompraId = d.CompraId,
                ProductoId = d.ProductoId,
                DescripcionItem = d.DescripcionItem,
                Cantidad = d.Cantidad,
                CostoUnitario = d.CostoUnitario,
                Subtotal = d.Subtotal,
                EsPendiente = d.EsPendiente
            }).ToList() ?? new()
        };
    }

    private static MovimientoCajaDto MapToMovimientoDto(MovimientoCaja m)
    {
        return new MovimientoCajaDto
        {
            Id = m.Id,
            Fecha = m.Fecha,
            Descripcion = m.Descripcion,
            Monto = m.Monto,
            Tipo = m.Tipo,
            Categoria = m.Categoria,
            ImagenPath = m.ImagenPath,
            ProductoId = m.ProductoId,
            Cantidad = m.Cantidad,
            OrdenCargaId = m.OrdenCargaId,
            CompraId = m.CompraId,
            GastoRecurrenteId = m.GastoRecurrenteId
        };
    }

    private static AccountingPeriodDto MapToDto(AccountingPeriod p)
    {
        var totalTransfers = p.Transferencias?.Sum(t => t.Monto) ?? 0;
        var totalCompras = p.Transferencias?
            .SelectMany(t => t.Compras)
            .Sum(c => c.MontoTotal) ?? 0;
        var totalGastos = p.Transferencias?
            .Where(t => t.Rendicion != null)
            .SelectMany(t => t.Rendicion!.Gastos)
            .Where(m => CategoriasGasto.EsGastoOperativoReal(m.Categoria))
            .Sum(g => Math.Abs(g.Monto)) ?? 0;

        return new AccountingPeriodDto
        {
            Id = p.Id,
            Name = p.Name,
            FechaInicio = p.FechaInicio,
            FechaFin = p.FechaFin,
            Estado = p.Estado,
            Trabajador = p.Trabajador,
            TotalTransferido = totalTransfers,
            TotalCompras = totalCompras,
            TotalGastos = totalGastos,
            // Devuelto defaults to nav-prop sum if loaded; GetPeriodosAsync overrides
            // via a single aggregate query (WARNING-1 fix). GetPeriodoFullAsync sets it directly.
            Devuelto = p.Devoluciones?.Sum(d => d.Monto) ?? 0
        };
    }

    private AccountingPeriodFullDto MapToFullDto(AccountingPeriod p)
    {
        var baseDto = MapToDto(p);

        var transferencias = p.Transferencias?.Select(t => new TransferenciaDto
        {
            Id = t.Id,
            Fecha = t.Fecha,
            Monto = t.Monto,
            Descripcion = t.Descripcion,
            Trabajador = t.Trabajador ?? string.Empty,
            Estado = t.Estado,
            RendicionId = t.RendicionId,
            PeriodoId = t.PeriodoId,
            MovimientoCajaId = t.MovimientoCajaId,
            // TASK-10: wire Verificada + HasComprobante
            Verificada = t.Verificada,
            HasComprobante = t.ComprobanteImagenFileName != null,
            ComprobanteImagenFileName = t.ComprobanteImagenFileName,
            Compras = t.Compras?.Select(c => new CompraDto
            {
                Id = c.Id,
                FechaCompra = c.FechaCompra,
                Proveedor = c.Proveedor,
                NumeroDocumento = c.NumeroDocumento,
                MontoTotal = c.MontoTotal,
                Estado = c.Estado,
                TipoFactura = c.TipoFactura,
                PagadaCaja = c.PagadaCaja,
                FacturaImagenPath = c.FacturaImagenPath,
                TransferenciaId = c.TransferenciaId,
                // TASK-10: wire Verificada on Compra
                Verificada = c.Verificada,
                Detalles = c.Detalles?.Select(d => new DetalleCompraDto
                {
                    Id = d.Id,
                    CompraId = d.CompraId,
                    ProductoId = d.ProductoId,
                    DescripcionItem = d.DescripcionItem,
                    Cantidad = d.Cantidad,
                    CostoUnitario = d.CostoUnitario,
                    Subtotal = d.Subtotal,
                    EsPendiente = d.EsPendiente
                }).ToList() ?? new()
            }).ToList() ?? new()
        }).ToList() ?? new();

        var gastos = p.Transferencias?
            .Where(t => t.Rendicion != null)
            .SelectMany(t => t.Rendicion!.Gastos)
            .Where(m => CategoriasGasto.EsGastoOperativoReal(m.Categoria))
            .Select(g => new MovimientoCajaDto
            {
                Id = g.Id,
                Fecha = g.Fecha,
                Descripcion = g.Descripcion,
                Monto = g.Monto,
                Tipo = g.Tipo,
                Categoria = g.Categoria,
                ImagenPath = g.ImagenPath,
                ProductoId = g.ProductoId,
                Cantidad = g.Cantidad,
                OrdenCargaId = g.OrdenCargaId,
                CompraId = g.CompraId,
                GastoRecurrenteId = g.GastoRecurrenteId
            })
            .DistinctBy(g => g.Id)
            .ToList() ?? new();

        return new AccountingPeriodFullDto
        {
            Id = baseDto.Id,
            Name = baseDto.Name,
            FechaInicio = baseDto.FechaInicio,
            FechaFin = baseDto.FechaFin,
            Estado = baseDto.Estado,
            Trabajador = baseDto.Trabajador,
            TotalTransferido = baseDto.TotalTransferido,
            TotalCompras = baseDto.TotalCompras,
            TotalGastos = baseDto.TotalGastos,
            Transferencias = transferencias,
            Gastos = gastos
        };
    }
}
