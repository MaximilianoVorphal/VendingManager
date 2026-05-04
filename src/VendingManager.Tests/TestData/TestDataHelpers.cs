namespace VendingManager.Tests.TestData;

public static class TestDataHelpers
{
    private static readonly DateTime _globalStart = new(2025, 12, 18);

    public static Venta CreateVenta(
        DateTime? fechaLocal = null,
        decimal precioVenta = 1000m,
        decimal costoVenta = 400m,
        bool pagado = true,
        int maquinaId = 1,
        int? productoId = 1,
        string tipoOperacion = "Ingreso")
        => new Venta
        {
            FechaHora = fechaLocal ?? _globalStart.AddDays(1),
            FechaLocal = fechaLocal ?? _globalStart.AddDays(1),
            MaquinaId = maquinaId,
            Maquina = CreateMaquina(maquinaId),
            ProductoId = productoId,
            Producto = productoId.HasValue ? CreateProducto(productoId.Value) : null,
            NumeroSlot = "1",
            PrecioVenta = precioVenta,
            CostoVenta = costoVenta,
            Pagado = pagado,
            IdOrdenMaquina = "TEST-001",
            TipoOperacion = tipoOperacion
        };

    public static MovimientoCaja CreateMovimientoCaja(
        decimal monto,
        DateTime? fecha = null,
        string categoria = "GENERAL",
        string tipo = "GASTO",
        string descripcion = "Test")
        => new MovimientoCaja
        {
            Fecha = fecha ?? _globalStart.AddDays(1),
            Descripcion = descripcion,
            Monto = monto,
            Categoria = categoria,
            Tipo = tipo
        };

    public static Producto CreateProducto(
        int id = 1,
        string nombre = "Test Product",
        decimal costoPromedio = 400m,
        int stockBodega = 10)
        => new Producto
        {
            Id = id,
            Nombre = nombre,
            CostoPromedio = costoPromedio,
            StockBodega = stockBodega,
            SKU = $"TEST-{id:D3}"
        };

    public static Maquina CreateMaquina(
        int id = 1,
        string nombre = "Test Machine",
        string ubicacion = "Test Location")
        => new Maquina
        {
            Id = id,
            Nombre = nombre,
            Ubicacion = ubicacion
        };

    public static ConfiguracionSlot CreateSlot(
        int id = 1,
        int maquinaId = 1,
        int productoId = 1,
        int stockActual = 5,
        int capacidadMaxima = 10,
        decimal precioVenta = 1000m,
        int stockMinimo = 2)
        => new ConfiguracionSlot
        {
            Id = id,
            MaquinaId = maquinaId,
            Maquina = CreateMaquina(maquinaId),
            ProductoId = productoId,
            Producto = CreateProducto(productoId),
            NumeroSlot = $"SLOT-{id}",
            StockActual = stockActual,
            CapacidadMaxima = capacidadMaxima,
            StockMinimo = stockMinimo,
            PrecioVenta = precioVenta
        };

    public static ApplicationDbContext CreateInMemoryContext(
        string databaseName = "TestDb")
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new ApplicationDbContext(options);
    }
}