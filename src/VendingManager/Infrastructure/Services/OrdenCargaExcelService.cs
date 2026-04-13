using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

namespace VendingManager.Infrastructure.Services
{
    public class OrdenCargaExcelService : IOrdenCargaExcelService
    {
        public async Task<byte[]> ExportarListaCarga(List<StockCriticoDto> items)
        {
            try
            {
                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("Carga Sugerida");

                    worksheet.Cell(1, 1).Value = "Máquina";
                    worksheet.Cell(1, 2).Value = "Slot";
                    worksheet.Cell(1, 3).Value = "Producto";
                    worksheet.Cell(1, 4).Value = "Stock Actual";
                    worksheet.Cell(1, 5).Value = "Capacidad";
                    worksheet.Cell(1, 6).Value = "A Cargar";

                    var headerRange = worksheet.Range("A1:F1");
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;
                    headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thick;

                    int row = 2;
                    foreach (var item in items)
                    {
                        worksheet.Cell(row, 1).Value = item.Maquina;
                        worksheet.Cell(row, 2).Value = item.NumeroSlot;
                        
                        worksheet.Cell(row, 2).Style.NumberFormat.Format = "@";

                        worksheet.Cell(row, 3).Value = item.Producto;
                        worksheet.Cell(row, 4).Value = item.StockActual;
                        worksheet.Cell(row, 5).Value = item.CapacidadMaxima;
                        
                        int carga = Math.Max(0, item.CapacidadMaxima - item.StockActual);
                        worksheet.Cell(row, 6).Value = carga;

                        if (carga > 0)
                        {
                            worksheet.Cell(row, 6).Style.Font.Bold = true;
                            worksheet.Cell(row, 6).Style.Fill.BackgroundColor = XLColor.LightYellow;
                        }

                        row++;
                    }

                    var summary = items
                        .Select(x => new 
                        { 
                            Producto = x.Producto, 
                            Carga = Math.Max(0, x.CapacidadMaxima - x.StockActual) 
                        })
                        .Where(x => x.Carga > 0)
                        .GroupBy(x => x.Producto)
                        .Select(g => new { Producto = g.Key, Total = g.Sum(x => x.Carga) })
                        .OrderBy(x => x.Producto)
                        .ToList();

                    int summaryCol = 9;
                    
                    worksheet.Cell(1, summaryCol).Value = "Producto (Resumen)";
                    worksheet.Cell(1, summaryCol + 1).Value = "Total Unidades";

                    var summaryHeader = worksheet.Range(1, summaryCol, 1, summaryCol + 1);
                    summaryHeader.Style.Font.Bold = true;
                    summaryHeader.Style.Fill.BackgroundColor = XLColor.LightGreen; 
                    summaryHeader.Style.Border.BottomBorder = XLBorderStyleValues.Thick;
                    summaryHeader.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                    int summaryRow = 2;
                    foreach(var item in summary)
                    {
                        worksheet.Cell(summaryRow, summaryCol).Value = item.Producto;
                        worksheet.Cell(summaryRow, summaryCol + 1).Value = item.Total;
                        
                         var range = worksheet.Range(summaryRow, summaryCol, summaryRow, summaryCol + 1);
                         range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

                        summaryRow++;
                    }
                    
                    if (summaryRow > 2)
                    {
                        worksheet.Cell(summaryRow, summaryCol).Value = "TOTAL GENERAL";
                        worksheet.Cell(summaryRow, summaryCol).Style.Font.Bold = true;
                        
                        worksheet.Cell(summaryRow, summaryCol + 1).FormulaA1 = $"SUM(J2:J{summaryRow-1})";
                        worksheet.Cell(summaryRow, summaryCol + 1).Style.Font.Bold = true;
                    }

                    worksheet.Columns().AdjustToContents();

                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        return stream.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error exportando lista de carga: {ex.Message}");
                throw; 
            }
        }
    }
}
