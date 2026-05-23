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
}