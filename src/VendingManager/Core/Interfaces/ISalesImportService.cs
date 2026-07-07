using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VendingManager.Shared.DTOs;

namespace VendingManager.Core.Interfaces
{
    public interface ISalesImportService
    {
        Task<string> ImportarVentasMaquina(Stream fileStream, string nombreArchivo, DateTime? fechaLimite = null, string? maquinaIdEsperado = null);
        Task ImportarTransbank(Stream fileStream, string nombreArchivo, DateTime? fechaLimite = null);

        /// <summary>
        /// Importa ventas desde los datos JSON de la API de Ourvend (sin archivo Excel).
        /// Aplica los mismos offsets horarios y deduplicación que ImportarVentasMaquina.
        /// </summary>
        Task<string> ImportarVentasDesdeJson(List<SalesReportRowDto> rows, DateTime? fechaLimite = null, string? maquinaIdEsperado = null);
    }
}
