namespace VendingManager.Shared.Enums;

/// <summary>
/// Estado del ciclo de vida de un AccountingPeriod.
/// - Abierto (0): Período activo, se pueden agregar/modificar operaciones.
/// - Cerrado (1): Período cerrado, solo lectura. Estado terminal.
/// </summary>
public enum AccountingPeriodEstado
{
    /// <summary>Período activo, se pueden agregar/modificar operaciones.</summary>
    Abierto = 0,

    /// <summary>Período cerrado, solo lectura. Estado terminal.</summary>
    Cerrado = 1
}
