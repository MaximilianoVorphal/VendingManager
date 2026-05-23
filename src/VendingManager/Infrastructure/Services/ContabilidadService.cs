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

    public ContabilidadService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Transferencia> CrearTransferenciaConMovimientoAsync(
        TransferenciaConMovimientoRequest request,
        CancellationToken ct = default)
    {
        await using var transaction = await _context.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        try
        {
            // 1. Verify Rendicion exists
            var rendicion = await _context.Rendiciones
                .FirstOrDefaultAsync(r => r.Id == request.RendicionId, ct)
                ?? throw new KeyNotFoundException($"Rendicion {request.RendicionId} no encontrada.");

            // 2. Create Transferencia
            var transferencia = new Transferencia
            {
                Fecha = request.Fecha,
                Monto = request.Monto,
                Descripcion = request.Descripcion,
                Trabajador = request.Trabajador,
                Estado = TransferenciaEstado.Pendiente,
                RendicionId = request.RendicionId
            };
            _context.Transferencias.Add(transferencia);
            await _context.SaveChangesAsync(ct);

            // 3. Create linked MovimientoCaja (RETIRO = money leaving caja)
            var movimiento = new MovimientoCaja
            {
                Fecha = request.Fecha,
                Descripcion = $"Retiro para rendición #{request.RendicionId}: {request.Descripcion}",
                Monto = -request.Monto,
                Tipo = "RETIRO",
                Categoria = "OTROS",
                Trabajador = request.Trabajador,
                RendicionId = request.RendicionId
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
}