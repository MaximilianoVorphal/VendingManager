using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;

namespace VendingManager.Infrastructure.Services;

public class RendicionService : IRendicionService
{
    private readonly ApplicationDbContext _context;

    public RendicionService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Rendicion>> GetAllAsync()
    {
        return await _context.Rendiciones
            .Include(r => r.Transferencias)
            .Include(r => r.Gastos)
            .OrderByDescending(r => r.FechaInicio)
            .ThenByDescending(r => r.Id)
            .ToListAsync();
    }

    public async Task<Rendicion?> GetByIdAsync(int id)
    {
        return await _context.Rendiciones
            .Include(r => r.Transferencias)
            .ThenInclude(t => t.Compras)
            .Include(r => r.Gastos)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<Rendicion> CreateAsync(Rendicion rendicion)
    {
        rendicion.Estado = RendicionEstado.Abierta;
        rendicion.FechaInicio = rendicion.FechaInicio == DateTime.MinValue ? DateTime.Now : rendicion.FechaInicio;
        _context.Rendiciones.Add(rendicion);
        await _context.SaveChangesAsync();
        return rendicion;
    }

    public async Task<Rendicion> UpdateAsync(int id, Rendicion rendicion)
    {
        var existing = await _context.Rendiciones.FindAsync(id);
        if (existing == null)
            throw new KeyNotFoundException($"Rendicion {id} no encontrada.");

        if (existing.Estado == RendicionEstado.Cerrada)
            throw new InvalidOperationException("No se puede modificar una rendición cerrada.");

        existing.Trabajador = rendicion.Trabajador;
        existing.FechaFin = rendicion.FechaFin;
        existing.Observaciones = rendicion.Observaciones;

        _context.Rendiciones.Update(existing);
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<Rendicion> CerrarAsync(int id)
    {
        var rendicion = await _context.Rendiciones
            .Include(r => r.Transferencias)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (rendicion == null)
            throw new KeyNotFoundException($"Rendicion {id} no encontrada.");

        if (rendicion.Estado == RendicionEstado.Cerrada)
            throw new InvalidOperationException("La rendición ya está cerrada.");

        // Validar que todas las transferencias estén Conciliado antes de cerrar
        var nonConciliada = rendicion.Transferencias
            .FirstOrDefault(t => t.Estado != TransferenciaEstado.Conciliado);
        if (nonConciliada != null)
            throw new InvalidOperationException(
                $"No se puede cerrar la rendición. La transferencia #{nonConciliada.Id} " +
                $"tiene estado {nonConciliada.Estado} y debe estar Conciliado.");

        rendicion.Estado = RendicionEstado.Cerrada;
        rendicion.FechaFin = DateTime.Now;

        _context.Rendiciones.Update(rendicion);
        await _context.SaveChangesAsync();
        return rendicion;
    }

    public async Task<Compra> LinkCompraAsync(int compraId, int transferenciaId)
    {
        var compra = await _context.Compras.FindAsync(compraId);
        if (compra == null)
            throw new KeyNotFoundException($"Compra {compraId} no encontrada.");

        var transferencia = await _context.Transferencias.FindAsync(transferenciaId);
        if (transferencia == null)
            throw new KeyNotFoundException($"Transferencia {transferenciaId} no encontrada.");

        if (transferencia.Estado == TransferenciaEstado.Conciliado)
            throw new InvalidOperationException("No se puede vincular una compra a una transferencia ya conciliada.");

        // Asignar la vinculación
        compra.TransferenciaId = transferenciaId;
        _context.Compras.Update(compra);

        // Auto-transicionar Transferencia a EnUso si está Pendiente
        if (transferencia.Estado == TransferenciaEstado.Pendiente)
        {
            transferencia.Estado = TransferenciaEstado.EnUso;
            _context.Transferencias.Update(transferencia);
        }

        await _context.SaveChangesAsync();
        return compra;
    }

    public async Task<Compra> UnlinkCompraAsync(int compraId)
    {
        var compra = await _context.Compras
            .Include(c => c.Transferencia)
            .FirstOrDefaultAsync(c => c.Id == compraId);

        if (compra == null)
            throw new KeyNotFoundException($"Compra {compraId} no encontrada.");

        if (!compra.TransferenciaId.HasValue)
            return compra; // Ya desvinculada

        var transferencia = compra.Transferencia;
        compra.TransferenciaId = null;
        compra.Transferencia = null;
        _context.Compras.Update(compra);

        // Si la transferencia no tiene más compras linking, volver a Pendiente
        if (transferencia != null)
        {
            var stillLinked = await _context.Compras
                .AnyAsync(c => c.TransferenciaId == transferencia.Id && c.Id != compraId);
            if (!stillLinked && transferencia.Estado == TransferenciaEstado.EnUso)
            {
                transferencia.Estado = TransferenciaEstado.Pendiente;
                _context.Transferencias.Update(transferencia);
            }
        }

        await _context.SaveChangesAsync();
        return compra;
    }

    public async Task<MovimientoCaja> LinkGastoAsync(int gastoId, int rendicionId)
    {
        var gasto = await _context.MovimientosCaja.FindAsync(gastoId);
        if (gasto == null)
            throw new KeyNotFoundException($"Gasto {gastoId} no encontrado.");

        var rendicion = await _context.Rendiciones.FindAsync(rendicionId);
        if (rendicion == null)
            throw new KeyNotFoundException($"Rendicion {rendicionId} no encontrada.");

        if (rendicion.Estado == RendicionEstado.Cerrada)
            throw new InvalidOperationException("No se puede vincular un gasto a una rendición cerrada.");

        gasto.RendicionId = rendicionId;
        _context.MovimientosCaja.Update(gasto);
        await _context.SaveChangesAsync();
        return gasto;
    }

    public async Task<MovimientoCaja> UnlinkGastoAsync(int gastoId)
    {
        var gasto = await _context.MovimientosCaja.FindAsync(gastoId);
        if (gasto == null)
            throw new KeyNotFoundException($"Gasto {gastoId} no encontrado.");

        gasto.RendicionId = null;
        _context.MovimientosCaja.Update(gasto);
        await _context.SaveChangesAsync();
        return gasto;
    }

    public async Task<RendicionResumenDto> GetResumenAsync(int rendicionId)
    {
        var rendicion = await _context.Rendiciones
            .Include(r => r.Transferencias)
            .Include(r => r.Gastos)
            .FirstOrDefaultAsync(r => r.Id == rendicionId);

        if (rendicion == null)
            throw new KeyNotFoundException($"Rendicion {rendicionId} no encontrada.");

        var transferido = rendicion.Transferencias.Sum(t => t.Monto);
        var totalCompras = rendicion.Transferencias
            .SelectMany(t => t.Compras)
            .Sum(c => c.MontoTotal);
        var totalGastos = rendicion.Gastos
            .Where(g => g.Tipo == "GASTO")
            .Sum(g => Math.Abs(g.Monto));
        var diferencia = transferido - totalCompras - totalGastos;

        return new RendicionResumenDto
        {
            RendicionId = rendicionId,
            Transferido = transferido,
            TotalCompras = totalCompras,
            TotalGastos = totalGastos,
            Diferencia = diferencia
        };
    }
}