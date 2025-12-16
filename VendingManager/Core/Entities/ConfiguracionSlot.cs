namespace VendingManager.Core.Entities;

public class ConfiguracionSlot
{
    public int Id { get; set; }

    public int MaquinaId { get; set; }
    public Maquina Maquina { get; set; } = null!;

    public int NumeroSlot { get; set; } // Ej: 58

    public int ProductoId { get; set; }
    public Producto Producto { get; set; } = null!;

    public decimal PrecioVenta { get; set; } // Ej: 950
}
