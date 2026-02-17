
namespace VendingManager.Core.Interfaces
{
    public interface IMaquinaService
    {
        Task<List<Maquina>> GetMaquinasAsync();
        Task<Maquina?> GetMaquinaAsync(int id);
        Task<Maquina> CreateMaquinaAsync(Maquina maquina);
        Task UpdateMaquinaAsync(int id, Maquina maquina);
        Task<List<ConfiguracionSlotDto>> GetSlotsAsync(int maquinaId);
        Task UpdateSlotAsync(ConfiguracionSlotDto slot);
        Task ProcesarMovimientosLoteAsync(int maquinaId, List<SlotActionDto> acciones);
        Task DeleteMaquinaAsync(int id);
    }
}
