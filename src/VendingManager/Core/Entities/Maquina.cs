
namespace VendingManager.Core.Entities;

public class Maquina
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;

    // Identificador del Excel de la MÁQUINA (Ej: "2410280023")
    public string IdInternoMaquina { get; set; } = string.Empty;

    public string Ubicacion { get; set; } = string.Empty;

    // Identificador del Excel de TRANSBANK (Ej: "SIV01099")
    public string CodigoTerminalPos { get; set; } = string.Empty;

    // Zona logística para optimización de rutas (opcional)
    public int? ZonaLogisticaId { get; set; }
    public ZonaLogistica? Zona { get; set; }

    /// <summary>
    /// Delta in hours from the machine's reported timestamp to Chilean CLT
    /// (e.g., -11 for machines reporting in UTC+7, so 7 + (-11) = -4 = CLT).
    /// Null means "use the appsettings default" (<see cref="Configuration.VendingConfig.DefaultTimezoneOffsetHours"/>).
    /// </summary>
    public int? TimezoneOffsetHours { get; set; }

    // Relación con los slots configurados
    public List<ConfiguracionSlot> Slots { get; set; } = new();
}
