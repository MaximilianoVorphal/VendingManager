using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VendingManager.Core.Entities;

public class MovimientoCaja
{
    [Key]
    public int Id { get; set; }

    public DateTime Fecha { get; set; } = DateTime.Now;

    [Required]
    public string Descripcion { get; set; } = ""; // Ej: "Compra Bebidas", "Retiro Ganancia"

    // Usaremos: Positivo (+) para dinero que entra, Negativo (-) para dinero que sale
    [Column(TypeName = "decimal(18,2)")]
    public decimal Monto { get; set; }

    public string Tipo { get; set; } = "GASTO"; // "GASTO", "APORTE", "RETIRO"
    public string Categoria { get; set; } = "GENERAL"; // "MERCADERÍA", "LOGÍSTICA", etc.

    public string? ImagenPath { get; set; }

    // Campos opcionales para Mermas / Gestión de Stock
    public int? ProductoId { get; set; }
    public int Cantidad { get; set; } = 0;

    // Vinculación con Orden de Carga
    public int? OrdenCargaId { get; set; }
    // public OrdenCarga? OrdenCarga { get; set; } // Optional navigation, helpful for EF

    // Vinculación con Compra (trazabilidad bidireccional)
    public int? CompraId { get; set; }

    // Vinculación con Gasto Recurrente (evita duplicados por mes)
    public int? GastoRecurrenteId { get; set; }

    // Vinculación con Rendicion (nullable FK — sin ruptura de cambios existentes)
    public int? RendicionId { get; set; }
    public Rendicion? Rendicion { get; set; }

    [MaxLength(200)]
    public string? Trabajador { get; set; }
}
