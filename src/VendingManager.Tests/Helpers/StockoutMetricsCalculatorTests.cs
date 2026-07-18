namespace VendingManager.Tests.Helpers;

using FluentAssertions;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Helpers;

public class StockoutMetricsCalculatorTests
{
    [Fact]
    public void CalendarDays_ConvertsWallClockHoursWithoutRounding()
    {
        StockoutMetricsCalculator.CalendarDays(180).Should().Be(7.5);
        StockoutMetricsCalculator.CalendarDays(10).Should().BeApproximately(10d / 24d, 0.001);
    }

    [Fact]
    public void CalculateOperatingWindowLoss_ExcludesClosedHours()
    {
        var loss = StockoutMetricsCalculator.CalculateOperatingWindowLoss(
            velocidadPorHora: 2m,
            fechaAgotamiento: new DateTime(2026, 7, 10, 2, 24, 0),
            finReporte: new DateTime(2026, 7, 17, 14, 24, 0),
            precioPromedioVenta: 10m,
            gananciaPromedio: 4m);

        loss.HorasOperativas.Should().BeApproximately(104.4, 0.001);
        loss.DineroPerdidoEstimado.Should().Be(2088m);
        loss.GananciaPerdidaEstimada.Should().Be(835.2m);
    }

    [Fact]
    public void CalculateOperatingWindowLoss_ExposesUnmetUnitsSeparatelyFromClp()
    {
        var loss = StockoutMetricsCalculator.CalculateOperatingWindowLoss(
            2m, new DateTime(2026, 7, 10, 8, 0, 0), new DateTime(2026, 7, 10, 18, 0, 0), 1000m, 400m);

        typeof(StockoutLossMetrics).GetProperty("UnidadesNoAtendidasEstimadas")!.GetValue(loss).Should().Be(20m);
        loss.DineroPerdidoEstimado.Should().Be(20_000m);
        loss.GananciaPerdidaEstimada.Should().Be(8_000m);
    }

    [Fact]
    public void CalculateSlotVelocity_UsesPreDepletionOperatingExposure()
    {
        var start = new DateTime(2026, 7, 1, 8, 0, 0);
        var depletion = new DateTime(2026, 7, 2, 22, 0, 0);
        var sales = Enumerable.Range(0, 10).Select(i => start.AddHours(i + 1));

        var metrics = StockoutMetricsCalculator.CalculateSlotVelocity(sales, start, depletion, depletion);

        metrics.VentasOperativasObservadas.Should().Be(10);
        metrics.HorasExposicionOperativas.Should().Be(28);
        metrics.VelocidadPorHora.Should().BeApproximately(10m / 28m, 0.0001m);
    }

    [Fact]
    public void SelectEffectiveVelocity_UsesUsablePoolAndFallsBackWithoutDuplicatingEvidence()
    {
        var observed = new StockoutVelocityMetrics(10, 28, 10m / 28m);
        var pool = StockoutMetricsCalculator.CreateProductMachinePool(7, 9,
            new DateTime(2026, 7, 1, 8, 0, 0), new DateTime(2026, 7, 3, 8, 0, 0),
            Enumerable.Range(0, 20).Select(i => new DateTime(2026, 7, 1, 9, 0, 0).AddHours(i)));

        var selected = StockoutMetricsCalculator.SelectEffectiveVelocity(9, false, observed, pool);
        var sparse = StockoutMetricsCalculator.SelectEffectiveVelocity(9, false, observed,
            StockoutMetricsCalculator.CreateProductMachinePool(7, 9, new DateTime(2026, 7, 1, 8, 0, 0), new DateTime(2026, 7, 1, 12, 0, 0), []));
        var dead = StockoutMetricsCalculator.SelectEffectiveVelocity(9, true, observed, pool);

        selected.OrigenVelocidad.Should().Be(OrigenVelocidad.ProductoMaquina);
        selected.VelocidadEfectivaPorHora.Should().Be(pool.VelocidadPorHora);
        StockoutMetricsCalculator.CalculateOperatingWindowLoss(selected.VelocidadEfectivaPorHora,
            new DateTime(2026, 7, 2, 8, 0, 0), new DateTime(2026, 7, 2, 10, 0, 0), 100m, 40m)
            .DineroPerdidoEstimado.Should().Be(pool.VelocidadPorHora * 2m * 100m, "per-slot loss uses Veff");
        pool.Id.Should().Be("7:9:2026-07-01T08:00:00.0000000:2026-07-03T08:00:00.0000000");
        sparse.VelocidadEfectivaPorHora.Should().Be(observed.VelocidadPorHora);
        dead.VelocidadEfectivaPorHora.Should().Be(0m);
    }

