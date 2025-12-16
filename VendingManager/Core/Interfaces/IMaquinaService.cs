
namespace VendingManager.Core.Interfaces
{
    public interface IMaquinaService
    {
        Task<List<Maquina>> GetMaquinasAsync();
        Task<Maquina?> GetMaquinaAsync(int id);
        Task<Maquina> CreateMaquinaAsync(Maquina maquina);
        Task UpdateMaquinaAsync(int id, Maquina maquina);
        Task DeleteMaquinaAsync(int id);
    }
}
