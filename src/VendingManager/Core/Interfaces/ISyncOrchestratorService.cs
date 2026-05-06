using System;
using System.Threading.Tasks;

namespace VendingManager.Core.Interfaces
{
    public interface ISyncOrchestratorService
    {
        Task<string> SincronizarDesdePortal(int maquinaId, DateTime? fechaLimite = null);

        /// <summary>
        /// Versión alternativa: usa el scraper ALT (inglés, todas las máquinas, orden corregido)
        /// </summary>
        Task<string> SincronizarDesdePortalAlt(int maquinaId, DateTime? fechaLimite = null);
    }
}
