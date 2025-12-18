namespace VendingManager.Core.Entities;

public class Venta
{
    public int Id { get; set; }
    public DateTime FechaHora { get; set; }

    public DateTime FechaLocal { get; set; } // Hora "Local" ajustada (ej. -11h)

    public int MaquinaId { get; set; }
    public Maquina Maquina { get; set; } = null!;

    public int? ProductoId { get; set; } // Puede ser nulo si no configuraste el slot
    public Producto? Producto { get; set; }

    public string NumeroSlot { get; set; } = string.Empty;
    public decimal PrecioVenta { get; set; }
    public decimal CostoVenta { get; set; } = 0; // Costo histórico al momento de la venta
    public string IdOrdenMaquina { get; set; } = string.Empty; // Para evitar duplicados

    public bool Pagado { get; set; } = false; // Por defecto es falso
}
