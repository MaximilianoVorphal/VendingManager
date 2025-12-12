namespace VendingManager.Models
{
    public class Venta
    {
        public int Id { get; set; }
        public DateTime FechaHora { get; set; }

        public int MaquinaId { get; set; }
        public Maquina Maquina { get; set; } = null!;

        public int? ProductoId { get; set; } // Puede ser nulo si no configuraste el slot
        public Producto? Producto { get; set; }

        public int NumeroSlot { get; set; }
        public decimal PrecioVenta { get; set; }
        public string IdOrdenMaquina { get; set; } = string.Empty; // Para evitar duplicados

        public bool Pagado { get; set; } = false; // Por defecto es falso
    }
}