namespace VendingManager.Shared.DTOs
{
    public class ProductoSimpleDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public int StockBodega { get; set; }
        public decimal CostoPromedio { get; set; }
        public decimal PrecioVenta { get; set; }
    }
}
