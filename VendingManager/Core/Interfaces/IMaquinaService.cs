
namespace VendingManager.Core.Interfaces
{
    public interface IMaquinaService
    {
        Task<List<Maquina>> GetMaquinasAsync();
        Task<Maquina?> GetMaquinaAsync(int id);
        Task<Maquina> CreateMaquinaAsync(Maquina maquina);
        Task UpdateMaquinaAsync(int id, Maquina maquina);
        Task<List<DTOs.ConfiguracionSlotDto>> GetSlotsAsync(int maquinaId);
        Task UpdateSlotAsync(DTOs.ConfiguracionSlotDto slot);
        Task DeleteMaquinaAsync(int id);
    }
}