    [Fact]
    public void CalculateConservativeLoss_TruncatesAtFirstPostDepletionSale()
    {
        var depletion = new DateTime(2026, 7, 3, 10, 0, 0);
        var firstPostSale = new DateTime(2026, 7, 3, 12, 0, 0);
        var loss = StockoutMetricsCalculator.CalculateConservativeLoss(2m, depletion, firstPostSale, depletion.AddDays(2), 100m, 40m);

        loss.HorasOperativas.Should().Be(2);
        loss.DineroPerdidoEstimado.Should().Be(400m);
        loss.GananciaPerdidaEstimada.Should().Be(160m);
    }

    [Fact]
    public void CalculateConservativeLoss_FallbackVelocityConvergesToOperatingWindowLoss()
    {
        var depletion = new DateTime(2026, 7, 10, 2, 24, 0);
        var reportEnd = new DateTime(2026, 7, 17, 14, 24, 0);
        var fallback = StockoutMetricsCalculator.CalculateConservativeLoss(2m, depletion, null, reportEnd, 1000m, 400m);

        fallback.DineroPerdidoEstimado.Should().BeApproximately(208_800m, 1m);
        fallback.GananciaPerdidaEstimada.Should().BeApproximately(83_520m, 1m);
    }

    [Theory]
    [InlineData(0, 10d, false, true, EstimateConfidence.NotEstimable)]
    [InlineData(10, 0.5d, false, false, EstimateConfidence.Low)]
    [InlineData(10, 14d, true, false, EstimateConfidence.Low)]
    [InlineData(10, 14d, false, false, EstimateConfidence.Medium)]
    public void CalculateQuality_ReportsMissingSparsePostDepletionAndConfidence(
        int initialStock, double exposure, bool postDepletion, bool deadSlot, EstimateConfidence confidence)
    {
        var flags = StockoutMetricsCalculator.CalculateQualityFlags(initialStock, exposure, postDepletion);

        flags.HasFlag(StockoutQualityFlags.MissingSnapshot).Should().Be(initialStock == 0);
        flags.HasFlag(StockoutQualityFlags.SparseVelocity).Should().Be(exposure < 14);
        flags.HasFlag(StockoutQualityFlags.PostDepletionSales).Should().Be(postDepletion);
        StockoutMetricsCalculator.CalculateEstimateConfidence(flags, deadSlot).Should().Be(confidence);
    }

    [Fact]
    public void AggregateByProduct_UsesAuthoritativeSlotMetricsDeterministically()
    {
        var firstDepletion = new DateTime(2026, 7, 10, 2, 24, 0);
        var latestPostDepletionSale = new DateTime(2026, 7, 14, 9, 0, 0);
        var slots = new[]
        {
            Slot(1, "M2", 10, 10, 48, 120m, 50m, 2m, firstDepletion, false, null),
            Slot(1, "M1", 10, 10, 72, 180m, 75m, 4m, null, true, latestPostDepletionSale),
            Slot(2, "M1", 5, 3, 24, 60m, 25m, 6m, null, false, null)
        };

        var products = StockoutMetricsCalculator.AggregateByProduct(slots);

        products.Should().HaveCount(2);
        var product = products.Single(p => p.ProductoId == 1);
        product.CantidadTotalSlots.Should().Be(2);
        product.StockInicialTotal.Should().Be(20);
        product.CantidadVendidaTotal.Should().Be(20);
        product.HorasSinStock.Should().Be(72);
        product.DiasSinStock.Should().Be(3);
        product.VelocidadDiaria.Should().BeApproximately(3m, 0.001m);
        product.DineroPerdidoEstimado.Should().Be(300m);
        product.GananciaPerdidaEstimada.Should().Be(125m);
        product.FechaAgotamientoEstimada.Should().Be(firstDepletion);
        product.TieneVentasPosterioresAlAgotamiento.Should().BeTrue();
        product.UltimaVentaPosteriorAlAgotamiento.Should().Be(latestPostDepletionSale);
        product.Maquinas.Should().Equal("M1", "M2");
    }

