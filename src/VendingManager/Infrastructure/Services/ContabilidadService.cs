using System.Data;
using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;

namespace VendingManager.Infrastructure.Services;

public class ContabilidadService : IContabilidadService
{
    private readonly ApplicationDbContext _context;
    private readonly IAccountingPeriodRepository _periodRepository;

    public ContabilidadService(ApplicationDbContext context, IAccountingPeriodRepository periodRepository)
    {
        _context = context;
        _periodRepository = periodRepository;
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
                RendicionId = rendicionId
            };
            _context.Transferencias.Add(transferencia);
            await _context.SaveChangesAsync(ct);

            // 3. Create linked MovimientoCaja (RETIRO = money leaving caja)
            var movimiento = new MovimientoCaja
            {
                Fecha = request.Fecha,
                Descripcion = $"Retiro para rendición #{rendicionId}: {request.Descripcion}",
                Monto = -request.Monto,
                Tipo = "RETIRO",
                Categoria = "OTROS",
                Trabajador = request.Trabajador,
                RendicionId = rendicionId
            };
            _context.MovimientosCaja.Add(movimiento);
            await _context.SaveChangesAsync(ct);

            // 4. Wire Transferencia → MovimientoCaja
            transferencia.MovimientoCajaId = movimiento.Id;
            _context.Transferencias.Update(transferencia);
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
                    Subtotal = d.Cantidad * d.CostoUnitario
                }).ToList()
            };
            compra.MontoTotal = compra.Detalles.Sum(d => d.Subtotal);

            _context.Compras.Add(compra);
            await _context.SaveChangesAsync(ct);

            // 3. Update Transferencia estado: Pendiente → EnUso
            if (transferencia.Estado == TransferenciaEstado.Pendiente)
            {
                transferencia.Estado = TransferenciaEstado.EnUso;
                _context.Transferencias.Update(transferencia);
            }

            // 4. Update Producto stock + CostoPromedio (CPP formula from CompraService)
            foreach (var detalle in compra.Detalles.Where(d => d.ProductoId.HasValue))
            {
                var producto = await _context.Productos.FindAsync(new object[] { detalle.ProductoId!.Value }, ct);
                if (producto != null)
                {
                    decimal valorInventarioActual = producto.StockBodega * producto.CostoPromedio;
                    decimal valorNuevaTransaccion = detalle.Cantidad * detalle.CostoUnitario;
                    int nuevoStockTotal = producto.StockBodega + detalle.Cantidad;

                    if (nuevoStockTotal > 0)
                        producto.CostoPromedio = (valorInventarioActual + valorNuevaTransaccion) / nuevoStockTotal;

                    producto.StockBodega = nuevoStockTotal;
                    _context.Productos.Update(producto);
                }
            }

            // 5. Insert ProductoCosto rows (close open rows, create new)
            foreach (var item in compra.Detalles.Where(d => d.ProductoId.HasValue).GroupBy(d => d.ProductoId))
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

            // Update linked MovimientoCaja.Monto if exists
            if (transferencia.MovimientoCajaId.HasValue)
            {
                var movimiento = await _context.MovimientosCaja
                    .FirstOrDefaultAsync(m => m.Id == transferencia.MovimientoCajaId.Value, ct);
                if (movimiento != null)
                {
                    // Keep the sign convention: MovimientoCaja.Monto is negative (money out)
                    // so we mirror the absolute value with negative sign
                    movimiento.Monto = -Math.Abs(nuevoMonto);
                    _context.MovimientosCaja.Update(movimiento);
                }
            }

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
                    Subtotal = d.Cantidad * d.CostoUnitario
                }).ToList();
            }

            // Recalculate total
            compra.MontoTotal = compra.Detalles.Sum(d => d.Subtotal);

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
        var periods = await _periodRepository.GetByDateRangeAsync(desde, hasta, ct);
        return periods.Select(MapToDto).ToList();
    }

    public async Task<AccountingPeriodFullDto?> GetPeriodoFullAsync(
        int id, CancellationToken ct = default)
    {
        var period = await _periodRepository.GetFullByIdAsync(id, ct);
        if (period == null) return null;

        var dto = MapToFullDto(period);
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

        // Validate: all transfers must be Conciliado
        var nonConciliated = period.Transferencias
            .Where(t => t.Estado != TransferenciaEstado.Conciliado)
            .ToList();

        if (nonConciliated.Count != 0)
            throw new InvalidOperationException(
                $"No se puede cerrar el período. Hay {nonConciliated.Count} transferencia(s) no conciliada(s).");

        period.Estado = AccountingPeriodEstado.Cerrado;
        await _periodRepository.UpdateAsync(period, ct);
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
                Subtotal = d.Subtotal
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
            TotalGastos = totalGastos
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
                Detalles = c.Detalles?.Select(d => new DetalleCompraDto
                {
                    Id = d.Id,
                    CompraId = d.CompraId,
                    ProductoId = d.ProductoId,
                    DescripcionItem = d.DescripcionItem,
                    Cantidad = d.Cantidad,
                    CostoUnitario = d.CostoUnitario,
                    Subtotal = d.Subtotal
                }).ToList() ?? new()
            }).ToList() ?? new()
        }).ToList() ?? new();

        var gastos = p.Transferencias?
            .Where(t => t.Rendicion != null)
            .SelectMany(t => t.Rendicion!.Gastos)
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
