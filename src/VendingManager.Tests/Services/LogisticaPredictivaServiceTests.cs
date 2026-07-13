namespace VendingManager.Tests.Services;

using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using VendingManager.Core.Entities;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.DTOs;
using VendingManager.Tests.TestData;

/// <summary>
/// LogisticaPredictivaService: velocidad de venta por slot, proyección de quiebre,
/// lucro cesante proyectado (LCP), agrupación por zona logística y generación de
/// órdenes de carga de rescate.
/// </summary>
public class LogisticaPredictivaServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly LogisticaPredictivaService _service;

    public LogisticaPredictivaServiceTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext(
            $"LogisticaPredictiva_{Guid.NewGuid()}");
        _service = new LogisticaPredictivaService(_context, new OrdenCargaService(_context));
    }

    public void Dispose() => _context.Dispose();

    // ------------------------------------------------------------------ helpers

    private ZonaLogistica AddZona(string nombre, decimal costoBaseViaje)
    {
        var zona = new ZonaLogistica { Nombre = nombre, CostoBaseViaje = costoBaseViaje };
        _context.ZonasLogisticas.Add(zona);
        return zona;
    }

    private Maquina AddMaquina(string nombre, ZonaLogistica? zona = null)
    {
        var maquina = new Maquina { Nombre = nombre, Ubicacion = "Test", Zona = zona };
        _context.Maquinas.Add(maquina);
        return maquina;
    }

    private Producto AddProducto(string nombre, decimal costoPromedio, int stockBodega = 500)
    {
        var producto = new Producto
        {
            Nombre = nombre,
            SKU = nombre.ToUpperInvariant(),
            CostoPromedio = costoPromedio,
            StockBodega = stockBodega
        };
        _context.Productos.Add(producto);
        return producto;
    }

    private ConfiguracionSlot AddSlot(
        Maquina maquina, Producto producto, string numeroSlot,
        int stockActual, int capacidad, decimal precioVenta)
    {
        var slot = new ConfiguracionSlot
        {
            Maquina = maquina,
            Producto = producto,
            NumeroSlot = numeroSlot,
            StockActual = stockActual,
            CapacidadMaxima = capacidad,
            PrecioVenta = precioVenta
        };
        _context.ConfiguracionSlots.Add(slot);
        return slot;
    }

    /// <summary>Agrega N ventas (1 fila = 1 unidad) dentro de la ventana de historial.</summary>
    private void AddVentas(Maquina maquina, Producto producto, int unidades, double diasAtras = 1)
    {
        for (int i = 0; i < unidades; i++)
        {
            var fecha = DateTime.Now.AddDays(-diasAtras).AddMinutes(i);
            _context.Ventas.Add(new Venta
            {
                Maquina = maquina,
                Producto = producto,
                FechaHora = fecha,
                FechaLocal = fecha,
                PrecioVenta = 0
            });
        }
    }

    private static LogisticaSlotDto SoloSlot(List<LogisticaZonaDto> zonas) =>
        zonas.Single().Maquinas.Single().Slots.Single();

    // ------------------------------------------------------------------ velocity

    [Fact]
    public async Task Velocidad_SeCalculaConVentasRecientes()
    {
        var zona = AddZona("Norte", 5000m);
        var maquina = AddMaquina("M1", zona);
        var producto = AddProducto("Papas", 300m);
        AddSlot(maquina, producto, "10", stockActual: 5, capacidad: 10, precioVenta: 800m);
        AddVentas(maquina, producto, 28); // 28 unidades / 14 días = 2/día
        // Ventas fuera de la ventana no deben contar
        AddVentas(maquina, producto, 10, diasAtras: 30);
        await _context.SaveChangesAsync();

        var zonas = await _service.GetAnalisisZonasAsync(diasHistorial: 14, ventanaProyeccionDias: 3);

        var slot = SoloSlot(zonas);
        slot.VelocidadDiaria.Should().Be(2m);
        slot.DiasHastaQuiebre.Should().BeApproximately(2.5, 0.001); // 5 / 2
        slot.EsCritico.Should().BeFalse(); // 2.5 días >= 48h
    }

    [Fact]
    public async Task Velocidad_SeRepartePorIgualEntreSlotsDelMismoProducto()
    {
        var zona = AddZona("Norte", 5000m);
        var maquina = AddMaquina("M1", zona);
        var producto = AddProducto("Bebida", 300m);
        AddSlot(maquina, producto, "10", stockActual: 5, capacidad: 10, precioVenta: 800m);
        AddSlot(maquina, producto, "11", stockActual: 5, capacidad: 10, precioVenta: 800m);
        AddVentas(maquina, producto, 28); // 2/día máquina-producto → 1/día por slot
        await _context.SaveChangesAsync();

        var zonas = await _service.GetAnalisisZonasAsync(diasHistorial: 14, ventanaProyeccionDias: 3);

        var slots = zonas.Single().Maquinas.Single().Slots;
        slots.Should().HaveCount(2);
        slots.Should().OnlyContain(s => s.VelocidadDiaria == 1m);
    }

    // ------------------------------------------------------------------ zero history

    [Fact]
    public async Task SinHistorial_VelocidadCeroDiasNullLcpCero_SinExcepcion()
    {
        var zona = AddZona("Norte", 5000m);
        var maquina = AddMaquina("M1", zona);
        var producto = AddProducto("Galletas", 300m);
        AddSlot(maquina, producto, "10", stockActual: 5, capacidad: 10, precioVenta: 800m);
        await _context.SaveChangesAsync();

        var zonas = await _service.GetAnalisisZonasAsync();

        var slot = SoloSlot(zonas);
        slot.VelocidadDiaria.Should().Be(0m);
        slot.DiasHastaQuiebre.Should().BeNull();
        slot.LcpSlot.Should().Be(0m);
        slot.EsCritico.Should().BeFalse();
        double.IsNaN(slot.DiasHastaQuiebre ?? 0).Should().BeFalse();
        double.IsInfinity(slot.DiasHastaQuiebre ?? 0).Should().BeFalse();
    }

    // ------------------------------------------------------------------ stock cero

    [Fact]
    public async Task StockCero_DiasHastaQuiebreEsCeroYCritico()
    {
        var zona = AddZona("Norte", 100m);
        var maquina = AddMaquina("M1", zona);
        var producto = AddProducto("Jugo", 300m);
        AddSlot(maquina, producto, "10", stockActual: 0, capacidad: 10, precioVenta: 800m);
        AddVentas(maquina, producto, 28); // v = 2/día
        await _context.SaveChangesAsync();

        var zonas = await _service.GetAnalisisZonasAsync(diasHistorial: 14, ventanaProyeccionDias: 3);

        var slot = SoloSlot(zonas);
        slot.DiasHastaQuiebre.Should().Be(0);
        slot.EsCritico.Should().BeTrue();
        // Ventana completa vacía: margen 500 x 2/día x 3 días
        slot.LcpSlot.Should().Be(3000m);
    }

    // ------------------------------------------------------------------ LCP clamping

    [Fact]
    public async Task Lcp_QuiebreFueraDeLaVentana_EsCero()
    {
        var zona = AddZona("Norte", 100m);
        var maquina = AddMaquina("M1", zona);
        var producto = AddProducto("Agua", 300m);
        // v = 1/día, stock 10 → quiebre en 10 días, fuera de la ventana de 3
        AddSlot(maquina, producto, "10", stockActual: 10, capacidad: 12, precioVenta: 800m);
        AddVentas(maquina, producto, 14);
        await _context.SaveChangesAsync();

        var zonas = await _service.GetAnalisisZonasAsync(diasHistorial: 14, ventanaProyeccionDias: 3);

        var slot = SoloSlot(zonas);
        slot.DiasHastaQuiebre.Should().BeApproximately(10, 0.001);
        slot.LcpSlot.Should().Be(0m);
        slot.EsCritico.Should().BeFalse();
    }

    [Fact]
    public async Task Lcp_QuiebreDentroDeLaVentana_SoloCuentaDiasVacios()
    {
        var zona = AddZona("Norte", 100m);
        var maquina = AddMaquina("M1", zona);
        var producto = AddProducto("Café", 300m);
        // v = 1/día, stock 1 → quiebre en 1 día → 2 días vacíos de la ventana de 3
        AddSlot(maquina, producto, "10", stockActual: 1, capacidad: 10, precioVenta: 800m);
        AddVentas(maquina, producto, 14);
        await _context.SaveChangesAsync();

        var zonas = await _service.GetAnalisisZonasAsync(diasHistorial: 14, ventanaProyeccionDias: 3);

        var slot = SoloSlot(zonas);
        slot.DiasHastaQuiebre.Should().BeApproximately(1, 0.001);
        slot.EsCritico.Should().BeTrue();
        // margen 500 x 1/día x 2 días vacíos
        slot.LcpSlot.Should().Be(1000m);
    }

    // ------------------------------------------------------------------ negative margin

    [Fact]
    public async Task MargenNegativo_SeClampeaACero()
    {
        var zona = AddZona("Norte", 100m);
        var maquina = AddMaquina("M1", zona);
        var producto = AddProducto("Premium", costoPromedio: 1200m);
        // Precio de venta menor al costo → margen negativo → 0 para LCP
        AddSlot(maquina, producto, "10", stockActual: 0, capacidad: 10, precioVenta: 800m);
        AddVentas(maquina, producto, 28);
        await _context.SaveChangesAsync();

        var zonas = await _service.GetAnalisisZonasAsync(diasHistorial: 14, ventanaProyeccionDias: 3);

        var slot = SoloSlot(zonas);
        slot.MargenUnitario.Should().Be(0m);
        slot.LcpSlot.Should().Be(0m);
    }

    // ------------------------------------------------------------------ zone grouping

    [Fact]
    public async Task Agrupacion_MaquinasSinZonaVanAlBucketSinZona()
    {
        var zona = AddZona("Norte", 5000m);
        var conZona = AddMaquina("M1", zona);
        var sinZona = AddMaquina("M2"); // sin zona
        var producto = AddProducto("Snack", 300m);
        AddSlot(conZona, producto, "10", stockActual: 5, capacidad: 10, precioVenta: 800m);
        AddSlot(sinZona, producto, "10", stockActual: 5, capacidad: 10, precioVenta: 800m);
        await _context.SaveChangesAsync();

        var zonas = await _service.GetAnalisisZonasAsync();

        zonas.Should().HaveCount(2);
        var bucketSinZona = zonas.Single(z => z.ZonaLogisticaId == null);
        bucketSinZona.ZonaNombre.Should().Be("Sin zona");
        bucketSinZona.CostoBaseViaje.Should().Be(0m);
        bucketSinZona.EsRentableViajar.Should().BeFalse();
        bucketSinZona.Maquinas.Single().MaquinaNombre.Should().Be("M2");

        zonas.Single(z => z.ZonaLogisticaId == zona.Id)
            .Maquinas.Single().MaquinaNombre.Should().Be("M1");
    }

    // ------------------------------------------------------------------ profitability threshold

    [Fact]
    public async Task EsRentableViajar_SoloCuandoLcpSuperaElCostoBase()
    {
        // Zona barata: LCP 3000 > costo 100 → rentable
        var zonaBarata = AddZona("Barata", 100m);
        var m1 = AddMaquina("M1", zonaBarata);
        var p1 = AddProducto("P1", 300m);
        AddSlot(m1, p1, "10", stockActual: 0, capacidad: 10, precioVenta: 800m); // LCP 3000
        AddVentas(m1, p1, 28);

        // Zona cara: LCP 3000 <= costo 999999 → no rentable
        var zonaCara = AddZona("Cara", 999_999m);
        var m2 = AddMaquina("M2", zonaCara);
        var p2 = AddProducto("P2", 300m);
        AddSlot(m2, p2, "10", stockActual: 0, capacidad: 10, precioVenta: 800m);
        AddVentas(m2, p2, 28);
        await _context.SaveChangesAsync();

        var zonas = await _service.GetAnalisisZonasAsync(diasHistorial: 14, ventanaProyeccionDias: 3);

        zonas.Single(z => z.ZonaNombre == "Barata").EsRentableViajar.Should().BeTrue();
        zonas.Single(z => z.ZonaNombre == "Cara").EsRentableViajar.Should().BeFalse();

        // Orden: delta (LcpTotal - CostoBaseViaje) descendente
        zonas.First().ZonaNombre.Should().Be("Barata");
    }

    [Fact]
    public async Task EsRentableViajar_ZonaRealConCostoBaseCero_EsTrueSiHayLcp()
    {
        // Zona real con costo base 0: sigue el contrato general (LcpTotal > CostoBaseViaje),
        // a diferencia del bucket "Sin zona" que queda forzado a false.
        var zona = AddZona("Gratis", 0m);
        var maquina = AddMaquina("M1", zona);
        var producto = AddProducto("P1", 300m);
        AddSlot(maquina, producto, "10", stockActual: 0, capacidad: 10, precioVenta: 800m); // LCP 3000
        AddVentas(maquina, producto, 28);
        await _context.SaveChangesAsync();

        var zonas = await _service.GetAnalisisZonasAsync(diasHistorial: 14, ventanaProyeccionDias: 3);

        var dto = zonas.Single();
        dto.LcpTotal.Should().BeGreaterThan(0m);
        dto.EsRentableViajar.Should().BeTrue();
    }

    // ------------------------------------------------------------------ threshold boundaries

    [Fact]
    public async Task QuiebreExactamenteEn48Horas_NoEsCritico()
    {
        var zona = AddZona("Norte", 100m);
        var maquina = AddMaquina("M1", zona);
        var producto = AddProducto("Limite", 300m);
        // v = 1/día (14 ventas / 14 días), stock 2 → t = 2.0 días exactos.
        // El comparador es estricto (t < UmbralCriticoDias): en el borde NO es crítico.
        AddSlot(maquina, producto, "10", stockActual: 2, capacidad: 10, precioVenta: 800m);
        AddVentas(maquina, producto, 14);
        await _context.SaveChangesAsync();

        var zonas = await _service.GetAnalisisZonasAsync(diasHistorial: 14, ventanaProyeccionDias: 3);

        var slot = SoloSlot(zonas);
        slot.DiasHastaQuiebre.Should().Be(2.0);
        slot.EsCritico.Should().BeFalse();
    }

    [Fact]
    public async Task Lcp_QuiebreExactamenteAlFinalDeLaVentana_EsCero()
    {
        var zona = AddZona("Norte", 100m);
        var maquina = AddMaquina("M1", zona);
        var producto = AddProducto("Justo", 300m);
        // v = 1/día, stock 3 → t = 3.0 = ventana → 0 días vacíos → LCP 0
        AddSlot(maquina, producto, "10", stockActual: 3, capacidad: 10, precioVenta: 800m);
        AddVentas(maquina, producto, 14);
        await _context.SaveChangesAsync();

        var zonas = await _service.GetAnalisisZonasAsync(diasHistorial: 14, ventanaProyeccionDias: 3);

        var slot = SoloSlot(zonas);
        slot.DiasHastaQuiebre.Should().Be(3.0);
        slot.LcpSlot.Should().Be(0m);
    }

    // ------------------------------------------------------------------ draft order

    [Fact]
    public async Task GenerarOrden_CreaOrdenPendienteConSlotsCriticos()
    {
        var zona = AddZona("Norte", 100m);
        var maquina = AddMaquina("M1", zona);
        var critico = AddProducto("Critico", 300m, stockBodega: 500);
        var tranquilo = AddProducto("Tranquilo", 300m, stockBodega: 500);
        // Crítico: v = 2/día, stock 1 → quiebre en 0.5 días → faltan 9
        AddSlot(maquina, critico, "10", stockActual: 1, capacidad: 10, precioVenta: 800m);
        AddVentas(maquina, critico, 28);
        // No crítico: v ≈ 0.14/día, stock 8 → quiebre en ~56 días
        AddSlot(maquina, tranquilo, "11", stockActual: 8, capacidad: 10, precioVenta: 800m);
        AddVentas(maquina, tranquilo, 2);
        await _context.SaveChangesAsync();

        var ordenId = await _service.GenerarOrdenCargaBorradorAsync(zona.Id);

        var orden = await _context.OrdenesCarga
            .Include(o => o.Detalles)
            .SingleAsync(o => o.Id == ordenId);

        orden.Estado.Should().Be("PENDIENTE");
        orden.MaquinaId.Should().BeNull("la orden de zona es consolidada; cada detalle lleva su máquina");
        orden.Nombre.Should().StartWith("Rescate Norte");

        var detalle = orden.Detalles.Single();
        detalle.ProductoId.Should().Be(critico.Id);
        detalle.CantidadSolicitada.Should().Be(9); // CapacidadMaxima - StockActual
        detalle.MaquinaId.Should().Be(maquina.Id);
    }

    [Fact]
    public async Task GenerarOrden_SinSlotsCriticos_Lanza()
    {
        var zona = AddZona("Norte", 100m);
        var maquina = AddMaquina("M1", zona);
        var producto = AddProducto("Lento", 300m);
        AddSlot(maquina, producto, "10", stockActual: 8, capacidad: 10, precioVenta: 800m);
        AddVentas(maquina, producto, 2); // v muy baja, sin quiebre próximo
        await _context.SaveChangesAsync();

        var act = () => _service.GenerarOrdenCargaBorradorAsync(zona.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GenerarOrden_BucketSinZona_UsaZonaNull()
    {
        var maquina = AddMaquina("M-Suelta"); // sin zona
        var producto = AddProducto("Urgente", 300m, stockBodega: 500);
        AddSlot(maquina, producto, "10", stockActual: 0, capacidad: 10, precioVenta: 800m);
        AddVentas(maquina, producto, 28);
        await _context.SaveChangesAsync();

        var ordenId = await _service.GenerarOrdenCargaBorradorAsync(null);

        var orden = await _context.OrdenesCarga
            .Include(o => o.Detalles)
            .SingleAsync(o => o.Id == ordenId);
        orden.Estado.Should().Be("PENDIENTE");
        orden.Detalles.Single().CantidadSolicitada.Should().Be(10);
    }
}
