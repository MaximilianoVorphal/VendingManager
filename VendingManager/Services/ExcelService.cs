using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using VendingManager.Data;
using VendingManager.Models;
using System.Data;
using System.IO;

namespace VendingManager.Services
{
    public class ExcelService
    {
        private readonly ApplicationDbContext _context;

        public ExcelService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task ImportarVentasMaquina(Stream fileStream, string nombreArchivo)
        {
            try
            {
                // 1. CREAR EL LECTOR (Detecta .xls o .xlsx automáticamente)
                using (var reader = ExcelReaderFactory.CreateReader(fileStream))
                {
                    // Convertimos el Excel a una Tabla en memoria
                    var conf = new ExcelDataSetConfiguration
                    {
                        ConfigureDataTable = _ => new ExcelDataTableConfiguration
                        {
                            UseHeaderRow = true // Le decimos que la Fila 1 son títulos
                        }
                    };

                    var dataSet = reader.AsDataSet(conf);
                    var tabla = dataSet.Tables[0]; // Trabajamos con la primera hoja

                    Console.WriteLine($"📊 EXCEL CARGADO: {tabla.Rows.Count} filas encontradas.");

                    // 2. MAPEO DE COLUMNAS (Buscamos el índice de la columna por su nombre en Español)
                    // Si el chino cambia el nombre, solo ajustamos aquí.
                    int colId = tabla.Columns.IndexOf("Número de máquina");
                    int colSlot = tabla.Columns.IndexOf("carril de carga");
                    int colPrecio = tabla.Columns.IndexOf("precio unitario");
                    int colTiempo = tabla.Columns.IndexOf("tiempo de la máquina"); // Ojo: a veces es "time"
                    int colOrden = tabla.Columns.IndexOf("número de serie");

                    // Validación: Si no encuentra la columna "Número de máquina", avisamos
                    if (colId == -1)
                    {
                        Console.WriteLine("🔥 ERROR: No encuentro la columna 'Número de máquina'. Revisa los títulos.");
                        return;
                    }

                    int guardados = 0;
                    int duplicados = 0;

                    // 3. RECORRER FILAS
                    foreach (DataRow row in tabla.Rows)
                    {
                        // Leemos los datos usando los índices
                        string machineId = row[colId]?.ToString() ?? "";

                        if (string.IsNullOrEmpty(machineId)) continue;

                        string orderNumber = colOrden != -1 ? row[colOrden]?.ToString() ?? "" : "";

                        // Parsear números y fechas con seguridad
                        decimal precio = 0;
                        if (colPrecio != -1) decimal.TryParse(row[colPrecio]?.ToString(), out precio);

                        int slot = 0;
                        if (colSlot != -1) int.TryParse(row[colSlot]?.ToString(), out slot);

                        DateTime fecha = DateTime.MinValue;
                        if (colTiempo != -1) DateTime.TryParse(row[colTiempo]?.ToString(), out fecha);

                        // --- LÓGICA DE GUARDADO ---

                        // Evitar duplicados
                        if (await _context.Ventas.AnyAsync(v => v.IdOrdenMaquina == orderNumber))
                        {
                            duplicados++;
                            continue;
                        }

                        // Buscar máquina en BD
                        var maquina = await _context.Maquinas.FirstOrDefaultAsync(m => m.IdInternoMaquina == machineId);

                        if (maquina != null)
                        {
                            _context.Ventas.Add(new Venta
                            {
                                FechaHora = fecha,
                                PrecioVenta = precio,
                                NumeroSlot = slot,
                                IdOrdenMaquina = orderNumber,
                                MaquinaId = maquina.Id,
                                Pagado = false
                            });
                            guardados++;
                        }
                    }

                    await _context.SaveChangesAsync();
                    Console.WriteLine($"✅ IMPORTACIÓN FINALIZADA: {guardados} nuevas, {duplicados} repetidas.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ERROR CRÍTICO: {ex.Message}");
            }
        }

        // Método dummy para que no falle el compilador si alguien lo llama
        public async Task ImportarTransbank(Stream s, string n) { await Task.CompletedTask; }
    }
}