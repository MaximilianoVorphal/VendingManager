namespace VendingManager.Tests.Controllers;

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using VendingManager.Controllers;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.DTOs;
using VendingManager.Tests.TestData;

/// <summary>
/// Controller-level tests for ProveedoresController.
/// Tests HTTP shape (status codes, routing, error mapping) with mocked service.
/// </summary>
public class ProveedoresControllerTests
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IProveedorMatchingService> _mockMatchingService;
    private readonly ProveedoresController _controller;

    public ProveedoresControllerTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"ProveedoresControllerTest_{Guid.NewGuid()}");
        _mockMatchingService = new Mock<IProveedorMatchingService>();
        _controller = new ProveedoresController(_context, _mockMatchingService.Object);
    }

    [Fact]
    public async Task GetProveedores_ReturnsListOrderedByNombreCanonical()
    {
        // Arrange
        _context.ProveedorCatalog.AddRange(
            new ProveedorCatalog { NombreCanonical = "Zeta Supplier" },
            new ProveedorCatalog { NombreCanonical = "Alpha Supplier" },
            new ProveedorCatalog { NombreCanonical = "Beta Supplier" }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.GetProveedores();

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<List<ProveedorCatalogDto>>().Subject;
        list.Should().HaveCount(3);
        list[0].NombreCanonical.Should().Be("Alpha Supplier");
        list[1].NombreCanonical.Should().Be("Beta Supplier");
        list[2].NombreCanonical.Should().Be("Zeta Supplier");
    }

    [Fact]
    public async Task GetProveedores_WhenEmpty_ReturnsEmptyList()
    {
        // Act
        var result = await _controller.GetProveedores();

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<List<ProveedorCatalogDto>>().Subject;
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateProveedor_WithValidName_Returns201WithDto()
    {
        // Arrange
        var request = new CrearProveedorRequestDto { NombreCanonical = "New Supplier" };

        // Act
        var result = await _controller.CreateProveedor(request);

        // Assert
        var createdResult = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.ActionName.Should().Be(nameof(ProveedoresController.GetProveedores));
        var dto = createdResult.Value.Should().BeAssignableTo<ProveedorCatalogDto>().Subject;
        dto.Id.Should().BeGreaterThan(0);
        dto.NombreCanonical.Should().Be("New Supplier");

        // Verify persistence
        var saved = await _context.ProveedorCatalog.FindAsync(dto.Id);
        saved.Should().NotBeNull();
        saved!.NombreCanonical.Should().Be("New Supplier");
    }

    [Fact]
    public async Task CreateProveedor_WithDuplicateName_Returns409Conflict()
    {
        // Arrange
        _context.ProveedorCatalog.Add(new ProveedorCatalog { NombreCanonical = "Existing" });
        await _context.SaveChangesAsync();

        var request = new CrearProveedorRequestDto { NombreCanonical = "Existing" };

        // Act
        var result = await _controller.CreateProveedor(request);

        // Assert
        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task CreateProveedor_WithEmptyName_ReturnsBadRequest()
    {
        // Arrange
        var request = new CrearProveedorRequestDto { NombreCanonical = "" };

        // Act
        var result = await _controller.CreateProveedor(request);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ─── PUT UpdateProveedor (T2-T6) ─────────────────────────────────

    [Fact]
    public async Task UpdateProveedor_WithValidName_Returns200AndUpdates()
    {
        // Arrange
        _context.ProveedorCatalog.Add(new ProveedorCatalog { NombreCanonical = "Old Name" });
        await _context.SaveChangesAsync();
        var dto = new ActualizarProveedorRequestDto { NombreCanonical = "New Name" };

        // Act
        var result = await _controller.UpdateProveedor(1, dto);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var updated = okResult.Value.Should().BeAssignableTo<ProveedorCatalogDto>().Subject;
        updated.Id.Should().Be(1);
        updated.NombreCanonical.Should().Be("New Name");

        // Verify persistence
        var saved = await _context.ProveedorCatalog.FindAsync(1);
        saved.Should().NotBeNull();
        saved!.NombreCanonical.Should().Be("New Name");
    }

    [Fact]
    public async Task UpdateProveedor_WithDuplicateNameOtherId_Returns409Conflict()
    {
        // Arrange
        _context.ProveedorCatalog.AddRange(
            new ProveedorCatalog { Id = 1, NombreCanonical = "Supplier A" },
            new ProveedorCatalog { Id = 2, NombreCanonical = "Supplier B" }
        );
        await _context.SaveChangesAsync();
        // Detach seeded entities so controller loads fresh from InMemory
        _context.Entry(_context.ProveedorCatalog.Find(1)!).State = EntityState.Detached;
        _context.Entry(_context.ProveedorCatalog.Find(2)!).State = EntityState.Detached;

        var dto = new ActualizarProveedorRequestDto { NombreCanonical = "Supplier B" };

        // Act
        var result = await _controller.UpdateProveedor(1, dto);

        // Assert
        result.Result.Should().BeOfType<ConflictObjectResult>();

        // Verify Supplier A's name was NOT changed
        var savedA = await _context.ProveedorCatalog.FindAsync(1);
        savedA!.NombreCanonical.Should().Be("Supplier A");
    }

    [Fact]
    public async Task UpdateProveedor_SameName_Returns200NoOp()
    {
        // Arrange
        _context.ProveedorCatalog.Add(new ProveedorCatalog
        {
            Id = 1,
            NombreCanonical = "Same Name",
            CreatedAt = new DateTime(2025, 1, 1),
            LastSeenAt = new DateTime(2025, 6, 1)
        });
        await _context.SaveChangesAsync();
        _context.Entry(_context.ProveedorCatalog.Find(1)!).State = EntityState.Detached;

        var dto = new ActualizarProveedorRequestDto { NombreCanonical = "Same Name" };

        // Act
        var result = await _controller.UpdateProveedor(1, dto);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var updated = okResult.Value.Should().BeAssignableTo<ProveedorCatalogDto>().Subject;
        updated.Id.Should().Be(1);
        updated.NombreCanonical.Should().Be("Same Name");

        // Verify no history row was created (no SaveChanges triggered)
        var historyCount = await _context.ProveedorCatalogHistory.CountAsync();
        historyCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateProveedor_NotFound_Returns404()
    {
        // Act
        var result = await _controller.UpdateProveedor(999, new ActualizarProveedorRequestDto { NombreCanonical = "Any" });

        // Assert
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task UpdateProveedor_EmptyName_ReturnsBadRequest()
    {
        // Arrange
        _context.ProveedorCatalog.Add(new ProveedorCatalog { NombreCanonical = "Exists" });
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.UpdateProveedor(1, new ActualizarProveedorRequestDto { NombreCanonical = "" });

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateProveedor_WhitespaceName_ReturnsBadRequest()
    {
        // Arrange
        _context.ProveedorCatalog.Add(new ProveedorCatalog { NombreCanonical = "Exists" });
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.UpdateProveedor(1, new ActualizarProveedorRequestDto { NombreCanonical = "   " });

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ─── DELETE DeleteProveedor (T7-T10) ──────────────────────────────

    [Fact]
    public async Task DeleteProveedor_Existing_ReturnsNoContentAndRemoves()
    {
        // Arrange
        _context.ProveedorCatalog.Add(new ProveedorCatalog { Id = 1, NombreCanonical = "To Delete" });
        await _context.SaveChangesAsync();
        _context.Entry(_context.ProveedorCatalog.Find(1)!).State = EntityState.Detached;

        // Act
        var result = await _controller.DeleteProveedor(1);

        // Assert
        result.Should().BeOfType<NoContentResult>();

        // Verify removed from DB
        var saved = await _context.ProveedorCatalog.FindAsync(1);
        saved.Should().BeNull();
    }

    [Fact]
    public async Task DeleteProveedor_NotFound_Returns404()
    {
        // Act
        var result = await _controller.DeleteProveedor(999);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DeleteProveedor_WithLinkedCompra_CompraSurvives()
    {
        // Arrange
        _context.ProveedorCatalog.Add(new ProveedorCatalog { Id = 1, NombreCanonical = "Linked Supplier" });
        _context.Compras.Add(new Compra
        {
            Id = 100,
            Proveedor = "Linked Supplier",
            ProveedorCatalogId = 1,
            FechaCompra = DateTime.UtcNow,
            MontoTotal = 5000,
            Estado = "PAGADA"
        });
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.DeleteProveedor(1);

        // Assert
        result.Should().BeOfType<NoContentResult>();

        // Compra row survives deletion (FK SetNull in SQL Server; InMemory does not cascade)
        var compra = await _context.Compras.FindAsync(100);
        compra.Should().NotBeNull();
        // NOTE: InMemory does NOT enforce SetNull on delete. The ProveedorCatalogId
        // remains set. In SQL Server, the FK constraint would set it to null.
        // This is a documented InMemory limitation — real SetNull verification
        // requires SQL Server integration tests.
    }

    [Fact]
    public async Task DeleteProveedor_WithLinkedAlias_ControllerDoesNotThrow()
    {
        // Arrange
        _context.ProveedorCatalog.Add(new ProveedorCatalog { Id = 1, NombreCanonical = "With Alias" });
        _context.ProveedorAlias.Add(new ProveedorAlias
        {
            Id = 10,
            RawName = "Alias Name",
            RawNameNormalized = "alias name",
            ProveedorCatalogId = 1,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.DeleteProveedor(1);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        // NOTE: Cascade delete on Alias is enforced by SQL Server FK constraint.
        // InMemory does not cascade automatically. The controller runs without error
        // and the ProveedorCatalog is removed. Cascade verification requires SQL Server.
    }

    // ─── Backfill (T34/T35) ─────────────────────────────────────────

    [Fact]
    public async Task Backfill_AutoLinksHighConfidenceMatch()
    {
        // Arrange
        _context.ProveedorCatalog.Add(new ProveedorCatalog { Id = 10, NombreCanonical = "ALVI" });
        _context.Compras.Add(new Compra
        {
            Id = 100,
            Proveedor = "ALVI S.A.",
            FechaCompra = DateTime.UtcNow,
            MontoTotal = 5000,
            Estado = "PAGADA"
        });
        await _context.SaveChangesAsync();
        // Detach seeded entities so the controller loads fresh from InMemory
        _context.Entry(_context.ProveedorCatalog.Find(10)!).State = EntityState.Detached;

        _mockMatchingService
            .Setup(s => s.MatchAsync("ALVI S.A.", 0.85))
            .ReturnsAsync(new ProveedorMatchResult
            {
                ProveedorCatalog = new ProveedorCatalog { Id = 10, NombreCanonical = "ALVI" },
                Confidence = 0.90,
                SugerirCreacion = false,
                MatchMethod = ProveedorMatchMethod.Tokenized
            });

        // Act
        var result = await _controller.Backfill();

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeAssignableTo<BackfillResultDto>().Subject;
        dto.Procesadas.Should().Be(1);
        dto.AutoVinculadas.Should().Be(1);
        dto.Pendientes.Should().Be(0);

        var saved = await _context.Compras.FindAsync(100);
        saved.Should().NotBeNull();
        saved!.ProveedorCatalogId.Should().Be(10);
    }

    [Fact]
    public async Task Backfill_LeavesPendingBelowThreshold()
    {
        // Arrange
        _context.ProveedorCatalog.Add(new ProveedorCatalog { Id = 20, NombreCanonical = "Distribuidora" });
        _context.Compras.Add(new Compra
        {
            Id = 200,
            Proveedor = "Dist Nte",
            FechaCompra = DateTime.UtcNow,
            MontoTotal = 3000,
            Estado = "PAGADA"
        });
        await _context.SaveChangesAsync();

        _mockMatchingService
            .Setup(s => s.MatchAsync("Dist Nte", 0.85))
            .ReturnsAsync(new ProveedorMatchResult
            {
                ProveedorCatalog = null,
                Confidence = 0.0,
                SugerirCreacion = true,
                MatchMethod = ProveedorMatchMethod.None
            });

        // Act
        var result = await _controller.Backfill();

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeAssignableTo<BackfillResultDto>().Subject;
        dto.Procesadas.Should().Be(1);
        dto.AutoVinculadas.Should().Be(0);
        dto.Pendientes.Should().Be(1);

        var saved = await _context.Compras.FindAsync(200);
        saved!.ProveedorCatalogId.Should().BeNull();
    }

    [Fact]
    public async Task Backfill_SkipsAlreadyLinkedCompras()
    {
        // Arrange — compra already linked, should be excluded from processing
        _context.Compras.Add(new Compra
        {
            Id = 300,
            Proveedor = "Already Linked",
            ProveedorCatalogId = 30,
            FechaCompra = DateTime.UtcNow,
            MontoTotal = 2000,
            Estado = "PAGADA"
        });
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.Backfill();

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeAssignableTo<BackfillResultDto>().Subject;
        dto.Procesadas.Should().Be(0);
        dto.AutoVinculadas.Should().Be(0);
        dto.Pendientes.Should().Be(0);

        // Verify matching service was NEVER called
        _mockMatchingService.Verify(
            s => s.MatchAsync(It.IsAny<string>(), It.IsAny<double>()),
            Times.Never);
    }

    [Fact]
    public async Task Backfill_ReturnsCorrectCounts_MixedScenario()
    {
        // Arrange
        _context.ProveedorCatalog.Add(new ProveedorCatalog { Id = 40, NombreCanonical = "ALVI" });
        _context.Compras.AddRange(
            new Compra { Id = 400, Proveedor = "ALVI S.A.", FechaCompra = DateTime.UtcNow, MontoTotal = 1000, Estado = "PAGADA" },
            new Compra { Id = 401, Proveedor = "Unknown Shop", FechaCompra = DateTime.UtcNow, MontoTotal = 2000, Estado = "PAGADA" },
            new Compra { Id = 402, Proveedor = "Already Done", ProveedorCatalogId = 99, FechaCompra = DateTime.UtcNow, MontoTotal = 3000, Estado = "PAGADA" }
        );
        await _context.SaveChangesAsync();

        _mockMatchingService
            .Setup(s => s.MatchAsync("ALVI S.A.", 0.85))
            .ReturnsAsync(new ProveedorMatchResult
            {
                ProveedorCatalog = new ProveedorCatalog { Id = 40, NombreCanonical = "ALVI" },
                Confidence = 0.90,
                SugerirCreacion = false,
                MatchMethod = ProveedorMatchMethod.Tokenized
            });

        _mockMatchingService
            .Setup(s => s.MatchAsync("Unknown Shop", 0.85))
            .ReturnsAsync(new ProveedorMatchResult
            {
                ProveedorCatalog = null,
                Confidence = 0.0,
                SugerirCreacion = true,
                MatchMethod = ProveedorMatchMethod.None
            });

        // Act
        var result = await _controller.Backfill();

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeAssignableTo<BackfillResultDto>().Subject;
        dto.Procesadas.Should().Be(2);  // 2 null-linked compras
        dto.AutoVinculadas.Should().Be(1);  // ALVI S.A. matched
        dto.Pendientes.Should().Be(1);  // Unknown Shop did not match
    }

    [Fact]
    public async Task Backfill_IsIdempotent()
    {
        // Arrange
        _context.ProveedorCatalog.Add(new ProveedorCatalog { Id = 50, NombreCanonical = "El Molino" });
        _context.Compras.Add(new Compra
        {
            Id = 500,
            Proveedor = "El Molino S.A.",
            FechaCompra = DateTime.UtcNow,
            MontoTotal = 4000,
            Estado = "PAGADA"
        });
        await _context.SaveChangesAsync();

        _mockMatchingService
            .Setup(s => s.MatchAsync("El Molino S.A.", 0.85))
            .ReturnsAsync(new ProveedorMatchResult
            {
                ProveedorCatalog = new ProveedorCatalog { Id = 50, NombreCanonical = "El Molino" },
                Confidence = 0.92,
                SugerirCreacion = false,
                MatchMethod = ProveedorMatchMethod.Tokenized
            });

        // Act — first run
        var result1 = await _controller.Backfill();
        var ok1 = result1.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto1 = ok1.Value.Should().BeAssignableTo<BackfillResultDto>().Subject;
        dto1.Procesadas.Should().Be(1);
        dto1.AutoVinculadas.Should().Be(1);

        // Act — second run (compra already linked, should be skipped)
        var result2 = await _controller.Backfill();
        var ok2 = result2.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto2 = ok2.Value.Should().BeAssignableTo<BackfillResultDto>().Subject;
        dto2.Procesadas.Should().Be(0);  // Already linked — not processed
        dto2.AutoVinculadas.Should().Be(0);
        dto2.Pendientes.Should().Be(0);

        // Verify ProveedorCatalogId still correct (not double-linked)
        var saved = await _context.Compras.FindAsync(500);
        saved!.ProveedorCatalogId.Should().Be(50);
    }
}
