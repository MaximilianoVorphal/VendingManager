namespace VendingManager.Shared.Helpers;

using VendingManager.Shared.DTOs;

/// <summary>
/// Shared, deterministic stockout calculations used by backend services and clients.
/// </summary>
public static class StockoutMetricsCalculator
{
    public static double CalendarDays(double wallClockHours) => wallClockHours / 24d;

    public static StockoutLossMetrics CalculateOperatingWindowLoss(
        decimal velocidadPorHora,
        DateTime fechaAgotamiento,
        DateTime finReporte,
        decimal precioPromedioVenta,
        decimal gananciaPromedio)
    {
        var horasOperativas = HorarioOperativoHelper.HorasEnRangoOperativo(fechaAgotamiento, finReporte);
        var unidadesPerdidas = velocidadPorHora * (decimal)horasOperativas;

        return new StockoutLossMetrics(
            horasOperativas,
            unidadesPerdidas * precioPromedioVenta,
            unidadesPerdidas * gananciaPromedio,
            unidadesPerdidas);
    }

    public static StockoutVelocityMetrics CalculateSlotVelocity(IEnumerable<DateTime> sales, DateTime observationStart, DateTime? depletion, DateTime reportEnd)
    {
        var windowEnd = depletion is { } date && date < reportEnd ? date : reportEnd;
        var operatingSales = sales.Count(sale => sale <= windowEnd && HorarioOperativoHelper.EsHoraOperativa(sale));
        var exposure = HorarioOperativoHelper.HorasEnRangoOperativo(observationStart, windowEnd);
        return new StockoutVelocityMetrics(operatingSales, exposure, operatingSales / (decimal)Math.Max(exposure, 1));
    }

    public static ProductMachineVelocityPool CreateProductMachinePool(
        int maquinaId, int productoId, DateTime observationStart, DateTime observationEnd, IEnumerable<DateTime> sales)
    {
        var operatingSales = sales.Count(HorarioOperativoHelper.EsHoraOperativa);
        var exposure = HorarioOperativoHelper.HorasEnRangoOperativo(observationStart, observationEnd);
        return new ProductMachineVelocityPool(
            $"{maquinaId}:{productoId}:{observationStart:O}:{observationEnd:O}",
            operatingSales, exposure, operatingSales / (decimal)Math.Max(exposure, 1));
    }

    public static StockoutVelocitySelection SelectEffectiveVelocity(
        int productoId, bool deadSlot, StockoutVelocityMetrics observed, ProductMachineVelocityPool? pool)
    {
        if (deadSlot || productoId <= 0)
            return new(0m, OrigenVelocidad.Slot);

        if (pool is { IsUsable: true })
            return new(pool.VelocidadPorHora, OrigenVelocidad.ProductoMaquina);

        return new(observed.VelocidadPorHora, OrigenVelocidad.Slot);
    }

    public static StockoutLossMetrics CalculateConservativeLoss(decimal velocidadPorHora, DateTime fechaAgotamiento, DateTime? primeraVentaPosterior, DateTime finReporte, decimal precioPromedioVenta, decimal gananciaPromedio)
        => CalculateOperatingWindowLoss(velocidadPorHora, fechaAgotamiento,
            primeraVentaPosterior is { } post && post < finReporte ? post : finReporte, precioPromedioVenta, gananciaPromedio);

    public static StockoutQualityFlags CalculateQualityFlags(int stockInicial, double exposure, bool postDepletionSales)
    {
        return (stockInicial <= 0 ? StockoutQualityFlags.MissingSnapshot : StockoutQualityFlags.None)
            | (exposure < HorarioOperativoHelper.HorasOperativasPorDia ? StockoutQualityFlags.SparseVelocity : StockoutQualityFlags.None)
            | (postDepletionSales ? StockoutQualityFlags.PostDepletionSales : StockoutQualityFlags.None);
    }

    public static EstimateConfidence CalculateEstimateConfidence(StockoutQualityFlags flags, bool deadSlot)
        => deadSlot || flags.HasFlag(StockoutQualityFlags.MissingSnapshot)
            ? EstimateConfidence.NotEstimable
            : flags != StockoutQualityFlags.None ? EstimateConfidence.Low : EstimateConfidence.Medium;

    public static decimal MoneyClp(decimal amount) => Math.Round(amount, 0, MidpointRounding.AwayFromZero);

    public static List<StockoutProductoMaquinaDto> CalculateProductMachineStockouts(
        IEnumerable<StockoutAnalysisDto> slots,
        IEnumerable<StockoutProductoMaquinaVentaDto> sales,
        DateTime observationStart,
        DateTime reportEnd)
        => CalculateProductMachineStockouts(slots, sales, [], observationStart, reportEnd);

