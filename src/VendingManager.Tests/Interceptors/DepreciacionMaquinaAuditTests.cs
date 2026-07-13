namespace VendingManager.Tests.Interceptors;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VendingManager.Infrastructure.Data;
using VendingManager.Infrastructure.Interceptors;

/// <summary>
/// Verifies that AuditSaveChangesInterceptor creates history records for DepreciacionMaquina.
/// </summary>
public class DepreciacionMaquinaAuditTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var interceptor = new AuditSaveChangesInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(interceptor)
            .Options;
        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// Adding a DepreciacionMaquina creates both an Auditoria record and a DepreciacionMaquinaHistory record.
    /// </summary>
    [Fact]
    public async Task SavingChanges_AddedDepreciacionMaquina_CreatesHistoryRecord()
    {
        using var context = CreateContext("DeprAuditAddedTest");

        var entity = new DepreciacionMaquina
        {
            MaquinaId = 7,
            Descripcion = "Internet M-007 CAPEX",
            ValorAdquisicion = 2_000_000m,
            ValorResidual = 200_000m,
            VidaUtilMeses = 36,
            FechaAdquisicion = new DateTime(2025, 1, 1)
        };

        context.Set<DepreciacionMaquina>().Add(entity);
        await context.SaveChangesAsync();

        // Verify Auditoria record was created
        var audit = await context.Auditoria
            .FirstOrDefaultAsync(a => a.EntityType == "DepreciacionMaquina");
        audit.Should().NotBeNull("an audit record must exist for DepreciacionMaquina");
        audit!.Accion.Should().Be("Added");

        // Verify DepreciacionMaquinaHistory record was created
        var history = await context.Set<DepreciacionMaquinaHistory>()
            .FirstOrDefaultAsync(h => h.EntityId == entity.Id);
        history.Should().NotBeNull("a history record must exist for DepreciacionMaquina");
        history!.Action.Should().Be("Added");
        history.MaquinaId.Should().Be(7);
        history.Descripcion.Should().Be("Internet M-007 CAPEX");
        history.ValorAdquisicion.Should().Be(2_000_000m);
    }

    /// <summary>
    /// Modifying a DepreciacionMaquina creates a history record with the updated values.
    /// </summary>
    [Fact]
    public async Task SavingChanges_ModifiedDepreciacionMaquina_CreatesHistoryRecord()
    {
        using var context = CreateContext("DeprAuditModifiedTest");

        var entity = new DepreciacionMaquina
        {
            MaquinaId = 7,
            Descripcion = "Original CAPEX",
            ValorAdquisicion = 2_000_000m,
            ValorResidual = 200_000m,
            VidaUtilMeses = 36,
            FechaAdquisicion = new DateTime(2025, 1, 1)
        };

        context.Set<DepreciacionMaquina>().Add(entity);
        await context.SaveChangesAsync();

        // Modify
        entity.Descripcion = "Updated CAPEX";
        entity.ValorAdquisicion = 2_500_000m;
        await context.SaveChangesAsync();

        // Should have 2 history records: Added + Modified
        var histories = await context.Set<DepreciacionMaquinaHistory>()
            .Where(h => h.EntityId == entity.Id)
            .OrderBy(h => h.Id)
            .ToListAsync();

        histories.Should().HaveCount(2);
        histories[0].Action.Should().Be("Added");
        histories[1].Action.Should().Be("Modified");
        histories[1].AfterJson.Should().NotBeNull();
        histories[1].AfterJson.Should().Contain("Updated CAPEX");
    }
}
