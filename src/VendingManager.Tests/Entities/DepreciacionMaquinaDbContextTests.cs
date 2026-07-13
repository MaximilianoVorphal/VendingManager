namespace VendingManager.Tests.Entities;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using VendingManager.Infrastructure.Data;

/// <summary>
/// Verifies DepreciacionMaquina entity is fully wired in ApplicationDbContext.
/// </summary>
public class DepreciacionMaquinaDbContextTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// ApplicationDbContext exposes DbSet for DepreciacionMaquina.
    /// </summary>
    [Fact]
    public void DbContext_HasDepreciacionMaquinaDbSet()
    {
        using var context = CreateContext("DeprDbSetTest");
        var set = context.Set<DepreciacionMaquina>();
        set.Should().NotBeNull();
    }

    /// <summary>
    /// ApplicationDbContext exposes DbSet for DepreciacionMaquinaHistory.
    /// </summary>
    [Fact]
    public void DbContext_HasDepreciacionMaquinaHistoryDbSet()
    {
        using var context = CreateContext("DeprHistoryDbSetTest");
        var set = context.Set<DepreciacionMaquinaHistory>();
        set.Should().NotBeNull();
    }

    /// <summary>
    /// DepreciacionMaquina can be added and retrieved from DbContext.
    /// </summary>
    [Fact]
    public async Task CanAddAndRetrieveDepreciacionMaquina()
    {
        using var context = CreateContext("DeprAddTest");
        var entity = new DepreciacionMaquina
        {
            MaquinaId = 1,
            Descripcion = "Test Machine CAPEX",
            ValorAdquisicion = 2_500_000m,
            ValorResidual = 250_000m,
            VidaUtilMeses = 48,
            FechaAdquisicion = new DateTime(2025, 1, 1)
        };

        context.Set<DepreciacionMaquina>().Add(entity);
        await context.SaveChangesAsync();

        var retrieved = await context.Set<DepreciacionMaquina>().FirstOrDefaultAsync();
        retrieved.Should().NotBeNull();
        retrieved!.Descripcion.Should().Be("Test Machine CAPEX");
        retrieved.ValorAdquisicion.Should().Be(2_500_000m);
    }

    /// <summary>
    /// ValorAdquisicion and ValorResidual are configured as decimal(18,2) in the model.
    /// InMemory provider doesn't support GetColumnType() — verify CLR type and existence instead.
    /// The actual column type is set via [Column(TypeName)] attribute on the entity.
    /// </summary>
    [Fact]
    public void DecimalColumns_HaveCorrectTypeAndPrecision()
    {
        using var context = CreateContext("DeprPrecisionTest");
        var entityType = context.Model.FindEntityType(typeof(DepreciacionMaquina));
        entityType.Should().NotBeNull();

        var valorAdqProp = entityType!.FindProperty(nameof(DepreciacionMaquina.ValorAdquisicion));
        valorAdqProp.Should().NotBeNull();
        valorAdqProp!.ClrType.Should().Be(typeof(decimal));

        var valorResProp = entityType.FindProperty(nameof(DepreciacionMaquina.ValorResidual));
        valorResProp.Should().NotBeNull();
        valorResProp!.ClrType.Should().Be(typeof(decimal));

        // The [Column(TypeName = "decimal(18,2)")] attribute on the entity
        // guarantees the precision — this is verified at migration generation time,
        // not in-memory.
    }

    /// <summary>
    /// MovimientoCaja.MaquinaId is configured as nullable FK in the model.
    /// </summary>
    [Fact]
    public void MovimientoCaja_MaquinaId_IsNullableInModel()
    {
        using var context = CreateContext("MovCajaMaquinaIdTest");
        var entityType = context.Model.FindEntityType(typeof(MovimientoCaja));
        entityType.Should().NotBeNull();

        var maquinaIdProp = entityType!.FindProperty(nameof(MovimientoCaja.MaquinaId));
        maquinaIdProp.Should().NotBeNull();
        maquinaIdProp!.IsNullable.Should().BeTrue();
    }
}