    public static List<StockoutProductoMaquinaDto> CalculateProductMachineStockouts(
        IEnumerable<StockoutAnalysisDto> slots,
        IEnumerable<StockoutProductoMaquinaVentaDto> sales,
        IEnumerable<StockoutProductoMaquinaRecargaDto> refills,
        DateTime observationStart,
        DateTime reportEnd)
    {
        var saleEvidence = sales.ToList();
        var refillEvidence = refills.ToList();
        return slots
            .Where(slot => slot.ProductoId is > 0)
            .GroupBy(slot => new ProductMachineKey(slot.MaquinaId, slot.ProductoId!.Value))
            .OrderBy(group => group.Key.MaquinaId)
            .ThenBy(group => group.Key.ProductoId)
            .Select(group => CalculateProductMachineStockout(group.Key, group.ToList(), saleEvidence, refillEvidence, observationStart, reportEnd))
            .ToList();
    }

    private static StockoutProductoMaquinaDto CalculateProductMachineStockout(
        ProductMachineKey key,
        List<StockoutAnalysisDto> allSlots,
        List<StockoutProductoMaquinaVentaDto> sales,
        List<StockoutProductoMaquinaRecargaDto> refills,
        DateTime observationStart,
        DateTime reportEnd)
    {
        var eligibleSlots = allSlots
            .Where(slot => slot.StockInicial > 0 && !slot.QualityFlags.HasFlag(StockoutQualityFlags.MissingSnapshot))
            .OrderBy(slot => SlotOrder(slot.NumeroSlot))
            .ThenBy(slot => slot.NumeroSlot, StringComparer.Ordinal)
            .ToList();
        var slotLoads = eligibleSlots.ToDictionary(slot => slot.NumeroSlot, slot => slot.StockInicial, StringComparer.Ordinal);
        var consumedBySlot = slotLoads.Keys.ToDictionary(slot => slot, _ => 0, StringComparer.Ordinal);
        var totalStock = slotLoads.Values.Sum();
        var consumedTotal = 0;
        var orderedSales = sales
            .Where(sale => sale.MaquinaId == key.MaquinaId && sale.ProductoId == key.ProductoId && slotLoads.ContainsKey(sale.NumeroSlot))
            .OrderBy(sale => sale.FechaLocal)
            .ThenBy(sale => SlotOrder(sale.NumeroSlot))
            .ThenBy(sale => sale.NumeroSlot, StringComparer.Ordinal)
            .ThenBy(sale => sale.VentaId)
            .ToList();
        var orderedRefills = refills
            .Where(refill => refill.MaquinaId == key.MaquinaId && refill.ProductoId == key.ProductoId &&
                             refill.UnidadesAgregadas > 0 && slotLoads.ContainsKey(refill.NumeroSlot))
            .OrderBy(refill => refill.FechaLocal)
            .ToList();

        DateTime? depletion = null;
        var consumedSales = new List<StockoutProductoMaquinaVentaDto>();
        foreach (var sale in orderedSales)
        {
            if (consumedBySlot[sale.NumeroSlot] >= slotLoads[sale.NumeroSlot])
                continue;

            consumedBySlot[sale.NumeroSlot]++;
            consumedTotal++;
            consumedSales.Add(sale);
            if (consumedTotal == totalStock)
                depletion = sale.FechaLocal;
        }

        var isDepleted = totalStock > 0 && depletion.HasValue;
        var row = new StockoutProductoMaquinaDto
        {
            MaquinaId = key.MaquinaId,
            MaquinaNombre = allSlots.OrderBy(slot => slot.MaquinaNombre, StringComparer.Ordinal).First().MaquinaNombre,
            ProductoId = key.ProductoId,
            ProductoNombre = allSlots.OrderBy(slot => slot.ProductoNombre, StringComparer.Ordinal).First().ProductoNombre,
            CantidadSlotsElegibles = eligibleSlots.Count,
            CantidadSlotsExcluidos = allSlots.Count - eligibleSlots.Count,
            StockInicialTotal = totalStock,
            CantidadVendidaTotal = consumedTotal,
            SlotsParcialmenteAgotados = isDepleted
                ? []
                : eligibleSlots.Where(slot => consumedBySlot[slot.NumeroSlot] == slot.StockInicial).Select(slot => slot.NumeroSlot).ToList(),
            TieneDatosNoConfiables = allSlots.Count != eligibleSlots.Count,
            TieneEvidenciaCronologicaIncompleta = totalStock > 0 && !isDepleted,
            TieneAnomalias = orderedSales.Count > consumedSales.Count
        };

        if (!isDepleted)
            return row;

        var depletionTime = depletion!.Value;
        DateTime? firstPostDepletionAnomaly = orderedSales
            .Where(sale => sale.FechaLocal > depletionTime)
            .Select(sale => (DateTime?)sale.FechaLocal)
            .Concat(orderedRefills.Where(refill => refill.FechaLocal > depletionTime).Select(refill => (DateTime?)refill.FechaLocal))
            .Min();
        var operatingSales = consumedSales.Count(sale => sale.FechaLocal <= depletionTime && HorarioOperativoHelper.EsHoraOperativa(sale.FechaLocal));
        var exposure = HorarioOperativoHelper.HorasEnRangoOperativo(observationStart, depletionTime);
        var velocity = operatingSales / (decimal)Math.Max(exposure, 1d);
        var preDepletionSales = consumedSales.Where(sale => sale.FechaLocal <= depletionTime).ToList();
        var averagePrice = preDepletionSales.Count == 0 ? 0m : preDepletionSales.Average(sale => sale.PrecioVenta);
        var averageMargin = preDepletionSales.Count == 0 ? 0m : preDepletionSales.Average(sale => sale.GananciaUnitaria);
        var loss = CalculateConservativeLoss(velocity, depletionTime, firstPostDepletionAnomaly,
            reportEnd, averagePrice, averageMargin);

        row.FechaAgotamientoEstimada = depletion;
        row.HorasSinStock = loss.HorasOperativas;
        row.VelocidadPorHora = velocity;
        row.UnidadesNoAtendidasEstimadas = loss.UnidadesNoAtendidasEstimadas;
        row.DineroPerdidoEstimado = loss.DineroPerdidoEstimado;
        row.GananciaPerdidaEstimada = loss.GananciaPerdidaEstimada;
        row.TieneAnomalias |= firstPostDepletionAnomaly.HasValue;
        row.TieneEvidenciaCronologicaIncompleta = false;
        return row;
    }

