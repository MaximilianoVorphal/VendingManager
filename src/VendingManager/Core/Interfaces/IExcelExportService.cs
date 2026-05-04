using VendingManager.Core.Entities;
using VendingManager.Shared.DTOs;

namespace VendingManager.Core.Interfaces;

public interface IExcelExportService
{
    Task<byte[]> ExportCajaReportAsync(
        CajaResumenDto resumen,
        List<MovimientoCaja> movimientos,
        IReadOnlyList<Venta> ventas,
        int month,
        int year,
        CancellationToken ct = default);

    Task<byte[]> ExportMovimientosAsync(
        List<MovimientoCaja> movimientos,
        int month,
        int year,
        CancellationToken ct = default);

    Task<byte[]> ExportSalesReportAsync(
        List<DetalleVentaDto> detalle,
        DateTime inicio,
        DateTime fin,
        CancellationToken ct = default);

    Task<byte[]> ExportPurchasingReportAsync(
        List<PurchaseSuggestionDto> items,
        int dias,
        int? maquinaId,
        CancellationToken ct = default);
}