    [Fact]
    public void AggregateByProduct_DoesNotApplyLossUntilAggregateStockIsDepleted()
    {
        var products = StockoutMetricsCalculator.AggregateByProduct(
        [
            Slot(1, "M1", 10, 6, 72, 180m, 75m, 4m, null, false, null),
            Slot(1, "M2", 10, 8, 48, 120m, 50m, 2m, null, false, null)
        ]);

        var product = products.Single();
        product.DineroPerdidoEstimado.Should().Be(0m);
        product.GananciaPerdidaEstimada.Should().Be(0m);
    }

    [Fact]
    public void AggregateByProduct_WeightsSlotEvidenceInsteadOfAveragingRates()
    {
        var product = StockoutMetricsCalculator.AggregateByProduct(
        [
            Slot(1, "A", 10, 10, 24, 0m, 0m, 0m, null, false, null,
                observedSales: 100, observedExposure: 200),
            Slot(1, "B", 10, 10, 24, 0m, 0m, 0m, null, false, null,
                observedSales: 10, observedExposure: 20)
        ]).Single();

        product.VelocidadDiaria.Should().Be(7m);
    }

    [Fact]
    public void AggregateByProduct_SumsOnlyDepletedSlotsAndExposesPartialMachineStockouts()
    {
        var product = StockoutMetricsCalculator.AggregateByProduct(
        [
            Slot(1, "A", 10, 10, 24, 20_000m, 8_000m, 0m, null, false, null),
            Slot(1, "B", 10, 3, 0, 99_000m, 39_600m, 0m, null, false, null),
            Slot(1, "C", 5, 5, 12, 5_000m, 2_000m, 0m, null, false, null)
        ]).Single();

        product.DineroPerdidoEstimado.Should().Be(25_000m);
        product.GananciaPerdidaEstimada.Should().Be(10_000m);
        product.CantidadSlotsAgotados.Should().Be(2);
        product.CantidadMaquinasAgotadas.Should().Be(2);
        product.MaquinasAgotadas.Should().Equal("A", "C");
        product.TieneQuiebreParcialPorMaquina.Should().BeTrue();
    }

    [Fact]
    public void AggregateByProduct_PreservesDecimalLossUntilMoneyClpPresentation()
    {
        var product = StockoutMetricsCalculator.AggregateByProduct(
            Enumerable.Range(1, 3).Select(index => Slot(1, $"M{index}", 1, 1, 1,
                133.50m, 0m, 0m, null, false, null))).Single();

        product.DineroPerdidoEstimado.Should().Be(400.50m);
        StockoutMetricsCalculator.MoneyClp(product.DineroPerdidoEstimado).Should().Be(401m);
    }

    [Fact]
    public void AggregateByProduct_CountsSharedProductMachinePoolEvidenceOnce()
    {
        const string poolId = "1:1:pool";
        var product = StockoutMetricsCalculator.AggregateByProduct(
        [
            Slot(1, "A", 10, 10, 1, 0m, 0m, 0m, null, false, null,
                poolId: poolId, poolRepresentative: true, poolSales: 50, poolExposure: 100),
            Slot(1, "A", 10, 10, 1, 0m, 0m, 0m, null, false, null,
                poolId: poolId, poolRepresentative: false)
        ]).Single();

        product.VelocidadDiaria.Should().Be(7m);
    }