    public static List<StockoutProductoDto> AggregateByProduct(IEnumerable<StockoutAnalysisDto> slots)
        => slots
            .GroupBy(slot => new ProductKey(slot.ProductoId, slot.ProductoNombre))
            .OrderBy(group => group.Key.ProductoNombre, StringComparer.Ordinal)
            .Select(AggregateProduct)
            .ToList();

    private static StockoutProductoDto AggregateProduct(IGrouping<ProductKey, StockoutAnalysisDto> group)
    {
        var orderedSlots = group
            .OrderBy(slot => slot.MaquinaNombre, StringComparer.Ordinal)
            .ThenBy(slot => slot.NumeroSlot, StringComparer.Ordinal)
            .ToList();
        var depletedSlots = orderedSlots.Where(IsDepleted).ToList();
        var depletedMachines = depletedSlots
            .Select(slot => slot.MaquinaNombre)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        var postDepletionSales = orderedSlots
            .Where(slot => slot.TieneVentasPosterioresAlAgotamiento)
            .Select(slot => slot.UltimaVentaPosteriorAlAgotamiento)
            .Where(fecha => fecha.HasValue)
            .Select(fecha => fecha!.Value)
            .ToList();

        return new StockoutProductoDto
        {
            ProductoId = group.Key.ProductoId,
            ProductoNombre = group.Key.ProductoNombre,
            CantidadTotalSlots = orderedSlots.Count,
            StockInicialTotal = orderedSlots.Sum(slot => slot.StockInicial),
            CantidadVendidaTotal = orderedSlots.Sum(slot => slot.CantidadVendida),
            PrimeraVenta = Earliest(orderedSlots.Select(slot => slot.PrimeraVenta)),
            UltimaVenta = Latest(orderedSlots.Select(slot => slot.UltimaVenta)),
            FechaAgotamientoEstimada = Earliest(orderedSlots.Select(slot => slot.FechaAgotamientoEstimada)),
            TieneVentasPosterioresAlAgotamiento = orderedSlots.Any(slot => slot.TieneVentasPosterioresAlAgotamiento),
            UltimaVentaPosteriorAlAgotamiento = postDepletionSales.Count == 0 ? null : postDepletionSales.Max(),
            HorasSinStock = orderedSlots.Max(slot => slot.HorasSinStock),
            DineroPerdidoEstimado = depletedSlots.Sum(slot => slot.DineroPerdidoEstimado),
            GananciaPerdidaEstimada = depletedSlots.Sum(slot => slot.GananciaPerdidaEstimada),
            UnidadesNoAtendidasEstimadas = depletedSlots.Sum(slot => slot.UnidadesNoAtendidasEstimadas),
            VelocidadDiaria = CalculateWeightedDailyVelocity(orderedSlots),
            PosibleQuiebre = depletedSlots.Count > 0,
            Maquinas = orderedSlots.Select(slot => slot.MaquinaNombre).Distinct(StringComparer.Ordinal).ToList(),
            CantidadSlotsAgotados = depletedSlots.Count,
            CantidadMaquinasAgotadas = depletedMachines.Count,
            MaquinasAgotadas = depletedMachines,
            TieneQuiebreParcialPorMaquina = depletedSlots.Count > 0 && depletedSlots.Count < orderedSlots.Count
        };
    }

