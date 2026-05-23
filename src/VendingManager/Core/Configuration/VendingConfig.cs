namespace VendingManager.Core.Configuration;

public class VendingConfig
{
    public DateTime CajaStartDate { get; set; } = new DateTime(2025, 12, 18);

    public int TransbankFee { get; set; } = 80;

    public int RotacionStockMinimoDias { get; set; } = 30;

    public int RotacionUmbralCritico { get; set; } = 7;

    /// <summary>
    /// Ruta absoluta donde se guardan las imágenes de facturas.
    /// Si es null o vacío, se usa WebRootPath/wwwroot como fallback.
    /// Ejemplo en Docker: "/var/uploads/vendingmanager"
    /// </summary>
    public string? FacturaUploadPath { get; set; }

    /// <summary>
    /// Cuando es true, stock-critico consulta SnapshotSlots del template Terminado más reciente
    /// en lugar de ConfiguracionSlots. Permite transición gradual al nuevo flujo.
    /// </summary>
    public bool UseTemplateInventoryForStockCritico { get; set; } = false;
}