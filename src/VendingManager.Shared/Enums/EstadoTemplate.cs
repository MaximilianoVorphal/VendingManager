namespace VendingManager.Shared.Enums;

/// <summary>
/// Estado del ciclo de vida de un TemplateRecarga.
/// - Pendiente (0): Template en desarrollo, slots siendo configurados. No feed stock-critico.
/// - Activo (1): Template completado. Es la fuente para stock-critico (más reciente por máquina).
/// - Terminado (2): Cycle cerrado. Histórico, no usado para stock-critico.
/// </summary>
public enum EstadoTemplate
{
    /// <summary>Template en desarrollo, slots siendo configurados. No feed stock-critico.</summary>
    Pendiente = 0,

    /// <summary>Template completado. Fuente para stock-critico (más reciente por máquina).</summary>
    Activo = 1,

    /// <summary>Cycle cerrado. Histórico, no usado para stock-critico.</summary>
    Terminado = 2
}