namespace VendingManager.Core.Entities;

public class ConfiguracionSlot
{
    public int Id { get; set; }

    public int MaquinaId { get; set; }
    public Maquina Maquina { get; set; } = null!;

    public string NumeroSlot { get; set; } = string.Empty; // Ej: "10", "A1"

    public int? ProductoId { get; set; }
    public Producto? Producto { get; set; }

    public int StockActual { get; set; } = 0;
    public int CapacidadMaxima { get; set; } = 10; // Valor por defecto

    public decimal PrecioVenta { get; set; } // Ej: 950
}
