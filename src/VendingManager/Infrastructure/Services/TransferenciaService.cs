using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;

namespace VendingManager.Infrastructure.Services;

public class TransferenciaService : ITransferenciaService
{
    private readonly ApplicationDbContext _context;

    public TransferenciaService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Transferencia>> GetAllAsync()
    {
        return await _context.Transferencias
            .OrderByDescending(t => t.Fecha)
            .ThenByDescending(t => t.Id)
            .ToListAsync();
    }

    public async Task<Transferencia?> GetByIdAsync(int id)
    {
        return await _context.Transferencias
            .Include(t => t.Compras)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<Transferencia> CreateAsync(Transferencia transferencia)
    {
        transferencia.Estado = TransferenciaEstado.Pendiente;
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();
        return transferencia;
    }

    public async Task<Transferencia> UpdateAsync(int id, Transferencia transferencia)
    {
        var existing = await _context.Transferencias.FindAsync(id);
        if (existing == null)
            throw new KeyNotFoundException($"Transferencia {id} no encontrada.");

        if (existing.Estado == TransferenciaEstado.Conciliado)
            throw new InvalidOperationException("No se puede modificar una transferencia ya conciliada.");

        existing.Fecha = transferencia.Fecha;
        existing.Monto = transferencia.Monto;
        existing.Descripcion = transferencia.Descripcion;
        existing.Trabajador = transferencia.Trabajador;
        // Estado se maneja vía transiciones automáticas

        _context.Transferencias.Update(existing);
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task DeleteAsync(int id)
    {
        var existing = await _context.Transferencias.FindAsync(id);
        if (existing == null)
            throw new KeyNotFoundException($"Transferencia {id} no encontrada.");

        _context.Transferencias.Remove(existing);
        await _context.SaveChangesAsync();
    }

    public async Task<IEnumerable<Transferencia>> GetTransferenciasByRendicionAsync(int rendicionId)
    {
        return await _context.Transferencias
            .Where(t => t.RendicionId == rendicionId)
            .OrderBy(t => t.Fecha)
            .ToListAsync();
    }

    public async Task<IEnumerable<Transferencia>> GetTransferenciasPendientesAsync()
    {
        return await _context.Transferencias
            .Where(t => t.Estado == TransferenciaEstado.Pendiente || t.Estado == TransferenciaEstado.EnUso)
            .OrderByDescending(t => t.Fecha)
            .ToListAsync();
    }

    public async Task<IEnumerable<Transferencia>> GetTransferenciasNoVinculadasAsync()
    {
        return await _context.Transferencias
            .Where(t => t.RendicionId == null)
            .OrderByDescending(t => t.Fecha)
            .ToListAsync();
    }
}