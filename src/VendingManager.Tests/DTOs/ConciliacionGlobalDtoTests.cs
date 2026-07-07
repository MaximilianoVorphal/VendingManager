namespace VendingManager.Tests.DTOs;

using VendingManager.Shared.DTOs;

/// <summary>
/// Structural tests for ConciliacionGlobalDto and nested DTOs.
/// Purely structural — no logic to triangulate.
/// </summary>
public class ConciliacionGlobalDtoTests
{
    [Fact]
    public void ConciliacionGlobalDto_HasExpectedStructure()
    {
        // Act
        var dto = new ConciliacionGlobalDto();

        // Assert — all properties exist with correct types and defaults
        dto.Semanas.Should().BeEmpty();
        dto.Proveedores.Should().BeEmpty();
        dto.Resumen.Should().NotBeNull();
        dto.SaldoInicial.Should().Be(0m);
    }

    [Fact]
    public void SemanaColumnaDto_HasExpectedProperties()
    {
        // Act
        var dto = new SemanaColumnaDto
        {
            Id = 1,
            Numero = 3,
            FechaInicio = new DateTime(2026, 7, 6),
            FechaFin = new DateTime(2026, 7, 12),
            EstaCerrada = true,
            TotalTransferido = 10000m,
            TotalCompras = 5000m,
            TotalGastos = 2000m
        };

        // Assert
        dto.Id.Should().Be(1);
        dto.Numero.Should().Be(3);
        dto.FechaInicio.Should().Be(new DateTime(2026, 7, 6));
        dto.FechaFin.Should().Be(new DateTime(2026, 7, 12));
        dto.EstaCerrada.Should().BeTrue();
        dto.TotalTransferido.Should().Be(10000m);
        dto.TotalCompras.Should().Be(5000m);
        dto.TotalGastos.Should().Be(2000m);
    }

    [Fact]
    public void FilaProveedorDto_HasExpectedProperties()
    {
        // Act
        var dto = new FilaProveedorDto
        {
            ProveedorSlug = "juanperez",
            ProveedorNombre = "Juan Pérez",
            TotalProveedor = 15000m
        };
        dto.Celdas.Add(new CeldaSemanaDto
        {
            SemanaId = 1,
            Monto = 5000m,
            Estado = "Pendiente"
        });

        // Assert
        dto.ProveedorSlug.Should().Be("juanperez");
        dto.ProveedorNombre.Should().Be("Juan Pérez");
        dto.TotalProveedor.Should().Be(15000m);
        dto.Celdas.Should().HaveCount(1);
        dto.Celdas[0].SemanaId.Should().Be(1);
        dto.Celdas[0].Monto.Should().Be(5000m);
        dto.Celdas[0].Estado.Should().Be("Pendiente");
    }

    [Fact]
    public void CeldaSemanaDto_DefaultEstado_IsVacio()
    {
        // Act
        var dto = new CeldaSemanaDto();

        // Assert
        dto.Estado.Should().Be("Vacio");
        dto.Comprobantes.Should().BeEmpty();
    }

    [Fact]
    public void ComprobanteItemDto_HasExpectedProperties()
    {
        // Act
        var dto = new ComprobanteItemDto
        {
            Id = 42,
            Tipo = "Compra",
            NumeroDocumento = "FAC-001",
            Fecha = new DateTime(2026, 7, 7),
            Monto = 5000m,
            Verificada = true,
            Proveedor = "Juan Pérez"
        };

        // Assert
        dto.Id.Should().Be(42);
        dto.Tipo.Should().Be("Compra");
        dto.NumeroDocumento.Should().Be("FAC-001");
        dto.Fecha.Should().Be(new DateTime(2026, 7, 7));
        dto.Monto.Should().Be(5000m);
        dto.Verificada.Should().BeTrue();
        dto.Proveedor.Should().Be("Juan Pérez");
    }

    [Fact]
    public void ResumenConciliacionDto_HasExpectedProperties()
    {
        // Act
        var dto = new ResumenConciliacionDto
        {
            TotalTransferencias = 50000m,
            TotalCompras = 30000m,
            TotalGastos = 10000m,
            SaldoTotal = 10000m,
            SemanasTotales = 3,
            SemanasVerificadas = 1
        };

        // Assert
        dto.TotalTransferencias.Should().Be(50000m);
        dto.TotalCompras.Should().Be(30000m);
        dto.TotalGastos.Should().Be(10000m);
        dto.SaldoTotal.Should().Be(10000m);
        dto.SemanasTotales.Should().Be(3);
        dto.SemanasVerificadas.Should().Be(1);
    }
}
