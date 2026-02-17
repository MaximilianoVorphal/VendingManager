using System;

namespace VendingManager.Shared.DTOs
{
    public class ValorizacionStockDto
    {
        public decimal ValorBodega { get; set; }
        public decimal ValorMaquinas { get; set; }
        public decimal ValorTotal => ValorBodega + ValorMaquinas;
        public DateTime FechaCalculo { get; set; } = DateTime.Now;
    }
}
