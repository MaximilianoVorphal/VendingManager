using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VendingManager.Core.Entities;
using VendingManager.Infrastructure.Interceptors;
using Xunit;

namespace VendingManager.Tests.Interceptors;

public class AuditSaveChangesInterceptorTests
{
    private static ApplicationDbContext CreateContextWithInterceptor(
        AuditSaveChangesInterceptor interceptor,
        string databaseName = "TestDb")
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(interceptor)
            .Options;

        return new ApplicationDbContext(options);
    }

    private static AuditSaveChangesInterceptor CreateInterceptor(IHttpContextAccessor? accessor = null)
    {
        return accessor is null
            ? new AuditSaveChangesInterceptor()
            : new AuditSaveChangesInterceptor(accessor);
    }

    [Fact]
    public async Task SavingChangesAsync_WhenAddedEntity_CreatesAuditoriaRecordWithAfterJson()
    {
        // Arrange
        var interceptor = CreateInterceptor();
        using var context = CreateContextWithInterceptor(interceptor, "AddedEntityTest");

        var producto = new Producto
        {
            Nombre = "Test Product",
            SKU = "SKU-001",
            CostoPromedio = 100m,
            StockBodega = 10,
            PrecioVenta = 200m
        };
        context.Productos.Add(producto);

        // Act
        await context.SaveChangesAsync();

        // Assert
        var auditRecord = await context.Auditoria.FirstOrDefaultAsync();
        auditRecord.Should().NotBeNull();
        auditRecord!.Accion.Should().Be("Added");
        auditRecord.Detalle.Should().Contain("Producto");
        auditRecord.Usuario.Should().Be("system");
        auditRecord.Fecha.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SavingChangesAsync_WhenModifiedEntity_CreatesAuditoriaRecordWithBeforeAndAfterJson()
    {
        // Arrange
        var interceptor = CreateInterceptor();
        using var context = CreateContextWithInterceptor(interceptor, "ModifiedEntityTest");

        var producto = new Producto
        {
            Id = 999,
            Nombre = "Original Name",
            SKU = "SKU-002",
            CostoPromedio = 100m,
            StockBodega = 10,
            PrecioVenta = 200m
        };
        context.Productos.Add(producto);
        await context.SaveChangesAsync();

        // Detach and re-attach to simulate a modify scenario
        context.ChangeTracker.Clear();

        var existingProducto = await context.Productos.FindAsync(999);
        existingProducto!.Nombre = "Updated Name";

        // Act
        await context.SaveChangesAsync();

        // Assert
        var auditRecords = await context.Auditoria.ToListAsync();
        auditRecords.Should().HaveCount(2); // One for add, one for modify

        var modifyRecord = auditRecords.Last();
        modifyRecord.Accion.Should().Be("Modified");
        modifyRecord.Detalle.Should().Contain("Producto #999 Modified");
    }

    [Fact]
    public async Task SavingChangesAsync_WhenDeletedEntity_CreatesAuditoriaRecordWithBeforeJson()
    {
        // Arrange
        var interceptor = CreateInterceptor();
        using var context = CreateContextWithInterceptor(interceptor, "DeletedEntityTest");

        var producto = new Producto
        {
            Id = 888,
            Nombre = "To Be Deleted",
            SKU = "SKU-003",
            CostoPromedio = 50m,
            StockBodega = 5,
            PrecioVenta = 100m
        };
        context.Productos.Add(producto);
        await context.SaveChangesAsync();

        // Detach and re-attach to simulate delete
        context.ChangeTracker.Clear();

        var existingProducto = await context.Productos.FindAsync(888);
        context.Productos.Remove(existingProducto!);

        // Act
        await context.SaveChangesAsync();

        // Assert
        var auditRecords = await context.Auditoria.ToListAsync();
        auditRecords.Should().HaveCount(2); // Add + Delete

        var deleteRecord = auditRecords.Last();
        deleteRecord.Accion.Should().Be("Deleted");
        deleteRecord.Detalle.Should().Contain("Producto #888 Deleted");
    }

    [Fact]
    public async Task SavingChangesAsync_WhenNoChanges_DoesNotWriteAuditoria()
    {
        // Arrange
        var interceptor = CreateInterceptor();
        using var context = CreateContextWithInterceptor(interceptor, "NoChangesTest");

        // Act
        await context.SaveChangesAsync();

        // Assert
        var count = await context.Auditoria.CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task SavingChangesAsync_WithCircularReferences_DoesNotThrow()
    {
        // Arrange
        var interceptor = CreateInterceptor();
        using var context = CreateContextWithInterceptor(interceptor, "CircularRefTest");

        // Maquina has navigation properties that could cause cycles
        var maquina = new Maquina
        {
            Id = 777,
            Nombre = "Machine with slots",
            Ubicacion = "Test Location"
        };
        context.Maquinas.Add(maquina);

        // Act & Assert — should not throw despite navigation properties
        var action = async () => await context.SaveChangesAsync();
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SavingChangesAsync_MultipleEntitiesInSameSaveChanges_CreatesMultipleAuditRecords()
    {
        // Arrange
        var interceptor = CreateInterceptor();
        using var context = CreateContextWithInterceptor(interceptor, "MultipleEntitiesTest");

        var producto1 = new Producto { Nombre = "Product 1", SKU = "P1", CostoPromedio = 10m, StockBodega = 1, PrecioVenta = 20m };
        var producto2 = new Producto { Nombre = "Product 2", SKU = "P2", CostoPromedio = 15m, StockBodega = 2, PrecioVenta = 30m };
        var producto3 = new Producto { Nombre = "Product 3", SKU = "P3", CostoPromedio = 25m, StockBodega = 3, PrecioVenta = 50m };

        context.Productos.AddRange(producto1, producto2, producto3);

        // Act
        await context.SaveChangesAsync();

        // Assert
        var auditRecords = await context.Auditoria.ToListAsync();
        auditRecords.Should().HaveCount(3);
        auditRecords.All(r => r.Accion == "Added").Should().BeTrue();
    }

    [Fact]
    public async Task SavingChangesAsync_ResolvedEntityId_IsCorrect()
    {
        // Arrange
        var interceptor = CreateInterceptor();
        using var context = CreateContextWithInterceptor(interceptor, "EntityIdTest");

        var producto = new Producto
        {
            Nombre = "EntityId Test",
            SKU = "EID-001",
            CostoPromedio = 100m,
            StockBodega = 10,
            PrecioVenta = 200m
        };
        context.Productos.Add(producto);
        await context.SaveChangesAsync();

        // Act
        context.ChangeTracker.Clear();
        var existing = await context.Productos.FirstAsync();
        existing.Nombre = "Updated";
        await context.SaveChangesAsync();

        // Assert
        var modifyRecord = await context.Auditoria.Where(a => a.Accion == "Modified").FirstOrDefaultAsync();
        modifyRecord.Should().NotBeNull();
        modifyRecord!.Detalle.Should().Contain("#"); // Should have entity ID in format "#{id}"
    }

    [Fact]
    public async Task SavingChangesAsync_WhenHttpContextUserExists_UsuarioIsFromClaim()
    {
        // Arrange
        var httpContextAccessor = new MockHttpContextAccessor("testuser@example.com");
        var interceptor = CreateInterceptor(httpContextAccessor);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("HttpContextUsuarioTest")
            .AddInterceptors(interceptor)
            .Options;

        using var context = new ApplicationDbContext(options);

        var producto = new Producto
        {
            Nombre = "HttpContext Test",
            SKU = "HC-001",
            CostoPromedio = 100m,
            StockBodega = 10,
            PrecioVenta = 200m
        };
        context.Productos.Add(producto);

        // Act
        await context.SaveChangesAsync();

        // Assert
        var auditRecord = await context.Auditoria.FirstOrDefaultAsync();
        auditRecord.Should().NotBeNull();
        auditRecord!.Usuario.Should().Be("testuser@example.com");
    }

    // ─── ProveedorCatalog Audit (T26/T27) ────────────────────────────

    [Fact]
    public async Task SavingChangesAsync_WhenProveedorCatalogCreated_WritesProveedorCatalogHistoryWithInsert()
    {
        // Arrange
        var interceptor = CreateInterceptor();
        using var context = CreateContextWithInterceptor(interceptor, "ProveedorCatalogInsertTest");

        var catalog = new ProveedorCatalog
        {
            NombreCanonical = "Test Supplier"
        };
        context.ProveedorCatalog.Add(catalog);

        // Act
        await context.SaveChangesAsync();

        // Assert: ProveedorCatalogHistory row written
        var historyRecord = await context.Set<ProveedorCatalogHistory>().FirstOrDefaultAsync();
        historyRecord.Should().NotBeNull();
        historyRecord!.Action.Should().Be("Added");
        historyRecord.EntityId.Should().Be(catalog.Id);
        historyRecord.NombreCanonical.Should().Be("Test Supplier");
        historyRecord.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        historyRecord.Usuario.Should().Be("system");
    }

    [Fact]
    public async Task SavingChangesAsync_WhenProveedorCatalogRenamed_WritesProveedorCatalogHistoryWithUpdate()
    {
        // Arrange
        var interceptor = CreateInterceptor();
        using var context = CreateContextWithInterceptor(interceptor, "ProveedorCatalogRenameTest");

        var catalog = new ProveedorCatalog
        {
            Id = 42,
            NombreCanonical = "Original Name"
        };
        context.ProveedorCatalog.Add(catalog);
        await context.SaveChangesAsync();

        // Detach and re-attach to simulate a modify
        context.ChangeTracker.Clear();

        var existing = await context.ProveedorCatalog.FindAsync(42);
        existing!.NombreCanonical = "Updated Name";

        // Act
        await context.SaveChangesAsync();

        // Assert
        var historyRecords = await context.Set<ProveedorCatalogHistory>()
            .OrderBy(h => h.Id)
            .ToListAsync();
        historyRecords.Should().HaveCount(2); // Insert + Update

        var updateRecord = historyRecords.Last();
        updateRecord.Action.Should().Be("Modified");
        updateRecord.EntityId.Should().Be(42);
    }

    private class MockHttpContextAccessor : IHttpContextAccessor
    {
        public MockHttpContextAccessor(string username)
        {
            var claims = new Claim[]
            {
                new Claim(ClaimTypes.Name, username)
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            HttpContext = new DefaultHttpContext
            {
                User = principal
            };
        }

        public HttpContext? HttpContext { get; set; }
    }
}