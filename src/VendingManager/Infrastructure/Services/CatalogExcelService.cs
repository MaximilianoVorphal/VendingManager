using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ExcelDataReader;
using ClosedXML.Excel;
using System.Data;
using VendingManager.Infrastructure.Data;
using VendingManager.Core.Interfaces;
using VendingManager.Core.Entities;
using VendingManager.Core.Utils;

namespace VendingManager.Infrastructure.Services
{
    public class CatalogExcelService : ICatalogExcelService
    {
        private readonly ApplicationDbContext _context;

        public CatalogExcelService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<string> ImportarCatalogoProductos(Stream fileStream, string nombreArchivo)
        {
            try
            {
                // Buffer into a seekable MemoryStream and sniff content BEFORE
                // handing it to ExcelReaderFactory — avoids a generic parse-exception
                // leak on non-XLSX content renamed with a .xlsx extension.
                using var bufferedStream = new MemoryStream();
                await fileStream.CopyToAsync(bufferedStream);
                var contentBytes = bufferedStream.ToArray();
                FileSignatureValidator.Validate(contentBytes, AllowedFormats.Xlsx);
                bufferedStream.Position = 0;

                using (var reader = ExcelReaderFactory.CreateReader(bufferedStream))
                {
                    var conf = new ExcelDataSetConfiguration
                    {
                        ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
                    };
                    var dataSet = reader.AsDataSet(conf);
                    var tabla = dataSet.Tables[0];
                    Console.WriteLine($"📦 CATÁLOGO: Procesando {tabla.Rows.Count} productos...");

                    int colId = -1, colBarcode = -1, colName = -1, colPrice = -1, colCost = -1, colSupplier = -1, colType = -1, colStock = -1;

                    for (int i = 0; i < tabla.Columns.Count; i++)
                    {
                        string h = tabla.Columns[i].ColumnName.Trim().ToLower();

                        if (colId == -1 && (h.Contains("system id") || h == "id")) colId = i;
                        else if (colBarcode == -1 && h.Contains("product barcode")) colBarcode = i;
                        else if (colName == -1 && h.Contains("product name")) colName = i;
                        else if (colPrice == -1 && (h.Contains("unit price") || h.Contains("reference price"))) colPrice = i;
                        else if (colCost == -1 && h.Contains("cost price")) colCost = i;
                        else if (colSupplier == -1 && h.Contains("supplier")) colSupplier = i;
                        else if (colType == -1 && h.Contains("type")) colType = i;
                        else if (colStock == -1 && h.Contains("current stock")) colStock = i;
                    }

                    if ((colId == -1 && colBarcode == -1) || colName == -1)
                    {
                        string errorMsg = "🔥 ERROR CATÁLOGO: Faltan columnas clave (ID o Barcode) y Name. Verifique el archivo.";
                        Console.WriteLine(errorMsg);
                        return errorMsg;
                    }

                    int nuevos = 0;
                    int actualizados = 0;

                    foreach (DataRow row in tabla.Rows)
                    {
                        string nombre = row[colName]?.ToString()?.Trim() ?? "Sin Nombre";
                        if (string.IsNullOrEmpty(nombre)) continue;

                        Producto? producto = null;
                        if (colId != -1)
                        {
                            string sId = row[colId]?.ToString()?.Trim() ?? "";
                            if (int.TryParse(sId, out int id) && id > 0)
                            {
                                producto = await _context.Productos.FindAsync(id);
                            }
                        }

                        string barcode = "";
                        if (producto == null && colBarcode != -1)
                        {
                            var rawBarcode = row[colBarcode];
                            barcode = rawBarcode?.ToString()?.Trim() ?? "";
                            if (double.TryParse(barcode, out double dBarcode))
                            {
                                barcode = dBarcode.ToString("F0");
                            }

                            if (!string.IsNullOrEmpty(barcode))
                            {
                                producto = await _context.Productos.FirstOrDefaultAsync(p => p.CodigoBarras == barcode);
                            }
                        }

                        decimal precio = ParseDecimal(colPrice != -1 ? row[colPrice] : null);
                        decimal costo = ParseDecimal(colCost != -1 ? row[colCost] : null);
                        int stock = (int)ParseDecimal(colStock != -1 ? row[colStock] : null);

                        string proveedor = colSupplier != -1 ? row[colSupplier]?.ToString()?.Trim() ?? "" : "";
                        string categoria = colType != -1 ? row[colType]?.ToString()?.Trim() ?? "" : "";

                        if (producto == null)
                        {
                            if (string.IsNullOrEmpty(barcode))
                            {
                                continue;
                            }

                            producto = new Producto
                            {
                                CodigoBarras = barcode,
                                Nombre = nombre,
                                PrecioVenta = precio,
                                CostoPromedio = costo,
                                Proveedor = proveedor,
                                Categoria = categoria,
                                StockBodega = (colStock != -1) ? stock : 0,
                                SKU = barcode
                            };
                            _context.Productos.Add(producto);
                            nuevos++;
                        }
                        else
                        {
                            producto.Nombre = nombre;
                            if (!string.IsNullOrEmpty(barcode) && producto.CodigoBarras != barcode)
                            {
                                producto.CodigoBarras = barcode;
                            }

                            if (precio > 0) producto.PrecioVenta = precio;
                            if (colCost != -1) producto.CostoPromedio = costo;

                            if (!string.IsNullOrEmpty(proveedor)) producto.Proveedor = proveedor;
                            if (!string.IsNullOrEmpty(categoria)) producto.Categoria = categoria;
                            if (colStock != -1) producto.StockBodega = stock;

                            actualizados++;
                        }
                    }

                    await _context.SaveChangesAsync();
                    string resultado = $"✅ PROCESO COMPLETADO: {actualizados} productos actualizados, {nuevos} productos nuevos creados.";
                    Console.WriteLine(resultado);
                    return resultado;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ERROR CATÁLOGO: {ex.Message}");
                throw;
            }
        }

        public async Task<byte[]> ExportarCatalogoProductos()
        {
            try
            {
                var productos = await _context.Productos.OrderBy(p => p.Nombre).ToListAsync();

                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("Productos");

                    worksheet.Cell(1, 1).Value = "System ID";
                    worksheet.Cell(1, 2).Value = "Product Barcode";
                    worksheet.Cell(1, 3).Value = "Product Name";
                    worksheet.Cell(1, 4).Value = "Cost Price";
                    worksheet.Cell(1, 5).Value = "Supplier";
                    worksheet.Cell(1, 6).Value = "Type";
                    worksheet.Cell(1, 7).Value = "Current Stock";

                    var headerRange = worksheet.Range("A1:G1");
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
                    headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thick;

                    int row = 2;
                    foreach (var p in productos)
                    {
                        worksheet.Cell(row, 1).Value = p.Id;
                        worksheet.Cell(row, 2).Style.NumberFormat.Format = "@";
                        worksheet.Cell(row, 2).Value = p.CodigoBarras;
                        worksheet.Cell(row, 3).Value = p.Nombre;
                        worksheet.Cell(row, 4).Value = p.CostoPromedio;
                        worksheet.Cell(row, 5).Value = p.Proveedor;
                        worksheet.Cell(row, 6).Value = p.Categoria;
                        worksheet.Cell(row, 7).Value = p.StockBodega;
                        row++;
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
                Console.WriteLine($"Error exportando catálogo: {ex.Message}");
                throw;
            }
        }

        private decimal ParseDecimal(object? value)
        {
            if (value == null || value == DBNull.Value) return 0;
            string s = value.ToString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(s)) return 0;

            s = s.Replace("$", "").Replace(" ", "");

            if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal result))
            {
                return result;
            }
            return 0;
        }
    }
}
