namespace VendingManager.Shared.DTOs
{
    public class CajaResumenDto
    {
        public decimal SaldoAnterior { get; set; }
        public decimal IngresosVentas { get; set; }
        public decimal GastosOperativos { get; set; }
        public decimal AportesExtra { get; set; }
        public decimal SaldoFinal { get; set; }
        // Restore missing fields used in Service/Razor
        public decimal UtilidadTotal { get; set; }
        public decimal GastosMercaderia { get; set; }

        public decimal CostoTransbank { get; set; }
        public int CantidadVentasTransbank { get; set; }
        public decimal TotalCostoVenta { get; set; }
        public decimal Mermas { get; set; }          // [NEW]
        public decimal GastosVariables { get; set; } // [NEW] Logistica
        public decimal GastosFijos { get; set; }     // [NEW] Estructurales
        public decimal UtilidadOperacional { get; set; } // Unificado por SDD consolidacion-financiera
        public decimal UtilidadNeta { get; set; }    // Real Net
        public bool IsLocked { get; set; }

        // Computed property for test compatibility
        public decimal TotalIngresos => SaldoAnterior + IngresosVentas;
    }
}
