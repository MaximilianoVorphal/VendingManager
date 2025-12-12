namespace VendingManager.Models
{
    public class Producto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty; // Ej: "Papas Fritas"
        public string SKU { get; set; } = string.Empty;
        public decimal CostoPromedio { get; set; } // Lo calcularemos más adelante
    }
}