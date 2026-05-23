namespace VendingManager.Shared.Enums;

/// <summary>
/// Estado del ciclo de vida de una Rendición.
/// - Abierta (0): Rendición en curso, pueden linkearse compras y gastos.
/// - Cerrada (1): Rendición finalizada, cálculos de conciliación bloqueados.
/// </summary>
public enum RendicionEstado
{
    /// <summary>Rendición en curso, pueden linkearse compras y gastos.</summary>
    Abierta = 0,

    /// <summary>Rendición finalizada, cálculos de conciliación bloqueados.</summary>
    Cerrada = 1
}