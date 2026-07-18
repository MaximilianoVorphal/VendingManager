namespace VendingManager.Shared.DTOs;

/// <summary>
/// Aggregated stockout metrics for a product across its authoritative slot results.
/// </summary>
public class StockoutProductoDto
{
    public int? ProductoId { get; set; }
    public string ProductoNombre { get; set; } = string.Empty;
    public int CantidadTotalSlots { get; set; }
    public int StockInicialTotal { get; set; }
    public int CantidadVendidaTotal { get; set; }
    public DateTime? PrimeraVenta { get; set; }
    public DateTime? UltimaVenta { get; set; }
    public DateTime? FechaAgotamientoEstimada { get; set; }
    public bool TieneVentasPosterioresAlAgotamiento { get; set; }
    public DateTime? UltimaVentaPosteriorAlAgotamiento { get; set; }
    public double HorasSinStock { get; set; }
    public double DiasSinStock => HorasSinStock / 24.0;
    public decimal DineroPerdidoEstimado { get; set; }
    public decimal GananciaPerdidaEstimada { get; set; }
    public decimal UnidadesNoAtendidasEstimadas { get; set; }
    public decimal VelocidadDiaria { get; set; }
    public bool PosibleQuiebre { get; set; }
    public List<string> Maquinas { get; set; } = new();
    public int CantidadSlotsAgotados { get; set; }
    public int CantidadMaquinasAgotadas { get; set; }
    public List<string> MaquinasAgotadas { get; set; } = new();
    public bool TieneQuiebreParcialPorMaquina { get; set; }

    public string MaquinasResumen => Maquinas.Count switch
    {
        0 => "-",
        1 => Maquinas[0],
        <= 3 => string.Join(", ", Maquinas),
        _ => $"{Maquinas.Count} máquinas"
    };

    public string MaquinasAgotadasResumen => MaquinasAgotadas.Count switch
    {
        0 => "-",
        1 => MaquinasAgotadas[0],
        <= 3 => string.Join(", ", MaquinasAgotadas),
        _ => $"{MaquinasAgotadas.Count} máquinas"
    };

    public string NivelAlerta => HorasSinStock switch
    {
        > 72 => "Crítico",
        > 48 => "Alto",
        > 24 => "Medio",
        _ => "Normal"
    };

    public string ColorAlerta => NivelAlerta switch
    {
        "Crítico" => "bg-danger text-white",
        "Alto" => "bg-warning text-dark",
        "Medio" => "bg-info text-dark",
        _ => "bg-light text-muted border"
    };
}
