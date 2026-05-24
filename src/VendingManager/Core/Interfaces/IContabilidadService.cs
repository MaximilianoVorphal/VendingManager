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

    // Edit methods
    Task<CompraDto> UpdateCompraAsync(int compraId, UpdateCompraRequest req, CancellationToken ct = default);
    Task<MovimientoCajaDto> UpdateGastoAsync(int gastoId, UpdateGastoRequest req, CancellationToken ct = default);
}
