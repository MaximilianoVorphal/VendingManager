namespace VendingManager.Core.Configuration;

public class VendingConfig
{
    public DateTime CajaStartDate { get; set; } = new DateTime(2025, 12, 18);

    public int TransbankFee { get; set; } = 80;

    public int RotacionStockMinimoDias { get; set; } = 30;

    public int RotacionUmbralCritico { get; set; } = 7;
}