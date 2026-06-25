using VendingManager.Core.Entities;
using VendingManager.Shared.DTOs;

namespace VendingManager.Core.Interfaces;

public interface IGastoRecurrenteService
{
    /// <summary>
    /// Obtiene todos los gastos recurrentes activos.
    /// </summary>
    Task<List<GastoRecurrente>> GetActivosAsync();

    /// <summary>
    /// Obtiene todos los gastos recurrentes (activos e inactivos).
    /// </summary>
    Task<List<GastoRecurrente>> GetTodosAsync();

    /// <summary>
    /// Crea un nuevo gasto recurrente.
    /// </summary>
    Task<GastoRecurrente> CrearAsync(GastoRecurrente gasto);

    /// <summary>
    /// Actualiza un gasto recurrente existente.
    /// </summary>
    Task ActualizarAsync(int id, GastoRecurrente gasto);

    /// <summary>
    /// Desactiva (soft-delete) un gasto recurrente.
    /// </summary>
    Task DesactivarAsync(int id);

    /// <summary>
    /// Devuelve los gastos recurrentes que aún no se han registrado en un mes/año específico.
    /// Cruza GastoRecurrente contra MovimientoCaja para detectar pendientes.
    /// </summary>
    Task<List<GastoPendienteDto>> GetPendientesDelMesAsync(int month, int year);

    /// <summary>
    /// Aplica un gasto recurrente como MovimientoCaja del mes indicado.
    /// Permite ajustar el monto real antes de registrarlo.
    /// </summary>
    Task AplicarGastoAsync(int gastoRecurrenteId, int month, int year, decimal? montoReal = null);
}

