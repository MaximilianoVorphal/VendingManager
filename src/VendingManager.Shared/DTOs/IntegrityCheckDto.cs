namespace VendingManager.Shared.DTOs;

public class IntegrityCheckResultDto
{
    public string CheckType { get; set; } = string.Empty;
    public CheckSeverity Severity { get; set; }
    public List<IntegrityCheckDetailDto> DetailEntries { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

public class IntegrityCheckDetailDto
{
    public int? RendicionId { get; set; }
    public int? TransferenciaId { get; set; }
    public int? CompraId { get; set; }
    public string? Trabajador { get; set; }
    public decimal? MontoEntregado { get; set; }
    public decimal? MontoRecibido { get; set; }
    public decimal? SaldoADevolver { get; set; }
    public decimal? MontoTotal { get; set; }
    public decimal? Diferencia { get; set; }
    public string Mensaje { get; set; } = string.Empty;
}

public enum CheckSeverity
{
    Error,
    Warn,
    Info
}
