namespace VendingManager.Shared.Enums;

/// <summary>
/// Estado del ciclo de vida de una OrdenCarga.
/// - Borrador (0): Creada sin descontar stock. Se puede editar y confirmar.
/// - Pendiente (1): Stock descontado, en ruta. Requiere finalización con retornos.
/// - Finalizada (2): Cerrada con retornos procesados e inventario actualizado.
/// </summary>
public enum EstadoOrdenCarga
{
    /// <summary>Orden en edición, sin impacto en inventario.</summary>
    Borrador = 0,

    /// <summary>Stock descontado, orden en ejecución.</summary>
    Pendiente = 1,

    /// <summary>Orden cerrada, retornos procesados.</summary>
    Finalizada = 2
}
