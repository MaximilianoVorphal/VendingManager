using System;
using System.Threading.Tasks;

namespace VendingManager.Core.Interfaces
{
    public interface ISyncOrchestratorService
    {
        /// <summary>
        /// Sincroniza ventas desde el portal Ourvend usando el scraper ALT
        /// (inglés, todas las máquinas, orden corregido).
        /// </summary>
        Task<string> SincronizarDesdePortal(int maquinaId, DateTime? fechaLimite = null);
    }
}