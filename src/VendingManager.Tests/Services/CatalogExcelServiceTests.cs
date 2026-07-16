namespace VendingManager.Tests.Services;

using ClosedXML.Excel;
using VendingManager.Core.Entities;
using VendingManager.Infrastructure.Services;
using VendingManager.Tests.TestData;

/// <summary>
/// Covers <see cref="CatalogExcelService.ImportarCatalogoProductos"/> signature
/// validation (M-1b, REQ-UPLOAD-02): content is sniffed BEFORE handing the stream
/// to ExcelReaderFactory, avoiding a generic parse-exception leak on non-XLSX
/// content renamed with a .xlsx extension.
/// </summary>
public class CatalogExcelServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly CatalogExcelService _service;

    static CatalogExcelServiceTests()
    {
        // ExcelDataReader needs the legacy code-page provider registered to read
        // genuine XLSX streams (used only by the "valid file" tests below).
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    public CatalogExcelServiceTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"CatalogExcelServiceTestDb_{Guid.NewGuid()}");
        _service = new CatalogExcelService(_context);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task ImportarCatalogoProductos_NonXlsxRenamedAsXlsx_ThrowsArgumentException()
    {
        // Arrange: plain text content, renamed with a .xlsx extension.
        using var stream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes("not really an xlsx file"));

        // Act
        var act = () => _service.ImportarCatalogoProductos(stream, "catalogo.xlsx");

        // Assert — rejected before ExcelReaderFactory ever sees it (no generic parse-exception leak).
        await act.Should().ThrowAsync<ArgumentException>();
        (await _context.Productos.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportarCatalogoProductos_GenuineXlsx_ImportsUnchanged()
    {
        // Arrange: a real minimal XLSX built via ClosedXML, matching the columns
        // ImportarCatalogoProductos expects.
        using var stream = BuildGenuineCatalogXlsx();

        // Act
        var result = await _service.ImportarCatalogoProductos(stream, "catalogo.xlsx");

        // Assert — unchanged import behavior: the new product was created.
        result.Should().Contain("1 productos nuevos");
        var producto = await _context.Productos.FirstOrDefaultAsync(p => p.CodigoBarras == "7801234567890");
        producto.Should().NotBeNull();
        producto!.Nombre.Should().Be("Bebida Cola 350ml");
    }

    private static MemoryStream BuildGenuineCatalogXlsx()
    {
        var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add("Productos");
            worksheet.Cell(1, 1).Value = "Product Barcode";
            worksheet.Cell(1, 2).Value = "Product Name";
            worksheet.Cell(1, 3).Value = "Cost Price";
            worksheet.Cell(1, 4).Value = "Supplier";
            worksheet.Cell(1, 5).Value = "Type";
            worksheet.Cell(1, 6).Value = "Current Stock";

            worksheet.Cell(2, 1).Value = "7801234567890";
            worksheet.Cell(2, 2).Value = "Bebida Cola 350ml";
            worksheet.Cell(2, 3).Value = 500;
            worksheet.Cell(2, 4).Value = "Proveedor Test";
            worksheet.Cell(2, 5).Value = "Bebida";
            worksheet.Cell(2, 6).Value = 10;

            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        return stream;
    }
}
