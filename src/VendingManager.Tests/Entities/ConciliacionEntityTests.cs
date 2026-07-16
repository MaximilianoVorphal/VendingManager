namespace VendingManager.Tests.Entities;

/// <summary>
/// Domain model tests for TASK-01 (Transferencia extensions),
/// TASK-02 (Compra extensions), and TASK-03 (Devolucion entity).
/// Written RED first, then implementation turns them GREEN.
/// </summary>
public class ConciliacionEntityTests
{
    // ── TASK-01: Transferencia ────────────────────────────────────────────────

    [Fact]
    public void Transferencia_NewInstance_HasNullComprobanteImagen()
    {
        var t = new Transferencia();
        t.ComprobanteImagen.Should().BeNull();
        t.ComprobanteImagenContentType.Should().BeNull();
        t.ComprobanteImagenFileName.Should().BeNull();
    }

    [Fact]
    public void Transferencia_NewInstance_HasVerificadaFalse()
    {
        var t = new Transferencia();
        t.Verificada.Should().BeFalse();
    }

    [Fact]
    public void Transferencia_ComprobanteImagen_CanStoreBinaryData()
    {
        var t = new Transferencia
        {
            ComprobanteImagen = new byte[] { 0xFF, 0xD8, 0xFF },
            ComprobanteImagenContentType = "image/jpeg",
            ComprobanteImagenFileName = "comprobante.jpg"
        };
        t.ComprobanteImagen.Should().BeEquivalentTo(new byte[] { 0xFF, 0xD8, 0xFF });
        t.ComprobanteImagenContentType.Should().Be("image/jpeg");
        t.ComprobanteImagenFileName.Should().Be("comprobante.jpg");
    }

    [Fact]
    public void Transferencia_Verificada_CanBeSetToTrue()
    {
        var t = new Transferencia { Verificada = true };
        t.Verificada.Should().BeTrue();
    }

    // ── TASK-02: Compra ───────────────────────────────────────────────────────

    [Fact]
    public void Compra_NewInstance_HasVerificadaFalse()
    {
        var c = new Compra();
        c.Verificada.Should().BeFalse();
    }

    [Fact]
    public void Compra_Verificada_CanBeSetToTrue()
    {
        var c = new Compra { Verificada = true };
        c.Verificada.Should().BeTrue();
    }

    // ── TASK-03: Devolucion ───────────────────────────────────────────────────

    [Fact]
    public void Devolucion_NewInstance_HasExpectedDefaults()
    {
        var d = new Devolucion();

        d.Monto.Should().Be(0m);
        d.Trabajador.Should().Be(string.Empty);
        d.RendicionId.Should().BeNull();
        d.PeriodoId.Should().BeNull();
        d.MovimientoCajaId.Should().BeNull();
        d.Observaciones.Should().BeNull();
    }

    [Fact]
    public void Devolucion_CanAssignRequiredFields()
    {
        var fecha = new DateTime(2026, 6, 20);
        var d = new Devolucion
        {
            Monto = 500m,
            Fecha = fecha,
            Trabajador = "Juan Pérez",
            PeriodoId = 3
        };

        d.Monto.Should().Be(500m);
        d.Fecha.Should().Be(fecha);
        d.Trabajador.Should().Be("Juan Pérez");
        d.PeriodoId.Should().Be(3);
    }

    [Fact]
    public void Devolucion_NavigationProperties_AreLazyNullByDefault()
    {
        var d = new Devolucion();

        d.Rendicion.Should().BeNull();
        d.AccountingPeriod.Should().BeNull();
        d.MovimientoCaja.Should().BeNull();
    }
}
