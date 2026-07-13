
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
    // Relación con los slots configurados
    public List<ConfiguracionSlot> Slots { get; set; } = new();
}
