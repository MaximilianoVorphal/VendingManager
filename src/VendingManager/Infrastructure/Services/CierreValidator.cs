namespace VendingManager.Infrastructure.Services;

using VendingManager.Core.Entities;
using VendingManager.Shared.Enums;

/// <summary>
/// Shared close-gate validation for RendicionService.CerrarAsync and
/// ContabilidadService.ClosePeriodoAsync.
///
/// SDD endurecimiento-dominio — Slice 2.
/// Static class (stateless) — avoids DI changes to RendicionService.
///
/// Canonical behavior sourced from ClosePeriodoAsync:
///   G1: all Transferencias must be Verificada
///   G2: all Compras must be Verificada
///   G3: saldo = 0 (gastos filtered by EsGastoOperativoReal)
///   Auto: EnUso transfers with linked compras/gastos → Conciliado
///   G4: all Transferencias must be Conciliado after auto step
///
/// Caller is responsible for pre-filtering gastos via
/// CategoriasGasto.EsGastoOperativoReal and for saving changes
/// after Validate returns (auto-conciliation mutates transfer states).
/// </summary>
public static class CierreValidator
{
    /// <summary>
    /// Validates that a rendición or accounting period can be closed.
    /// Mutates transferencia states during auto-conciliation.
    /// Caller must persist changes via SaveChangesAsync after successful validation.
    /// </summary>
    /// <param name="transfers">Transferencias linked to the entity being closed.</param>
    /// <param name="gastos">
    /// Gastos to include in saldo calculation.
    /// Caller MUST pre-filter via CategoriasGasto.EsGastoOperativoReal
    /// to exclude structural gastos (RETIRO_CAPITAL, DEVOLUCION_RENDICION).
    /// </param>
    /// <param name="devuelto">Total devuelto amount already registered.</param>
    /// <param name="entityLabel">Human-readable label for error messages (e.g., "rendición", "período").</param>
    /// <returns>Validation result.</returns>
    public static CierreValidationResult Validate(
        IReadOnlyList<Transferencia> transfers,
        IReadOnlyList<MovimientoCaja> gastos,
        decimal devuelto,
        string entityLabel)
    {
        // ── G1: all Transferencias must be Verificada ──
        var unverifiedTransfers = transfers
            .Where(t => !t.Verificada)
            .ToList();
        if (unverifiedTransfers.Count != 0)
        {
            return new CierreValidationResult(false,
                $"No se puede cerrar la {entityLabel}. Hay {unverifiedTransfers.Count} transferencia(s) sin verificar. " +
                "Verificá todos los comprobantes antes de cerrar.");
        }

        // ── G2: all Compras must be Verificada ──
        var unverifiedCompras = transfers
            .SelectMany(t => t.Compras)
            .Where(c => !c.Verificada)
            .ToList();
        if (unverifiedCompras.Count != 0)
        {
            return new CierreValidationResult(false,
                $"No se puede cerrar la {entityLabel}. Hay {unverifiedCompras.Count} compra(s) sin verificar. " +
                "Verificá todos los comprobantes antes de cerrar.");
        }

        // ── G3: saldo must be 0 ──
        var totalTransferido = transfers.Sum(t => t.Monto);
        var totalCompras = transfers.SelectMany(t => t.Compras).Sum(c => c.MontoTotal);
        var totalGastos = gastos.Sum(g => Math.Abs(g.Monto));
        var diferencia = totalTransferido - totalCompras - totalGastos;
        var saldoADevolver = diferencia - devuelto;

        if (saldoADevolver != 0)
        {
            return new CierreValidationResult(false,
                $"No se puede cerrar la {entityLabel}. El saldo a devolver es ${saldoADevolver:N2}. " +
                "Registrá una devolución antes de cerrar.");
        }

        // ── Auto-conciliation ──
        // Conciliate transfers that have linked compras OR when the entity has gastos
        // (ClosePeriodoAsync canonical: t.Rendicion?.Gastos?.Count > 0 covers the latter).
        var hasGastos = gastos.Count > 0;
        foreach (var t in transfers)
        {
            if (t.Estado != TransferenciaEstado.Conciliado)
            {
                var hasLinkedItems = t.Compras.Count > 0 || hasGastos;
                if (hasLinkedItems)
                {
                    t.Estado = TransferenciaEstado.Conciliado;
                }
            }
        }

        // ── G4: all Transferencias must be Conciliado after auto step ──
        var nonConciliated = transfers
            .Where(t => t.Estado != TransferenciaEstado.Conciliado)
            .ToList();

        if (nonConciliated.Count != 0)
        {
            return new CierreValidationResult(false,
                $"No se puede cerrar la {entityLabel}. Hay {nonConciliated.Count} transferencia(s) no conciliada(s). " +
                "Vinculá compras o gastos a cada transferencia antes de cerrar.");
        }

        return new CierreValidationResult(true, null);
    }
}

/// <summary>
/// Result of CierreValidator.Validate.
/// </summary>
public record CierreValidationResult(bool Valid, string? ErrorMessage);
