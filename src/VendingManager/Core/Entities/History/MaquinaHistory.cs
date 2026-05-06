namespace VendingManager.Core.Entities;

/// <summary>
/// Entidad history para Maquina. Refleja las columnas de Maquina más los campos de auditoría.
/// </summary>
public class MaquinaHistory
{
    public int Id { get; set; }
    public int EntityId { get; set; }
    public string Action { get; set; } = string.Empty;

    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }

    public DateTime Timestamp { get; set; }
    public string Usuario { get; set; } = string.Empty;

    // --- Maquina base columns ---
    public string Nombre { get; set; } = string.Empty;
    public string IdInternoMaquina { get; set; } = string.Empty;
    public string Ubicacion { get; set; } = string.Empty;
    public string CodigoTerminalPos { get; set; } = string.Empty;
}
