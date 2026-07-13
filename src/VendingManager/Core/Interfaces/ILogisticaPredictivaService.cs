using System.Collections.Generic;
using System.Threading.Tasks;
using VendingManager.Shared.DTOs;

namespace VendingManager.Core.Interfaces
{
    public interface ILogisticaPredictivaService
    {
        Task<List<LogisticaZonaDto>> GetAnalisisZonasAsync(int diasHistorial = 14, int ventanaProyeccionDias = 3);

        /// <summary>
        /// Crea una orden de carga (Estado "PENDIENTE") con los slots críticos de la zona.
        /// Retorna el Id de la orden creada.
        /// </summary>
        Task<int> GenerarOrdenCargaBorradorAsync(int? zonaLogisticaId, int diasHistorial = 14, int ventanaProyeccionDias = 3);
    }
}