    private static bool IsDepleted(StockoutAnalysisDto slot)
        => !slot.EsDeadSlot && (slot.PosibleQuiebre || (slot.StockInicial > 0 && slot.CantidadVendida >= slot.StockInicial));

    private static decimal CalculateWeightedDailyVelocity(IEnumerable<StockoutAnalysisDto> slots)
    {
        var contributions = new List<VelocityContribution>();
        foreach (var slot in slots.Where(slot => slot.OrigenVelocidad == OrigenVelocidad.Slot))
            AddSlotContribution(contributions, slot);

        foreach (var poolGroup in slots
                     .Where(slot => slot.OrigenVelocidad == OrigenVelocidad.ProductoMaquina)
                     .GroupBy(slot => slot.IdPoolVelocidadProductoMaquina))
        {
            var poolSlots = poolGroup.ToList();
            var representative = poolSlots.SingleOrDefault(slot => slot.EsRepresentantePoolVelocidad);
            if (!string.IsNullOrWhiteSpace(poolGroup.Key) &&
                poolSlots.Count(slot => slot.EsRepresentantePoolVelocidad) == 1 &&
                HasEvidence(representative?.VentasOperativasPoolProductoMaquina, representative?.HorasExposicionPoolProductoMaquina))
            {
                contributions.Add(new VelocityContribution(
                    representative!.VentasOperativasPoolProductoMaquina!.Value,
                    representative.HorasExposicionPoolProductoMaquina!.Value));
                continue;
            }

            foreach (var slot in poolSlots)
                AddSlotContribution(contributions, slot);
        }

        var sales = contributions.Sum(contribution => contribution.Sales);
        var exposure = contributions.Sum(contribution => contribution.Exposure);
        return exposure > 0
            ? sales / (decimal)exposure * HorarioOperativoHelper.HorasOperativasPorDia
            : 0m;
    }

    private static void AddSlotContribution(List<VelocityContribution> contributions, StockoutAnalysisDto slot)
    {
        if (HasEvidence(slot.VentasOperativasObservadas, slot.HorasExposicionOperativas))
            contributions.Add(new VelocityContribution(slot.VentasOperativasObservadas, slot.HorasExposicionOperativas));
    }

    private static bool HasEvidence(int? sales, double? exposure)
        => sales is > 0 && exposure is > 0;

    private sealed record VelocityContribution(int Sales, double Exposure);

    private static DateTime? Earliest(IEnumerable<DateTime?> dates)
    {
        var values = dates.Where(fecha => fecha.HasValue).Select(fecha => fecha!.Value).ToList();
        return values.Count == 0 ? null : values.Min();
    }

    private static DateTime? Latest(IEnumerable<DateTime?> dates)
    {
        var values = dates.Where(fecha => fecha.HasValue).Select(fecha => fecha!.Value).ToList();
        return values.Count == 0 ? null : values.Max();
    }

    private static int SlotOrder(string slot) => int.TryParse(slot, out var value) ? value : int.MaxValue;

    private sealed record ProductKey(int? ProductoId, string ProductoNombre);
    private sealed record ProductMachineKey(int MaquinaId, int ProductoId);
}

public sealed record StockoutLossMetrics(
    double HorasOperativas,
    decimal DineroPerdidoEstimado,
    decimal GananciaPerdidaEstimada,
    decimal UnidadesNoAtendidasEstimadas = 0);

public sealed record StockoutVelocityMetrics(int VentasOperativasObservadas, double HorasExposicionOperativas, decimal VelocidadPorHora);
public sealed record ProductMachineVelocityPool(string Id, int VentasOperativas, double HorasExposicionOperativas, decimal VelocidadPorHora)
{
    public bool IsUsable => VentasOperativas > 0 && HorasExposicionOperativas >= HorarioOperativoHelper.HorasOperativasPorDia && VelocidadPorHora > 0;
}
public sealed record StockoutVelocitySelection(decimal VelocidadEfectivaPorHora, OrigenVelocidad OrigenVelocidad);
