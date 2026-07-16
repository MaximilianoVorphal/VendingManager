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

    /// <summary>Lista de fechas de todas las ventas para el timeline</summary>
    public List<DateTime> FechasVentas { get; set; } = new();

    // =============================================
    // MÉTRICAS DE STOCKOUT
    // =============================================
    /// <summary>True si el producto dejó de venderse mientras la máquina seguía activa</summary>
    public bool PosibleQuiebre { get; set; }

    /// <summary>Horas desde última venta hasta fin del periodo (o última actividad máquina)</summary>
    public double HorasSinStock { get; set; }

    /// <summary>Días sin stock (calculado sobre 14h operativas)</summary>
    public double DiasSinStock => HorasSinStock / 14.0;

    // =============================================
    // VELOCIDAD REAL (basada en horas activas, NO días calendario)
    // =============================================
    /// <summary>Stock inicial según snapshot del template (0 si no hay snapshot)</summary>
    public int StockInicial { get; set; }

    /// <summary>Stock actual/remanente del slot</summary>
    public int StockActual { get; set; }

    /// <summary>Cantidad total vendida en el periodo</summary>
    public int CantidadVendida { get; set; }

    /// <summary>Fill % (0-100, -1 si no aplica)</summary>
    public int FillPct { get; set; } = -1;

    /// <summary>Días estimados hasta stockout (null si no aplica)</summary>
    public decimal? DiasHastaStockout { get; set; }

    /// <summary>True si el slot no tuvo ventas en el período</summary>
    public bool EsDeadSlot { get; set; }

    /// <summary>Horas entre primera y última venta del producto</summary>
    public double HorasActivas { get; set; }

    /// <summary>Unidades vendidas por hora (Cantidad / HorasActivas)</summary>
    public decimal VelocidadPorHora { get; set; }

    /// <summary>Unidades vendidas por día (VelocidadPorHora * horas operativas)</summary>
    public decimal VelocidadDiaria => VelocidadPorHora * Helpers.HorarioOperativoHelper.HorasOperativasPorDia;

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

/// <summary>
/// DTO ligero para la lista inicial del dashboard de stockout.
/// Copia exacta de <see cref="StockoutAnalysisDto"/> sin la propiedad <see cref="StockoutAnalysisDto.FechasVentas"/>.
/// Sigue el patrón de split-DTO probado en recarga-movil-performance.
/// </summary>
public class StockoutSlotDto
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
    public DateTime? PrimeraVenta { get; set; }
    public DateTime? UltimaVenta { get; set; }
    public DateTime UltimaActividadMaquina { get; set; }
    public DateTime FinReporte { get; set; }

    /// <summary>
    /// Fechas de las ventas del período del template. Se popula en el análisis eager
    /// (AnalizarMaquinaEnPeriodo) para que el gráfico de Ventas Diarias represente
    /// exactamente las ventas del template sin depender del timeline lazy.
    /// </summary>
    public List<DateTime> FechasVentas { get; set; } = new();

    // =============================================
    // MÉTRICAS DE STOCKOUT
    // =============================================
    public bool PosibleQuiebre { get; set; }
    public double HorasSinStock { get; set; }
    public double DiasSinStock => HorasSinStock / 14.0;

    // =============================================
    // VELOCIDAD REAL
    // =============================================
    public int StockInicial { get; set; }
    public int StockActual { get; set; }
    public int CantidadVendida { get; set; }
    public int FillPct { get; set; } = -1;
    public decimal? DiasHastaStockout { get; set; }
    public bool EsDeadSlot { get; set; }
    public double HorasActivas { get; set; }
    public decimal VelocidadPorHora { get; set; }
    public decimal VelocidadDiaria => VelocidadPorHora * 14;

    // =============================================
    // COSTO DE OPORTUNIDAD
    // =============================================
    public decimal PrecioPromedioVenta { get; set; }
    public decimal GananciaPromedio { get; set; }
    public decimal DineroPerdidoEstimado { get; set; }
    public decimal GananciaPerdidaEstimada { get; set; }

    // =============================================
    // HELPERS PARA UI
    // =============================================
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
        _ => "bg-light text-muted"
    };
}

/// <summary>
/// DTO para el timeline lazy de un slot específico.
/// Devuelve las fechas de venta bajo demanda cuando el usuario interactúa con el scrubber.
/// </summary>
public class SlotTimelineDto
{
    public int MaquinaId { get; set; }
    public string NumeroSlot { get; set; } = string.Empty;
    public int? ProductoId { get; set; }
    public List<DateTime> FechasVentas { get; set; } = new();
}
