namespace VendingManager.Shared.Enums;

/// <summary>
/// Estado del ciclo de vida de una Transferencia.
/// - Pendiente (0): Transferencia creada, aún no vinculada a ninguna compra.
/// - EnUso (1): Al menos una compra ha sido vinculada a esta transferencia.
/// - Conciliado (2): Transferencia cerrada con rendición cuadrada. Estado terminal.
/// </summary>
public enum TransferenciaEstado
{
    /// <summary>Transferencia creada, aún no vinculada a ninguna compra.</summary>
    Pendiente = 0,

    /// <summary>Al menos una compra ha sido vinculada a esta transferencia.</summary>
    EnUso = 1,

    /// <summary>Transferencia cerrada con rendición cuadrada. Estado terminal.</summary>
    Conciliado = 2
}