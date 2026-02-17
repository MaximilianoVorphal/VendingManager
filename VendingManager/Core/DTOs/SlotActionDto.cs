namespace VendingManager.Core.DTOs
{
    public class SlotActionDto
    {
        public int SlotId { get; set; }
        public string ActionType { get; set; } = "REFILL";
        public int Cantidad { get; set; }
        public int? NewProductoId { get; set; }
        public decimal? NewPrecioVenta { get; set; }
    }
}
