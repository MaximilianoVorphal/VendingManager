using System.IO;
using ClosedXML.Excel;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

namespace VendingManager.Infrastructure.Services;

public class ExcelExportService : IExcelExportService
{
    private static readonly XLColor HeaderBackground = XLColor.LightGray;
    private const string CurrencyFormat = "$ #,##0";
    private const string DateTimeFormat = "dd/MM/yyyy HH:mm";

    public async Task<byte[]> ExportCajaReportAsync(
        CajaResumenDto resumen,
        List<MovimientoCaja> movimientos,
        List<Venta> ventas,
        int month,
        int year,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            using var workbook = new XLWorkbook();

            // Sheet 1: Resumen Financiero
            var s1 = workbook.Worksheets.Add("Resumen Financiero");
            s1.Cell(1, 1).Value = "REPORTE FINANCIERO MENSUAL";
            s1.Range(1, 1, 1, 4).Merge().Style.Font.Bold = true;
            s1.Cell(2, 1).Value = $"PERIODO: {month}/{year}";

            s1.Cell(4, 1).Value = "CONCEPTO";
            s1.Cell(4, 2).Value = "MONTO";
            s1.Row(4).Style.Font.Bold = true;
            s1.Row(4).Style.Fill.BackgroundColor = HeaderBackground;

            s1.Cell(5, 1).Value = "Saldo Anterior";
            s1.Cell(5, 2).Value = resumen.SaldoAnterior;

            s1.Cell(6, 1).Value = "(+) Ingresos por Ventas";
            s1.Cell(6, 2).Value = resumen.IngresosVentas;

            s1.Cell(7, 1).Value = "(+) Aportes de Capital";
            s1.Cell(7, 2).Value = resumen.AportesExtra;

            s1.Cell(8, 1).Value = "(-) Gastos y Retiros";
            s1.Cell(8, 2).Value = resumen.GastosOperativos;

            s1.Cell(9, 1).Value = "SALDO FINAL CAJA";
            s1.Cell(9, 2).Value = resumen.SaldoFinal;
            s1.Row(9).Style.Font.Bold = true;

            s1.Column(2).Style.NumberFormat.Format = CurrencyFormat;
            s1.Columns().AdjustToContents();

            // Sheet 2: Libro Caja
            var s2 = workbook.Worksheets.Add("Libro Caja");
            s2.Cell(1, 1).Value = "Fecha";
            s2.Cell(1, 2).Value = "Tipo";
            s2.Cell(1, 3).Value = "Categoría";
            s2.Cell(1, 4).Value = "Descripción";
            s2.Cell(1, 5).Value = "Monto";
            s2.Row(1).Style.Font.Bold = true;
            s2.Row(1).Style.Fill.BackgroundColor = HeaderBackground;

            int row = 2;
            foreach (var m in movimientos)
            {
                s2.Cell(row, 1).Value = m.Fecha;
                s2.Cell(row, 1).Style.DateFormat.Format = DateTimeFormat;
                s2.Cell(row, 2).Value = m.Tipo;
                s2.Cell(row, 3).Value = m.Categoria;
                s2.Cell(row, 4).Value = m.Descripcion;
                s2.Cell(row, 5).Value = m.Monto;

                if (m.Monto < 0)
                    s2.Cell(row, 5).Style.Font.FontColor = XLColor.Red;
                else
                    s2.Cell(row, 5).Style.Font.FontColor = XLColor.Green;

                row++;
            }
            s2.Column(5).Style.NumberFormat.Format = CurrencyFormat;
            s2.Columns().AdjustToContents();

            // Sheet 3: Detalle Ventas
            var s3 = workbook.Worksheets.Add("Detalle Ventas");
            s3.Cell(1, 1).Value = "Fecha";
            s3.Cell(1, 2).Value = "Máquina";
            s3.Cell(1, 3).Value = "Slot";
            s3.Cell(1, 4).Value = "Producto";
            s3.Cell(1, 5).Value = "P. Venta";
            s3.Cell(1, 6).Value = "P. Costo (Histórico)";
            s3.Cell(1, 7).Value = "Margen $";
            s3.Row(1).Style.Font.Bold = true;
            s3.Row(1).Style.Fill.BackgroundColor = HeaderBackground;

            row = 2;
            foreach (var v in ventas)
            {
                s3.Cell(row, 1).Value = v.FechaHora;
                s3.Cell(row, 1).Style.DateFormat.Format = DateTimeFormat;
                s3.Cell(row, 2).Value = v.Maquina?.Nombre ?? "N/A";
                s3.Cell(row, 3).Value = v.NumeroSlot;
                s3.Cell(row, 4).Value = v.Producto?.Nombre ?? "Indefinido";
                s3.Cell(row, 5).Value = v.PrecioVenta;
                s3.Cell(row, 6).Value = v.CostoVenta;
                s3.Cell(row, 7).FormulaA1 = $"E{row}-F{row}";

                var margen = v.PrecioVenta - v.CostoVenta;
                if (margen < 0)
                    s3.Cell(row, 7).Style.Font.FontColor = XLColor.Red;
                else
                    s3.Cell(row, 7).Style.Font.FontColor = XLColor.Green;

                row++;
            }
            s3.Range(2, 5, row, 7).Style.NumberFormat.Format = CurrencyFormat;
            s3.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }, ct);
    }

    public async Task<byte[]> ExportMovimientosAsync(
        List<MovimientoCaja> movimientos,
        int month,
        int year,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add($"Caja {month}-{year}");

            worksheet.Cell(1, 1).Value = "ID";
            worksheet.Cell(1, 2).Value = "FECHA";
            worksheet.Cell(1, 3).Value = "TIPO";
            worksheet.Cell(1, 4).Value = "CATEGORIA";
            worksheet.Cell(1, 5).Value = "DESCRIPCION";
            worksheet.Cell(1, 6).Value = "MONTO";

            var rangoHeader = worksheet.Range("A1:F1");
            rangoHeader.Style.Font.Bold = true;
            rangoHeader.Style.Fill.BackgroundColor = HeaderBackground;

            int row = 2;
            foreach (var m in movimientos)
            {
                worksheet.Cell(row, 1).Value = m.Id;
                worksheet.Cell(row, 2).Value = m.Fecha;
                worksheet.Cell(row, 2).Style.DateFormat.Format = DateTimeFormat;
                worksheet.Cell(row, 3).Value = m.Tipo;
                worksheet.Cell(row, 4).Value = m.Categoria;
                worksheet.Cell(row, 5).Value = m.Descripcion;
                worksheet.Cell(row, 6).Value = m.Monto;
                worksheet.Cell(row, 6).Style.NumberFormat.Format = CurrencyFormat;

                if (m.Monto < 0)
                    worksheet.Cell(row, 6).Style.Font.FontColor = XLColor.Red;
                else
                    worksheet.Cell(row, 6).Style.Font.FontColor = XLColor.Green;

                row++;
            }
            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }, ct);
    }

    public async Task<byte[]> ExportSalesReportAsync(
        List<DetalleVentaDto> detalle,
        DateTime inicio,
        DateTime fin,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Ventas");

            worksheet.Cell(1, 1).Value = "FECHA LOCAL";
            worksheet.Cell(1, 2).Value = "MAQUINA";
            worksheet.Cell(1, 3).Value = "SLOT";
            worksheet.Cell(1, 4).Value = "PRODUCTO";
            worksheet.Cell(1, 5).Value = "COSTO";
            worksheet.Cell(1, 6).Value = "VENTA";
            worksheet.Cell(1, 7).Value = "GANANCIA";
            worksheet.Cell(1, 8).Value = "ESTADO";

            var rangoHeader = worksheet.Range("A1:H1");
            rangoHeader.Style.Font.Bold = true;
            rangoHeader.Style.Fill.BackgroundColor = HeaderBackground;

            int row = 2;
            foreach (var v in detalle)
            {
                worksheet.Cell(row, 1).Value = v.FechaRaw;
                worksheet.Cell(row, 1).Style.DateFormat.Format = DateTimeFormat;
                worksheet.Cell(row, 2).Value = v.Maquina;
                worksheet.Cell(row, 3).Value = v.Slot;
                worksheet.Cell(row, 4).Value = v.Producto;
                worksheet.Cell(row, 5).Value = v.CostoUnitario;
                worksheet.Cell(row, 6).Value = v.Monto;
                worksheet.Cell(row, 7).Value = v.Ganancia;
                worksheet.Cell(row, 8).Value = v.Estado;

                worksheet.Cell(row, 5).Style.NumberFormat.Format = CurrencyFormat;
                worksheet.Cell(row, 6).Style.NumberFormat.Format = CurrencyFormat;
                worksheet.Cell(row, 7).Style.NumberFormat.Format = CurrencyFormat;

                if (v.Ganancia > 0)
                    worksheet.Cell(row, 7).Style.Font.FontColor = XLColor.Green;

                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }, ct);
    }

    public async Task<byte[]> ExportPurchasingReportAsync(
        List<PurchaseSuggestionDto> items,
        int dias,
        int? maquinaId,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Sugerencia Compra");

            worksheet.Cell(1, 1).Value = "EN MÁQUINA";
            worksheet.Cell(1, 2).Value = "PRODUCTO";
            worksheet.Cell(1, 3).Value = $"VENTAS ({dias} DÍAS)";
            worksheet.Cell(1, 4).Value = "STOCK MÁQUINAS";
            worksheet.Cell(1, 5).Value = "STOCK BODEGA";
            worksheet.Cell(1, 6).Value = "SUGERIDO";

            var rangoHeader = worksheet.Range("A1:F1");
            rangoHeader.Style.Font.Bold = true;
            rangoHeader.Style.Fill.BackgroundColor = HeaderBackground;

            int row = 2;
            foreach (var item in items)
            {
                worksheet.Cell(row, 1).Value = item.EnMaquina ? "Sí" : "No";
                worksheet.Cell(row, 2).Value = item.NombreProducto;
                worksheet.Cell(row, 3).Value = item.VentasUltimos30Dias;
                worksheet.Cell(row, 4).Value = item.StockActualMaquinas;
                worksheet.Cell(row, 5).Value = item.StockBodega;
                worksheet.Cell(row, 6).Value = item.CantidadSugerida;

                if (item.CantidadSugerida > 0)
                {
                    var rowRange = worksheet.Range(row, 1, row, 6);
                    rowRange.Style.Fill.BackgroundColor = XLColor.LightYellow;
                    worksheet.Cell(row, 6).Style.Font.Bold = true;
                    worksheet.Cell(row, 6).Style.Font.FontColor = XLColor.Red;
                }

                if (item.EnMaquina)
                {
                    worksheet.Cell(row, 1).Style.Font.FontColor = XLColor.Green;
                    worksheet.Cell(row, 1).Style.Font.Bold = true;
                }
                else
                {
                    worksheet.Cell(row, 1).Style.Font.FontColor = XLColor.Gray;
                }

                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }, ct);
    }
}