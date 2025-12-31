namespace VendingManager.Core.DTOs
{
    public class CajaResumenDto
    {
        public decimal SaldoAnterior { get; set; }
        public decimal IngresosVentas { get; set; }
        public decimal GastosOperativos { get; set; }
        public decimal AportesExtra { get; set; }
        public decimal SaldoFinal { get; set; }
        public decimal UtilidadTotal { get; set; }
        public decimal GastosMercaderia { get; set; }
        public bool IsLocked { get; set; }
    }
}
