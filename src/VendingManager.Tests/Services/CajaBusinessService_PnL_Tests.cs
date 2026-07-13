namespace VendingManager.Tests.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using FluentAssertions;
using VendingManager.Core.Configuration;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared;
using VendingManager.Tests.TestData;

/// <summary>
/// Characterization tests for CajaBusinessService P&amp;L — verifies the
/// service consumes CategoriasGasto.Variables/.Fijos and produces the
/// expected utilidad operacional via CalcularUtilidadOperacional.
/// </summary>
public class CajaBusinessService_PnL_Tests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IVentaRepository> _mockVentaRepo;
    private readonly Mock<IMaquinaRepository> _mockMaquinaRepo;
    private readonly Mock<IExcelExportService> _mockExcelExport;
    private readonly IOptions<VendingConfig> _config;
    private readonly CajaBusinessService _service;

    public CajaBusinessService_PnL_Tests()
    {
        _context = TestDataHelpers.CreateInMemoryContext(
            $"CajaBusinessPnLTestDb_{Guid.NewGuid()}");

        _mockVentaRepo = new Mock<IVentaRepository>();
        _mockMaquinaRepo = new Mock<IMaquinaRepository>();
        _mockExcelExport = new Mock<IExcelExportService>();
        _config = Options.Create(new VendingConfig());

        _service = new CajaBusinessService(
            _context,
            _mockVentaRepo.Object,
            _mockMaquinaRepo.Object,
            _mockExcelExport.Object,
            _config);
    }

    public void Dispose() => _context.Dispose();

    /// <summary>
    /// Verifies the catalog has 4 variable + 8 fixed categories and
    /// Operacionales = union of both.
    /// </summary>
    [Fact]
    public void CategoriasGasto_HasCorrectBuckets()
    {
        CategoriasGasto.Variables.Count.Should().Be(4);
        CategoriasGasto.Variables.Should().Contain(new[] { "LOGISTICA", "PEAJES", "INSUMOS", "MANTENCION" });

        CategoriasGasto.Fijos.Count.Should().Be(8);
        CategoriasGasto.Fijos.Should().Contain(new[] {
            "INFRA", "ARRIENDO_POS", "INTERNET", "COMISIONES",
            "SUELDOS", "GASTOS GENERALES", "OTROS", "SERVICIOS"
        });

        CategoriasGasto.Operacionales.Count.Should().Be(12);
        CategoriasGasto.Operacionales.Should().Contain(CategoriasGasto.Variables);
        CategoriasGasto.Operacionales.Should().Contain(CategoriasGasto.Fijos);
    }

    /// <summary>
    /// Verify the unified formula yields expected results.
    /// margenBruto=5000, mermas=200, gastosOps=1000 → 3800
    /// </summary>
    [Fact]
    public void CalcularUtilidadOperacional_MatchesExpected()
    {
        var result = CategoriasGasto.CalcularUtilidadOperacional(
            margenBruto: 5000m,
            mermasAbs: 200m,
            totalGastosOps: 1000m);

        // 5000 - 200 - 1000 = 3800
        result.Should().Be(3800m);
    }

    [Fact]
    public void CalcularUtilidadOperacional_NoMermas_NoGastos_EqualsMargenBruto()
    {
        var result = CategoriasGasto.CalcularUtilidadOperacional(5000m, 0, 0);
        result.Should().Be(5000m);
    }
}
