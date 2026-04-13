using System;
using System.IO;
using System.Threading.Tasks;

namespace VendingManager.Core.Interfaces
{
    public interface ISalesImportService
    {
        Task<string> ImportarVentasMaquina(Stream fileStream, string nombreArchivo, DateTime? fechaLimite = null, string? maquinaIdEsperado = null);
        Task ImportarTransbank(Stream fileStream, string nombreArchivo, DateTime? fechaLimite = null);
    }
}
