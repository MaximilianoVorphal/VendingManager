using System;
using System.Threading;
using System.Threading.Tasks;
using VendingManager.Shared.DTOs;

namespace VendingManager.Core.Interfaces
{
    public interface ISyncOrchestratorService
    {
        /// <summary>
        /// Sincroniza ventas desde el portal Ourvend usando el scraper ALT
        /// (inglés, todas las máquinas, orden corregido).
        /// </summary>
        Task<string> SincronizarDesdePortal(int maquinaId, DateTime? fechaLimite = null);

        /// <summary>
        /// Sincroniza ventas desde Ourvend via API pura (JSON, sin Playwright).
        /// </summary>
        Task<string> SincronizarDesdePortalApi(int maquinaId, DateTime? fechaLimite = null);

        /// <summary>
        /// Sincroniza ventas para una ventana de fechas explícita via API JSON,
        /// retornando un resultado estructurado que el llamador (AutomatedReportService)
        /// puede mapear a <c>PollOutcome</c>. NO actualiza LastSyncTracker — eso es
        /// responsabilidad del llamador.
        /// </summary>
        Task<SyncResult> SincronizarDesdePortalApi(DateTime desde, DateTime hasta,
            CancellationToken cancellationToken = default);
    }
}