    [Fact]
    public void AggregateByProduct_ExcludesDeadSlotsFromAllEstimates()
    {
        var product = StockoutMetricsCalculator.AggregateByProduct(
        [
            Slot(1, "M1", 10, 10, 10, 10_000m, 4_000m, 0m, null, false, null),
            new StockoutAnalysisDto { ProductoId = 1, ProductoNombre = "Product 1", MaquinaNombre = "M2", EsDeadSlot = true, PosibleQuiebre = true, DineroPerdidoEstimado = 99_000m, GananciaPerdidaEstimada = 39_600m }
        ]).Single();

        product.DineroPerdidoEstimado.Should().Be(10_000m);
        product.GananciaPerdidaEstimada.Should().Be(4_000m);
        typeof(StockoutProductoDto).GetProperty("UnidadesNoAtendidasEstimadas")!.GetValue(product).Should().Be(0m);
    }

    [Fact]
    public void CalculateProductMachineStockouts_IsolatesMachinesAndDoesNotPoolTheirInventory()
    {
        var rows = StockoutMetricsCalculator.CalculateProductMachineStockouts(
            [
                ProductSlot(1, "Machine A", "1", 7, 5),
                ProductSlot(2, "Machine B", "1", 7, 5)
            ],
            [
                Sale(1, 7, "1", 1), Sale(1, 7, "1", 2), Sale(1, 7, "1", 3), Sale(1, 7, "1", 4), Sale(1, 7, "1", 5),
                Sale(2, 7, "1", 1), Sale(2, 7, "1", 2), Sale(2, 7, "1", 3)
            ], PeriodStart, ReportEnd);

        rows.Should().HaveCount(2);
        rows.Single(row => row.MaquinaId == 1).FechaAgotamientoEstimada.Should().Be(SaleTime(5));
        rows.Single(row => row.MaquinaId == 2).FechaAgotamientoEstimada.Should().BeNull();
        rows.Single(row => row.MaquinaId == 2).StockRestante.Should().Be(2);
    }

    [Fact]
    public void CalculateProductMachineStockouts_UsesInterleavedSlotChronologyForExactDepletionBoundary()
    {
        var rows = StockoutMetricsCalculator.CalculateProductMachineStockouts(
            [ProductSlot(1, "Machine A", "1", 7, 2), ProductSlot(1, "Machine A", "2", 7, 3)],
            [Sale(1, 7, "2", 1), Sale(1, 7, "1", 2), Sale(1, 7, "2", 3), Sale(1, 7, "1", 4), Sale(1, 7, "2", 5)],
            PeriodStart, ReportEnd);

        var row = rows.Single();
        row.CantidadVendidaTotal.Should().Be(5);
        row.FechaAgotamientoEstimada.Should().Be(SaleTime(5));
        row.HorasSinStock.Should().NotBeNull();
        row.DineroPerdidoEstimado.Should().NotBeNull();
    }

    [Fact]
    public void CalculateProductMachineStockouts_CapsOversalesPerSlotAndKeepsPartialStateSeparate()
    {
        var rows = StockoutMetricsCalculator.CalculateProductMachineStockouts(
            [ProductSlot(1, "Machine A", "1", 7, 2), ProductSlot(1, "Machine A", "2", 7, 3)],
            [Sale(1, 7, "1", 1), Sale(1, 7, "1", 2), Sale(1, 7, "1", 3), Sale(1, 7, "1", 4)],
            PeriodStart, ReportEnd);

        var row = rows.Single();
        row.CantidadVendidaTotal.Should().Be(2, "oversales in slot 1 cannot consume slot 2 stock");
        row.StockRestante.Should().Be(3);
        row.SlotsParcialmenteAgotados.Should().Equal("1");
        row.FechaAgotamientoEstimada.Should().BeNull();
        row.HorasSinStock.Should().BeNull();
        row.UnidadesNoAtendidasEstimadas.Should().BeNull();
        row.DineroPerdidoEstimado.Should().BeNull();
    }

