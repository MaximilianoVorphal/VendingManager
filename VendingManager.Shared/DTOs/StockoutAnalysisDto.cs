namespace VendingManager.Shared.DTOs;

/// <summary>
/// DTO para el análisis de quiebres de stock y costo de oportunidad.
/// Representa datos calculados para cada combinación Máquina + Producto.
/// </summary>
public class StockoutAnalysisDto
{
    // =============================================
    // IDENTIFICACIÓN
    // =============================================
    public int MaquinaId { get; set; }
    public string MaquinaNombre { get; set; } = string.Empty;
    public int? ProductoId { get; set; }
    public string ProductoNombre { get; set; } = string.Empty;
    public string NumeroSlot { get; set; } = string.Empty;

    // =============================================
    // FECHAS CRÍTICAS
    // =============================================
    /// <summary>Primera venta del producto en el periodo analizado</summary>
    public DateTime? PrimeraVenta { get; set; }

    /// <summary>Última venta del producto específico</summary>
    public DateTime? UltimaVenta { get; set; }

    /// <summary>Última venta de CUALQUIER producto en la máquina</summary>
    public DateTime UltimaActividadMaquina { get; set; }

    /// <summary>Fecha fin del periodo de reporte</summary>
    public DateTime FinReporte { get; set; }

    // =============================================
    // MÉTRICAS DE STOCKOUT
    // =============================================
    /// <summary>True si el producto dejó de venderse mientras la máquina seguía activa</summary>
    public bool PosibleQuiebre { get; set; }

    /// <summary>Horas desde última venta hasta fin del periodo (o última actividad máquina)</summary>
    public double HorasSinStock { get; set; }

    /// <summary>Días sin stock (calculado)</summary>
    public double DiasSinStock => HorasSinStock / 24.0;

    // =============================================
    // VELOCIDAD REAL (basada en horas activas, NO días calendario)
    // =============================================
    /// <summary>Stock inicial según snapshot del template (0 si no hay snapshot)</summary>
    public int StockInicial { get; set; }

    /// <summary>Cantidad total vendida en el periodo</summary>
    public int CantidadVendida { get; set; }

    /// <summary>Horas entre primera y última venta del producto</summary>
    public double HorasActivas { get; set; }

    /// <summary>Unidades vendidas por hora (Cantidad / HorasActivas)</summary>
    public decimal VelocidadPorHora { get; set; }

    /// <summary>Unidades vendidas por día (VelocidadPorHora * 24)</summary>
    public decimal VelocidadDiaria => VelocidadPorHora * 24;

    // =============================================
    // COSTO DE OPORTUNIDAD
    // =============================================
    /// <summary>Precio promedio de venta del producto</summary>
    public decimal PrecioPromedioVenta { get; set; }

    /// <summary>Ganancia promedio por unidad (Precio - Costo)</summary>
    public decimal GananciaPromedio { get; set; }

    /// <summary>Dinero estimado perdido = VelocidadPorHora * HorasSinStock * PrecioPromedio</summary>
    public decimal DineroPerdidoEstimado { get; set; }

    /// <summary>Ganancia estimada perdida = VelocidadPorHora * HorasSinStock * GananciaPromedio</summary>
    public decimal GananciaPerdidaEstimada { get; set; }

    // =============================================
    // HELPERS PARA UI
    // =============================================
    /// <summary>Nivel de alerta basado en horas sin stock</summary>
    public string NivelAlerta => HorasSinStock switch
    {
        > 72 => "Crítico",
        > 48 => "Alto",
        > 24 => "Medio",
        _ => "Normal"
    };

    /// <summary>Color CSS para el nivel de alerta</summary>
    public string ColorAlerta => NivelAlerta switch
    {
        "Crítico" => "bg-danger text-white",
        "Alto" => "bg-warning text-dark",
        "Medio" => "bg-info text-dark",
        _ => "bg-light text-muted"
    };
}

/// <summary>
/// DTO para el timeline de disponibilidad (gráfico Gantt)
/// </summary>
public class TimelineDisponibilidadDto
{
    public string ProductoNombre { get; set; } = string.Empty;
    public string MaquinaNombre { get; set; } = string.Empty;

    /// <summary>Inicio del periodo con ventas (primera venta)</summary>
    public DateTime? InicioDisponible { get; set; }

    /// <summary>Fin del periodo con ventas (última venta)</summary>
    public DateTime? FinDisponible { get; set; }

    /// <summary>Inicio del periodo de silencio</summary>
    public DateTime? InicioSilencio { get; set; }

    /// <summary>Fin del periodo de silencio (fin reporte o última actividad)</summary>
    public DateTime? FinSilencio { get; set; }
}

/// <summary>
/// DTO para ventas diarias de un producto
/// </summary>
public class VentaDiariaDto
{
    public DateTime Fecha { get; set; }
    public int Cantidad { get; set; }
}
