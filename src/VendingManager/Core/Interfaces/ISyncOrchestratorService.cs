using System;
using System.Threading.Tasks;

namespace VendingManager.Core.Interfaces
{
    public interface ISyncOrchestratorService
    {
        Task<string> SincronizarDesdePortal(int maquinaId, DateTime? fechaLimite = null);
    }
}
