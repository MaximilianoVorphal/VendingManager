using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ExcelDataReader;
using System.Data;
using VendingManager.Core.Configuration;
using VendingManager.Infrastructure.Data;
using VendingManager.Core.Interfaces;
using VendingManager.Core.Entities;

namespace VendingManager.Infrastructure.Services
{
    public class SalesImportService : ISalesImportService
    {
        private readonly ApplicationDbContext _context;
        private readonly IOptions<VendingConfig> _config;

        public SalesImportService(ApplicationDbContext context, IOptions<VendingConfig> config)
        {
            _context = context;
            _config = config;
        }

        public async Task<string> ImportarVentasMaquina(Stream fileStream, string nombreArchivo, DateTime? fechaLimite = null, string? maquinaIdEsperado = null)
        {
            try
            {
                using (var reader = ExcelReaderFactory.CreateReader(fileStream))
                {
                    var conf = new ExcelDataSetConfiguration
                    {
                        ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
                    };
                    var dataSet = reader.AsDataSet(conf);
                    var tabla = dataSet.Tables[0];

                    Console.WriteLine($"📊 MÁQUINA: Procesando {tabla.Rows.Count} filas...");

                    int colId = tabla.Columns.IndexOf("Machine ID");
                    int colSlot = tabla.Columns.IndexOf("Slot Number");
                    int colPrecio = tabla.Columns.IndexOf("Price");
                    int colTiempo = tabla.Columns.IndexOf("Machine Time");
                    int colServerTime = tabla.Columns.IndexOf("Server time");
                    if (colServerTime == -1) colServerTime = tabla.Columns.IndexOf("Server Time");
                    int colOrden = tabla.Columns.IndexOf("Order Number");

                    if (colId == -1)
                    {
                        Console.WriteLine("🔥 ERROR: No encuentro la columna 'Machine ID'.");
                        return "Error: Formato de archivo incorrecto (Falta Machine ID)";
                    }

                    int guardados = 0;
                    int duplicados = 0;
                    int maquinaNoEncontrada = 0;
                    int filasVacias = 0;
                    int ignoradosPorFecha = 0;
                    int filtradosPorID = 0;

                    foreach (DataRow row in tabla.Rows)
                    {
                        string machineId = row[colId]?.ToString() ?? "";
                        if (string.IsNullOrEmpty(machineId))
                        {
                            filasVacias++;
                            continue;
                        }

                        if (!string.IsNullOrEmpty(maquinaIdEsperado) && machineId.Trim() != maquinaIdEsperado.Trim())
                        {
                            filtradosPorID++;
                            continue;
                        }

                        var maquina = await _context.Maquinas
                            .Include(m => m.Slots)
                            .ThenInclude(s => s.Producto)
                            .FirstOrDefaultAsync(m => m.IdInternoMaquina == machineId);

                        if (maquina == null)
                        {
                            maquinaNoEncontrada++;
                            if (maquinaNoEncontrada <= 5)
                            {
                                Console.WriteLine($"   ⚠️ Máquina NO encontrada en BD: '{machineId}'");
                            }
                            continue;
                        }

                        decimal.TryParse(row[colPrecio]?.ToString(), out decimal precio);
                        string slot = row[colSlot]?.ToString()?.Trim() ?? "";
                        DateTime.TryParse(row[colTiempo]?.ToString(), out DateTime fecha);

                        bool usandingServerTime = false;
                        if (fecha.Year < 2024 && colServerTime != -1)
                        {
                            if (DateTime.TryParse(row[colServerTime]?.ToString(), out DateTime fechaServer))
                            {
                                fecha = fechaServer;
                                usandingServerTime = true;
                            }
                        }

                        string orderNumber = colOrden != -1 ? row[colOrden]?.ToString() ?? "" : "";

                        if (orderNumber.Contains("E+") && double.TryParse(orderNumber, out double dOrder))
                        {
                            orderNumber = dOrder.ToString("F0");
                        }

                        bool esDuplicado = false;

                        if (!string.IsNullOrEmpty(orderNumber) && orderNumber != "0")
                        {
                            esDuplicado = await _context.Ventas.AnyAsync(v =>
                                v.IdOrdenMaquina == orderNumber &&
                                v.MaquinaId == maquina.Id &&
                                v.FechaHora >= fecha.AddHours(-24) && 
                                v.FechaHora <= fecha.AddHours(24)
                            );
                        }
                        else
                        {
                            esDuplicado = await _context.Ventas.AnyAsync(v =>
                                v.MaquinaId == maquina.Id &&
                                v.FechaHora == fecha &&
                                v.NumeroSlot == slot &&
                                v.PrecioVenta == precio);
                        }

                        if (esDuplicado)
                        {
                            duplicados++;
                            continue;
                        }

                        int offset;
                        if (usandingServerTime) 
                        {
                            offset = -14; 
                        }
                        else 
                        {
                            offset = machineId.Trim() == "2410280012" ? 1 : -11;
                        }

                        DateTime fechaLocal = fecha.AddHours(offset);

                        if (fechaLimite.HasValue && fechaLocal > fechaLimite.Value)
                        {
                            ignoradosPorFecha++;
                            continue;
                        }

                        var configSlot = maquina.Slots.FirstOrDefault(s => s.NumeroSlot == slot);

                        if (fechaLocal.Date < _config.Value.CajaStartDate)
                        {
                            configSlot = null;
                        }

                        int? prodId = configSlot?.ProductoId;
                        decimal cost = configSlot?.Producto?.CostoPromedio ?? 0;

                        if (configSlot != null)
                        {
                            configSlot.StockActual--;
                        }

                        _context.Ventas.Add(new Venta
                        {
                            FechaHora = fecha,
                            FechaLocal = fechaLocal,
                            PrecioVenta = precio,
                            NumeroSlot = slot,
                            IdOrdenMaquina = orderNumber,
                            MaquinaId = maquina.Id,
                            ProductoId = prodId,
                            CostoVenta = cost,
                            Pagado = false
                        });
                        guardados++;
                    }

                    await _context.SaveChangesAsync();
                    string reporte = $"PROCESADO: {guardados} nuevas | {duplicados} dupl | { ignoradosPorFecha} fuera_rango | { filtradosPorID} FILTRADOS_ID | { maquinaNoEncontrada} sin_maq";
                    Console.WriteLine($"✅ MÁQUINA: {reporte}");
                    return reporte;
                }
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"❌ ERROR MÁQUINA: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        public async Task ImportarTransbank(Stream fileStream, string nombreArchivo, DateTime? fechaLimite = null)
        {
            var tbRecords = new List<TransbankRecord>();

            try
            {
                using (var reader = ExcelReaderFactory.CreateReader(fileStream))
                {
                    var conf = new ExcelDataSetConfiguration { ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true } };
                    var tabla = reader.AsDataSet(conf).Tables[0];
                    Console.WriteLine($"💳 TRANSBANK: Analizando {tabla.Rows.Count} filas...");

                    int colFecha = -1, colHora = -1, colMonto = -1, colTipo = -1, colPos = -1;
                    for (int i = 0; i < tabla.Columns.Count; i++)
                    {
                        string h = tabla.Columns[i].ColumnName.ToUpper();
                        if (h == "FECHA") colFecha = i;
                        else if (h == "HORA") colHora = i;
                        else if (h == "MONTO") colMonto = i;
                        else if (h.Contains("TIPO MOVIMIENTO") || h.Contains("TIPO VENTA")) colTipo = i;
                        else if (h.Contains("TERMINAL") || h.Contains("POS") || h.Contains("CODIGO COMERCIO")) colPos = i;
                    }

                    if (colFecha == -1 || colMonto == -1) { Console.WriteLine("🔥 ERROR TB: Faltan columnas."); return; }

                    string[] formatos = { "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "dd-MM-yyyy HH:mm", "dd-MM-yyyy HH:mm:ss" };
                    foreach (DataRow row in tabla.Rows)
                    {
                        string tipo = colTipo != -1 ? row[colTipo]?.ToString()?.ToUpper() ?? "" : "VENTA";
                        if (!tipo.Contains("VENTA")) continue;

                        string sMonto = row[colMonto]?.ToString()?.Replace("$", "")?.Replace(".", "")?.Replace(",", "") ?? "0";
                        if (!decimal.TryParse(sMonto, out decimal montoTB) || montoTB <= 0) continue;

                        string sFecha = row[colFecha]?.ToString() ?? "";
                        string sHora = colHora != -1 ? row[colHora]?.ToString() ?? "00:00:00" : "00:00:00";

                        if (DateTime.TryParseExact($"{sFecha} {sHora}".Trim(), formatos,
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out DateTime fechaTB))
                        {
                            if (fechaLimite.HasValue && fechaTB > fechaLimite.Value) continue;

                            string posCode = colPos != -1 ? row[colPos]?.ToString()?.Trim() ?? "" : "";
                            tbRecords.Add(new TransbankRecord { Fecha = fechaTB, Monto = montoTB, PosCode = posCode });
                        }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine($"Error leyendo Excel: {ex.Message}"); return; }

            if (!tbRecords.Any()) return;

            tbRecords = tbRecords.OrderBy(x => x.Fecha).ToList();

            var maquinaMap = await _context.Maquinas
                .Where(m => !string.IsNullOrEmpty(m.CodigoTerminalPos))
                .ToDictionaryAsync(m => m.CodigoTerminalPos, m => m.Id);

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var minDate = tbRecords.Min(t => t.Fecha).AddHours(-48);
                var maxDate = tbRecords.Max(t => t.Fecha).AddHours(48);

                var ventasCandidatas = await _context.Ventas
                    .Where(v => v.FechaLocal >= minDate && v.FechaLocal <= maxDate)
                    .Where(v => !v.Pagado || v.IdOrdenMaquina == "TB-SIN-VENTA")
                    .ToListAsync();

                Console.WriteLine($"🧠 MEMORIA: Cargadas {ventasCandidatas.Count} ventas candidatas contra {tbRecords.Count} pagos.");

                var ventasAsignadasEnEstaSesion = new HashSet<int>();
                var nuevasVentasFantasma = new List<Venta>();

                int matches = 0;
                int fantasmas = 0;

                foreach (var pago in tbRecords)
                {
                    var inicioVentana = pago.Fecha.AddHours(-12);
                    var finVentana = pago.Fecha.AddHours(12);

                    var candidato = ventasCandidatas
                        .Where(v => !v.Pagado &&
                                    !ventasAsignadasEnEstaSesion.Contains(v.Id) &&
                                    v.PrecioVenta == pago.Monto &&
                                    v.FechaLocal >= inicioVentana &&
                                    v.FechaLocal <= finVentana)
                        .Select(v => new
                        {
                            Venta = v,
                            DiffSegundos = Math.Abs((v.FechaLocal - pago.Fecha).TotalSeconds)
                        })
                        .OrderBy(x => x.DiffSegundos) 
                        .FirstOrDefault();

                    if (candidato != null)
                    {
                        var venta = candidato.Venta;
                        venta.Pagado = true;
                        venta.IdTransaccionPago = "TB-MATCH";

                        if (candidato.DiffSegundos > 300)
                        {
                            venta.FechaLocal = pago.Fecha;
                        }

                        if (venta.IdOrdenMaquina == "TB-SIN-VENTA" && !string.IsNullOrEmpty(pago.PosCode))
                        {
                            if (maquinaMap.TryGetValue(pago.PosCode, out int mappedId) && venta.MaquinaId != mappedId)
                                venta.MaquinaId = mappedId;
                        }

                        ventasAsignadasEnEstaSesion.Add(venta.Id);
                        matches++;
                        pago.Matched = true;
                    }
                }

                var cobrosPendientes = tbRecords.Where(r => !r.Matched).OrderBy(r => r.Fecha).ToList();
                var ventasLibres = ventasCandidatas.Where(v => !v.Pagado && !ventasAsignadasEnEstaSesion.Contains(v.Id)).ToList();

                Console.WriteLine($"🛒 INICIANDO PASADA 2 (CARRITO): {cobrosPendientes.Count} cobros vs {ventasLibres.Count} ventas libres.");

                foreach (var cobro in cobrosPendientes)
                {
                    var inicio = cobro.Fecha.AddSeconds(-60);
                    var fin = cobro.Fecha.AddSeconds(120);

                    var candidatosCercanos = ventasLibres
                        .Where(v => v.FechaLocal >= inicio && v.FechaLocal <= fin)
                        .ToList();

                    if (candidatosCercanos.Count < 2) continue;

                    var comboGanador = EncontrarCombinacionExacta(candidatosCercanos, cobro.Monto);

                    if (comboGanador != null)
                    {
                        foreach (var venta in comboGanador)
                        {
                            venta.Pagado = true;
                            venta.IdTransaccionPago = "TB-BUNDLE";
                            venta.IdOrdenMaquina = $"BUNDLE-{cobro.PosCode}-{cobro.Fecha:HHmm}";

                            ventasAsignadasEnEstaSesion.Add(venta.Id);
                            ventasLibres.Remove(venta);
                        }

                        cobro.Matched = true;
                        matches++;
                        Console.WriteLine($" 🛒 BUNDLE MATCH: ${cobro.Monto} = {string.Join("+", comboGanador.Select(v => v.PrecioVenta))}");
                    }
                }

                var cobrosSinNada = tbRecords.Where(r => !r.Matched).ToList();

                foreach (var pago in cobrosSinNada)
                {
                    fantasmas++;

                    int targetMaquinaId = 0;
                    if (!string.IsNullOrEmpty(pago.PosCode) && maquinaMap.TryGetValue(pago.PosCode, out int mappedId))
                        targetMaquinaId = mappedId;
                    else
                        targetMaquinaId = (await _context.Maquinas.FirstOrDefaultAsync())?.Id ?? 0;

                    var fantasma = new Venta
                    {
                        FechaHora = pago.Fecha,
                        FechaLocal = pago.Fecha,
                        PrecioVenta = pago.Monto,
                        Pagado = true,
                        NumeroSlot = "ERR",
                        IdOrdenMaquina = "TB-SIN-VENTA",
                        ProductoId = null,
                        CostoVenta = 0,
                        MaquinaId = targetMaquinaId
                    };

                    nuevasVentasFantasma.Add(fantasma);
                    Console.WriteLine($" ⚠️ FANTASMA DETECTADO: ${pago.Monto} el {pago.Fecha}");
                }

                if (nuevasVentasFantasma.Any())
                {
                    _context.Ventas.AddRange(nuevasVentasFantasma);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                Console.WriteLine($"✅ PROCESO FINALIZADO: {matches} conciliados | {fantasmas} sin respaldo en máquina.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"❌ ERROR CRÍTICO (ROLLBACK): {ex.Message}");
                throw;
            }
        }

        private class TransbankRecord
        {
            public DateTime Fecha { get; set; }
            public decimal Monto { get; set; }
            public string PosCode { get; set; } = "";
            public bool Matched { get; set; } = false;
        }

        private List<Venta>? EncontrarCombinacionExacta(List<Venta> candidatos, decimal meta)
        {
            if (candidatos.Sum(v => v.PrecioVenta) < meta) return null;
            if (candidatos.Count > 5) candidatos = candidatos.Take(5).ToList();
            return BuscarRecursivo(candidatos, meta, new List<Venta>(), 0);
        }

        private List<Venta>? BuscarRecursivo(List<Venta> pool, decimal meta, List<Venta> actual, int index)
        {
            decimal sumaActual = actual.Sum(v => v.PrecioVenta);

            if (sumaActual == meta) return actual; // Found
            if (sumaActual > meta) return null;    // Exceeded
            if (index >= pool.Count) return null;  // Out of bounds

            var nuevoIntento = new List<Venta>(actual) { pool[index] };
            var resultado = BuscarRecursivo(pool, meta, nuevoIntento, index + 1);
            if (resultado != null) return resultado;

            return BuscarRecursivo(pool, meta, actual, index + 1);
        }
    }
}
