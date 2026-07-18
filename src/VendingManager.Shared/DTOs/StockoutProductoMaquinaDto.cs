namespace VendingManager.Shared.DTOs;

/// <summary>
/// Machine-bounded stockout analysis for one product. Derived metrics are present only after
/// chronological sales evidence confirms that every eligible slot is depleted.
/// </summary>
public class StockoutProductoMaquinaDto
{
    public int MaquinaId { get; set; }
    public string MaquinaNombre { get; set; } = string.Empty;
    public int ProductoId { get; set; }
    public string ProductoNombre { get; set; } = string.Empty;
    public int CantidadSlotsElegibles { get; set; }
    public int CantidadSlotsExcluidos { get; set; }
    public int StockInicialTotal { get; set; }
    public int CantidadVendidaTotal { get; set; }
    public int StockRestante => Math.Max(0, StockInicialTotal - CantidadVendidaTotal);
    public List<string> SlotsParcialmenteAgotados { get; set; } = new();
    public bool TieneQuiebreParcial => SlotsParcialmenteAgotados.Count > 0;
    public bool TieneDatosNoConfiables { get; set; }
    public bool TieneEvidenciaCronologicaIncompleta { get; set; }
    public bool TieneAnomalias { get; set; }
    public DateTime? FechaAgotamientoEstimada { get; set; }
    public double? HorasSinStock { get; set; }
    public decimal? VelocidadPorHora { get; set; }
    public decimal? UnidadesNoAtendidasEstimadas { get; set; }
    public decimal? DineroPerdidoEstimado { get; set; }
    public decimal? GananciaPerdidaEstimada { get; set; }
}

/// <summary>
/// Raw sale evidence attributed to a machine, product, and slot for deterministic aggregation.
/// </summary>
public class StockoutProductoMaquinaVentaDto
{
    public int MaquinaId { get; set; }
    public int ProductoId { get; set; }
    public string NumeroSlot { get; set; } = string.Empty;
    public DateTime FechaLocal { get; set; }
    public int VentaId { get; set; }
    public decimal PrecioVenta { get; set; }
    public decimal GananciaUnitaria { get; set; }
}
