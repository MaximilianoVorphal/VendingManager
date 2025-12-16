namespace VendingManager.Core.DTOs
{
    public class DashboardStats
    {
        public PeriodoStats Hoy { get; set; } = new();
        public PeriodoStats Semana { get; set; } = new();
        public PeriodoStats Mes { get; set; } = new();
    }

    public class PeriodoStats
    {
        public decimal VentaTotal { get; set; }
        public decimal PagadoTB { get; set; }
        public decimal Pendiente { get; set; }
        public int CantidadVentas { get; set; }
    }
}
