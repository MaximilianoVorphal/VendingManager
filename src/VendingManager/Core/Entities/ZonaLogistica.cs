namespace VendingManager.Core.Entities;

public class ZonaLogistica
{
    public int Id { get; set; }

    public string Nombre { get; set; } = string.Empty; // Ej: "Zona Norte"

    // Costo fijo estimado de enviar un vehículo a la zona (bencina, peajes, tiempo)
    public decimal CostoBaseViaje { get; set; }
}