    [Fact]
    public void CalculateProductMachineStockouts_ExcludesUnreliableSlotsAndDoesNotInferDepletionWithoutChronology()
    {
        var rows = StockoutMetricsCalculator.CalculateProductMachineStockouts(
            [
                ProductSlot(1, "Machine A", "1", 7, 5, reportedSold: 5),
                ProductSlot(1, "Machine A", "2", 7, 5, StockoutQualityFlags.MissingSnapshot),
                ProductSlot(1, "Machine A", "3", 7, 0)
            ],
            [], PeriodStart, ReportEnd);

        var row = rows.Single();
        row.CantidadSlotsElegibles.Should().Be(1);
        row.CantidadSlotsExcluidos.Should().Be(2);
        row.StockInicialTotal.Should().Be(5);
        row.TieneDatosNoConfiables.Should().BeTrue();
        row.TieneEvidenciaCronologicaIncompleta.Should().BeTrue();
        row.FechaAgotamientoEstimada.Should().BeNull();
        row.DineroPerdidoEstimado.Should().BeNull();
    }

    [Fact]
    public void CalculateProductMachineStockouts_PreservesInputSlotMathAndQualityMetadata()
    {
        var depletion = SaleTime(2);
        var slot = ProductSlot(1, "Machine A", "1", 7, 2, reportedSold: 1);
        slot.FechaAgotamientoEstimada = depletion;
        slot.DineroPerdidoEstimado = 123m;
        slot.GananciaPerdidaEstimada = 45m;
        slot.UnidadesNoAtendidasEstimadas = 2m;
        slot.VelocidadObservadaSlotPorHora = 0.5m;
        slot.VelocidadEfectivaPorHora = 0.75m;
        slot.QualityFlags = StockoutQualityFlags.SparseVelocity;
        slot.EstimateConfidence = EstimateConfidence.Low;

        StockoutMetricsCalculator.CalculateProductMachineStockouts(
            [slot], [Sale(1, 7, "1", 1)], PeriodStart, ReportEnd);

        slot.StockInicial.Should().Be(2);
        slot.CantidadVendida.Should().Be(1);
        slot.FechaAgotamientoEstimada.Should().Be(depletion);
        slot.DineroPerdidoEstimado.Should().Be(123m);
        slot.GananciaPerdidaEstimada.Should().Be(45m);
        slot.UnidadesNoAtendidasEstimadas.Should().Be(2m);
        slot.VelocidadObservadaSlotPorHora.Should().Be(0.5m);
        slot.VelocidadEfectivaPorHora.Should().Be(0.75m);
        slot.QualityFlags.Should().Be(StockoutQualityFlags.SparseVelocity);
        slot.EstimateConfidence.Should().Be(EstimateConfidence.Low);
    }

    [Fact]
    public void CalculateProductMachineStockouts_PreservesDepletionAndStopsLossAtFirstPostDepletionRefill()
    {
        var refillTime = SaleTime(4);
        var row = StockoutMetricsCalculator.CalculateProductMachineStockouts(
            [ProductSlot(1, "Machine A", "1", 7, 2)],
            [Sale(1, 7, "1", 1), Sale(1, 7, "1", 2)],
            [Refill(1, 7, "1", refillTime, 2)],
            PeriodStart,
            ReportEnd).Single();

        row.FechaAgotamientoEstimada.Should().Be(SaleTime(2));
        row.TieneAnomalias.Should().BeTrue();
        row.HorasSinStock.Should().Be(2);
        row.UnidadesNoAtendidasEstimadas.Should().Be(2m);
    }

    [Fact]
    public void CalculateProductMachineStockouts_DoesNotFlagRefillEvidenceBeforeDepletion()
    {
        var row = StockoutMetricsCalculator.CalculateProductMachineStockouts(
            [ProductSlot(1, "Machine A", "1", 7, 2)],
            [Sale(1, 7, "1", 1), Sale(1, 7, "1", 2)],
            [Refill(1, 7, "1", SaleTime(1), 2)],
            PeriodStart,
            ReportEnd).Single();

        row.FechaAgotamientoEstimada.Should().Be(SaleTime(2));
        row.TieneAnomalias.Should().BeFalse();
        row.HorasSinStock.Should().Be(8);
    }

