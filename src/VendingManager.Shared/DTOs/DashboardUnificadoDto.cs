namespace VendingManager.Shared.DTOs;

/// <summary>
/// DTO raíz del dashboard financiero unificado.
/// Contiene pipeline, alertas y actividad reciente.
/// </summary>
public class DashboardUnificadoDto
{
    public PipelineFinancieroDto Pipeline { get; set; } = new();
    public AlertaConsolidadaDto Alertas { get; set; } = new();
    public List<ActividadRecienteDto> Actividad { get; set; } = new();
}

/// <summary>
/// Pipeline financiero horizontal: Ventas → Transferencias → Compras+Gastos → Conciliación.
/// </summary>
public class PipelineFinancieroDto
{
    /// <summary>Monto total de ventas del mes actual.</summary>
    public decimal VentasMes { get; set; }

    /// <summary>Cantidad de ventas del mes.</summary>
    public int CantidadVentasMes { get; set; }

    /// <summary>Sumatoria de transferencias con estado Pendiente o EnUso.</summary>
    public decimal TransferenciasActivas { get; set; }

    /// <summary>Cantidad de transferencias activas.</summary>
    public int CantidadTransferencias { get; set; }

    /// <summary>Monto total de compras vinculadas a transferencias activas.</summary>
    public decimal ComprasVinculadas { get; set; }

    /// <summary>Monto total de gastos recurrentes pendientes del mes.</summary>
    public decimal GastosVinculados { get; set; }

    /// <summary>
    /// Conciliación = TransferenciasActivas - ComprasVinculadas - GastosVinculados.
    /// Valor positivo indica fondos por rendir; negativo indica sobregasto.
    /// </summary>
    public decimal Conciliacion { get; set; }

    /// <summary>True si Conciliacion >= 0, false si es negativa.</summary>
    public bool IsPositiva { get; set; }
}

/// <summary>
/// Alertas consolidadas de los 4 dominios: stock, transferencias, gastos, compras.
/// </summary>
public class AlertaConsolidadaDto
{
    /// <summary>Total de alertas no resueltas (suma de todos los tipos).</summary>
    public int Total { get; set; }

    /// <summary>Lista de alertas individuales.</summary>
    public List<ItemAlertaDto> Items { get; set; } = new();
}

/// <summary>
/// Una alerta individual con tipo, severidad y enlace a la página de resolución.
/// </summary>
public class ItemAlertaDto
{
    /// <summary>
    /// Tipo de alerta: stock-critico | transferencias-sin-rendir | gastos-fijos-no-registrados | compras-sin-factura.
    /// </summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>Mensaje legible para el usuario.</summary>
    public string Mensaje { get; set; } = string.Empty;

    /// <summary>Severidad: danger | warning | info.</summary>
    public string Severidad { get; set; } = "info";

    /// <summary>URL a la página de resolución de la alerta.</summary>
    public string LinkUrl { get; set; } = string.Empty;
}

/// <summary>
/// Entrada de actividad reciente en la línea de tiempo unificada.
/// </summary>
public class ActividadRecienteDto
{
    /// <summary>Fecha y hora del evento.</summary>
    public DateTime Fecha { get; set; }

    /// <summary>
    /// Tipo de actividad: venta | transferencia | compra | movimiento_caja.
    /// </summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>Monto asociado (puede ser 0 para movimientos sin monto).</summary>
    public decimal Monto { get; set; }

    /// <summary>Descripción breve del evento.</summary>
    public string Descripcion { get; set; } = string.Empty;

    /// <summary>URL al detalle del evento.</summary>
    public string LinkUrl { get; set; } = string.Empty;

    /// <summary>
    /// ID de la máquina asociada (solo para tipo=venta, null para otros tipos).
    /// </summary>
    public int? MaquinaId { get; set; }
}