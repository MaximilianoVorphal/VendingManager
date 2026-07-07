namespace VendingManager.Shared.DTOs;

/// <summary>
/// DTO que representa la matriz de conciliación global multi-período.
/// Contiene semanas como columnas, proveedores agrupados como filas,
/// resumen KPI y saldo inicial de arrastre.
/// </summary>
public class ConciliacionGlobalDto
{
    /// <summary>Columnas de la matriz: una por período semanal.</summary>
    public List<SemanaColumnaDto> Semanas { get; set; } = new();

    /// <summary>Filas de la matriz: un proveedor agrupado por slug.</summary>
    public List<FilaProveedorDto> Proveedores { get; set; } = new();

    /// <summary>Resumen KPI con totales del período.</summary>
    public ResumenConciliacionDto Resumen { get; set; } = new();

    /// <summary>Saldo arrastrado de períodos anteriores al rango consultado.</summary>
    public decimal SaldoInicial { get; set; }
}

/// <summary>
/// Columna de la matriz: representa una semana/período contable.
/// </summary>
public class SemanaColumnaDto
{
    public int Id { get; set; }
    public int Numero { get; set; }
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    public bool EstaCerrada { get; set; }
    public decimal TotalTransferido { get; set; }
    public decimal TotalCompras { get; set; }
    public decimal TotalGastos { get; set; }
}

/// <summary>
/// Fila de la matriz: representa un proveedor con sus celdas por semana.
/// </summary>
public class FilaProveedorDto
{
    public string ProveedorSlug { get; set; } = string.Empty;
    public string ProveedorNombre { get; set; } = string.Empty;
    public List<CeldaSemanaDto> Celdas { get; set; } = new();
    public decimal TotalProveedor { get; set; }
}

/// <summary>
/// Celda individual en la matriz: monto del proveedor en una semana específica.
/// </summary>
public class CeldaSemanaDto
{
    public int SemanaId { get; set; }
    public decimal Monto { get; set; }
    public string Estado { get; set; } = "Vacio";  // Pendiente|Justificado|Observado|Vacio
    public List<ComprobanteItemDto> Comprobantes { get; set; } = new();
}

/// <summary>
/// Comprobante (compra/gasto) individual dentro de una celda.
/// </summary>
public class ComprobanteItemDto
{
    public int Id { get; set; }
    public string Tipo { get; set; } = string.Empty;  // "Compra"
    public string NumeroDocumento { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public decimal Monto { get; set; }
    public bool Verificada { get; set; }
    public string? Proveedor { get; set; }
}

/// <summary>
/// Resumen KPI de la matriz global.
/// </summary>
public class ResumenConciliacionDto
{
    public decimal TotalTransferencias { get; set; }
    public decimal TotalCompras { get; set; }
    public decimal TotalGastos { get; set; }
    public decimal SaldoTotal { get; set; }
    public int SemanasTotales { get; set; }
    public int SemanasVerificadas { get; set; }
}
