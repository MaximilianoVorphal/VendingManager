using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;

namespace VendingManager.Infrastructure.Services;

public class GastoRecurrenteService : IGastoRecurrenteService
{
    private readonly ApplicationDbContext _context;

    public GastoRecurrenteService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<GastoRecurrente>> GetActivosAsync()
    {
        return await _context.GastosRecurrentes
            .Where(g => g.Activo)
            .OrderBy(g => g.Categoria)
            .ThenBy(g => g.Descripcion)
            .ToListAsync();
    }

    public async Task<List<GastoRecurrente>> GetTodosAsync()
    {
        return await _context.GastosRecurrentes
            .OrderBy(g => g.Activo ? 0 : 1) // Activos primero
            .ThenBy(g => g.Categoria)
            .ThenBy(g => g.Descripcion)
            .ToListAsync();
    }

    public async Task<GastoRecurrente> CrearAsync(GastoRecurrente gasto)
    {
        gasto.FechaCreacion = DateTime.Now;
        _context.GastosRecurrentes.Add(gasto);
        await _context.SaveChangesAsync();
        return gasto;
    }

    public async Task ActualizarAsync(int id, GastoRecurrente gasto)
    {
        var existente = await _context.GastosRecurrentes.FindAsync(id);
        if (existente == null) throw new Exception("Gasto recurrente no encontrado.");

        existente.Descripcion = gasto.Descripcion;
        existente.MontoEstimado = gasto.MontoEstimado;
        existente.Categoria = gasto.Categoria;
        existente.MaquinaId = gasto.MaquinaId;
        existente.Activo = gasto.Activo;

        await _context.SaveChangesAsync();
    }

    public async Task DesactivarAsync(int id)
    {
        var gasto = await _context.GastosRecurrentes.FindAsync(id);
        if (gasto == null) throw new Exception("Gasto recurrente no encontrado.");

        gasto.Activo = false;
        await _context.SaveChangesAsync();
    }

    public async Task<List<GastoPendienteDto>> GetPendientesDelMesAsync(int month, int year)
    {
        // 1. Obtener todos los gastos recurrentes activos
        var gastosActivos = await _context.GastosRecurrentes
            .Where(g => g.Activo)
            .ToListAsync();

        if (!gastosActivos.Any()) return new List<GastoPendienteDto>();

        // 2. Obtener los MovimientosCaja del mes que tienen GastoRecurrenteId
        var gastosYaRegistrados = await _context.MovimientosCaja
            .Where(m => m.Fecha.Month == month && m.Fecha.Year == year && m.GastoRecurrenteId != null)
            .Select(m => m.GastoRecurrenteId!.Value)
            .ToListAsync();

        // 3. Filtrar los que aún no se registraron
        var pendientes = gastosActivos
            .Where(g => !gastosYaRegistrados.Contains(g.Id))
            .ToList();

        // 4. Obtener nombres de máquinas para los que tienen MaquinaId
        var maquinaIds = pendientes
            .Where(g => g.MaquinaId.HasValue)
            .Select(g => g.MaquinaId!.Value)
            .Distinct()
            .ToList();

        var maquinasDict = maquinaIds.Any()
            ? await _context.Maquinas
                .Where(m => maquinaIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Nombre)
            : new Dictionary<int, string>();

        return pendientes.Select(g => new GastoPendienteDto
        {
            GastoRecurrenteId = g.Id,
            Descripcion = g.Descripcion,
            MontoEstimado = g.MontoEstimado,
            Categoria = g.Categoria,
            MaquinaId = g.MaquinaId,
            MaquinaNombre = g.MaquinaId.HasValue && maquinasDict.ContainsKey(g.MaquinaId.Value)
                ? maquinasDict[g.MaquinaId.Value]
                : null
        }).ToList();
    }

    public async Task AplicarGastoAsync(int gastoRecurrenteId, int month, int year, decimal? montoReal = null)
    {
        var gasto = await _context.GastosRecurrentes.FindAsync(gastoRecurrenteId);
        if (gasto == null) throw new Exception("Gasto recurrente no encontrado.");

        // Verificar que no se haya registrado ya este mes
        var yaRegistrado = await _context.MovimientosCaja
            .AnyAsync(m => m.GastoRecurrenteId == gastoRecurrenteId
                        && m.Fecha.Month == month
                        && m.Fecha.Year == year);

        if (yaRegistrado)
            throw new InvalidOperationException($"El gasto '{gasto.Descripcion}' ya fue registrado en {month}/{year}.");

        // Crear el MovimientoCaja
        decimal montoFinal = montoReal ?? gasto.MontoEstimado;

        var movimiento = new MovimientoCaja
        {
            Fecha = new DateTime(year, month, DateTime.Now.Day > DateTime.DaysInMonth(year, month)
                ? DateTime.DaysInMonth(year, month)
                : DateTime.Now.Day),
            Descripcion = gasto.Descripcion,
            Monto = -Math.Abs(montoFinal), // Siempre negativo (es gasto)
            Tipo = "GASTO",
            Categoria = gasto.Categoria,
            GastoRecurrenteId = gastoRecurrenteId
        };

        _context.MovimientosCaja.Add(movimiento);
        await _context.SaveChangesAsync();
    }
}
