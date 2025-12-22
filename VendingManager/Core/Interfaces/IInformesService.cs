using System.Collections.Generic;
using System.Threading.Tasks;
using VendingManager.Core.Entities;

namespace VendingManager.Core.Interfaces
{
    public interface IInformesService
    {
        Task<Informe> SubirInformeAsync(Informe informe);
        Task<List<Informe>> ObtenerTodosSinContenidoAsync();
        Task<Informe?> ObtenerPorIdAsync(int id);
        Task EliminarInformeAsync(int id);
    }
}
