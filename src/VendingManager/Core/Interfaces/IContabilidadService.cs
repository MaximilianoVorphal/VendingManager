using VendingManager.Core.Entities;
using VendingManager.Shared.DTOs;

namespace VendingManager.Core.Interfaces;

public interface IContabilidadService
{
    Task<Transferencia> CrearTransferenciaConMovimientoAsync(TransferenciaConMovimientoRequest request, CancellationToken ct = default);
    Task<Compra> CrearCompraVinculadaAsync(CompraVinculadaRequest request, CancellationToken ct = default);
    Task<MovimientoCaja> CrearGastoVinculadoAsync(GastoVinculadoRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<TrabajadorActivoDto>> GetTrabajadoresActivosAsync(CancellationToken ct = default);
    Task ActualizarMontoTransferenciaAsync(int transferenciaId, decimal nuevoMonto, CancellationToken ct = default);
    Task DesvincularTransferenciaAsync(int transferenciaId, CancellationToken ct = default);

    // AccountingPeriod methods
    Task<List<AccountingPeriodDto>> GetPeriodosAsync(DateTime? desde, DateTime? hasta, CancellationToken ct = default);
    Task<AccountingPeriodFullDto?> GetPeriodoFullAsync(int id, CancellationToken ct = default);
    Task<AccountingPeriodDto> CreatePeriodoAsync(CreatePeriodoRequest req, CancellationToken ct = default);
    Task<AccountingPeriodDto> UpdatePeriodoAsync(int id, UpdatePeriodoRequest req, CancellationToken ct = default);
    Task ClosePeriodoAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Deletes an AccountingPeriod by unlinking its Transferencias and Devoluciones
    /// (setting PeriodoId = null) without cascading deletes. Does NOT touch MovimientoCaja.
    /// Throws KeyNotFoundException if the period does not exist.
    /// </summary>
    Task DeletePeriodoAsync(int id, CancellationToken ct = default);

    // Edit methods
    Task<CompraDto> UpdateCompraAsync(int compraId, UpdateCompraRequest req, CancellationToken ct = default);
    Task<MovimientoCajaDto> UpdateGastoAsync(int gastoId, UpdateGastoRequest req, CancellationToken ct = default);

    // Slice 2: Verification methods (TASK-08)
    /// <summary>Sets Transferencia.Verificada to the given value. Respects RowVersion concurrency.</summary>
    Task MarcarTransferenciaVerificadaAsync(int transferenciaId, bool verificada, CancellationToken ct = default);

    /// <summary>Sets Compra.Verificada to the given value.</summary>
    Task MarcarCompraVerificadaAsync(int compraId, bool verificada, CancellationToken ct = default);

    // Slice 2: Devolución registration (TASK-09)
    /// <summary>
    /// Registers a Devolucion and posts one positive (cash-in) MovimientoCaja atomically.
    /// Validates: Monto > 0, period/rendición open, no prior Devolucion for same period/rendicion.
    /// Requires at least one of PeriodoId / RendicionId.
    /// </summary>
    Task<DevolucionDto> RegistrarDevolucionAsync(RegistrarDevolucionRequest request, CancellationToken ct = default);
}
