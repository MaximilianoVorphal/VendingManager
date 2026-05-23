namespace VendingManager.Shared.Enums;

/// <summary>
/// Estado del ciclo de vida de un TemplateRecarga.
/// - Pendiente (0): Template en desarrollo, slots siendo configurados. No feed stock-critico.
/// - Terminado (2): Ciclo cerrado. Histórico. latest Terminado template per machine feeds stock-critico.
/// </summary>
public enum EstadoTemplate
{
    /// <summary>Template en desarrollo, slots siendo configurados. No feed stock-critico.</summary>
    Pendiente = 0,

    /// <summary>Ciclo cerrado. Histórico, no usado para stock-critico.</summary>
    Terminado = 2
}