    [Fact]
    public void StockoutDashboardAnalysisDto_ExposesSlotAndMachineProductCollections()
    {
        var bundle = new StockoutDashboardAnalysisDto
        {
            Slots = [ProductSlot(1, "Machine A", "1", 7, 5)],
            ProductosMaquina = [new StockoutProductoMaquinaDto { MaquinaId = 1, ProductoId = 7, StockInicialTotal = 5 }]
        };

        bundle.Slots.Should().ContainSingle(slot => slot.NumeroSlot == "1");
        bundle.ProductosMaquina.Should().ContainSingle(row => row.StockRestante == 5);
    }

    private static readonly DateTime PeriodStart = new(2026, 7, 10, 8, 0, 0);
    private static readonly DateTime ReportEnd = new(2026, 7, 10, 18, 0, 0);

    private static StockoutAnalysisDto ProductSlot(int machineId, string machine, string slot, int productId, int initialStock,
        StockoutQualityFlags qualityFlags = StockoutQualityFlags.None, int reportedSold = 0) => new()
        {
            MaquinaId = machineId,
            MaquinaNombre = machine,
            NumeroSlot = slot,
            ProductoId = productId,
            ProductoNombre = $"Product {productId}",
            StockInicial = initialStock,
            CantidadVendida = reportedSold,
            QualityFlags = qualityFlags
        };

    private static StockoutProductoMaquinaVentaDto Sale(int machineId, int productId, string slot, int hour)
        => new()
        {
            MaquinaId = machineId,
            ProductoId = productId,
            NumeroSlot = slot,
            FechaLocal = SaleTime(hour),
            VentaId = hour
        };

    private static StockoutProductoMaquinaRecargaDto Refill(int machineId, int productId, string slot, DateTime time, int units)
        => new()
        {
            MaquinaId = machineId,
            ProductoId = productId,
            NumeroSlot = slot,
            FechaLocal = time,
            UnidadesAgregadas = units
        };

    private static DateTime SaleTime(int hour) => PeriodStart.AddHours(hour);

    private static StockoutAnalysisDto Slot(
        int productId,
        string machine,
        int initialStock,
        int sold,
        double stockoutHours,
        decimal revenueLoss,
        decimal profitLoss,
        decimal dailyVelocity,
        DateTime? depletion,
        bool hasPostDepletionSales,
        DateTime? lastPostDepletionSale,
        int? observedSales = null,
        double? observedExposure = null,
        string? poolId = null,
        bool poolRepresentative = false,
        int? poolSales = null,
        double? poolExposure = null) => new()
        {
            ProductoId = productId,
            ProductoNombre = $"Product {productId}",
            MaquinaNombre = machine,
            StockInicial = initialStock,
            CantidadVendida = sold,
            HorasSinStock = stockoutHours,
            DineroPerdidoEstimado = revenueLoss,
            GananciaPerdidaEstimada = profitLoss,
            VelocidadEfectivaPorHora = dailyVelocity / HorarioOperativoHelper.HorasOperativasPorDia,
            VelocidadObservadaSlotPorHora = (observedSales ?? (int)dailyVelocity)
                / (decimal)Math.Max(observedExposure ?? HorarioOperativoHelper.HorasOperativasPorDia, 1),
            VentasOperativasObservadas = observedSales ?? (int)dailyVelocity,
            HorasExposicionOperativas = observedExposure ?? HorarioOperativoHelper.HorasOperativasPorDia,
            OrigenVelocidad = poolId is null ? OrigenVelocidad.Slot : OrigenVelocidad.ProductoMaquina,
            IdPoolVelocidadProductoMaquina = poolId,
            EsRepresentantePoolVelocidad = poolRepresentative,
            VentasOperativasPoolProductoMaquina = poolSales,
            HorasExposicionPoolProductoMaquina = poolExposure,
            FechaAgotamientoEstimada = depletion,
            TieneVentasPosterioresAlAgotamiento = hasPostDepletionSales,
            UltimaVentaPosteriorAlAgotamiento = lastPostDepletionSale
        };
}
