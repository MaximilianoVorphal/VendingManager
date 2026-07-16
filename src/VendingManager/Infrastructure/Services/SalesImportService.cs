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
using VendingManager.Shared.Constants;
using VendingManager.Shared.DTOs;

namespace VendingManager.Infrastructure.Services
{
    public class SalesImportService : ISalesImportService
    {
        private const int MaxSaleAgeYears = 2;
        /// <summary>
        /// OurVend server-to-CLT delta. When the year guard fires and fecha is
        /// overwritten with the server timestamp (<c>usingServerTime=true</c>),
        /// the raw server timestamp must be adjusted by -14 hours to convert from
        /// the server's UTC+14 reference to Chilean CLT (UTC-4 standard / UTC-3 DST).
        /// This constant is intentionally NOT configurable — it is a fixed
        /// property of the OurVend data source, not a per-machine timezone setting.
        /// </summary>
        private const int ServerTimeOffsetHours = -14;

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

                        decimal.TryParse(row[colPrecio]?.ToString(), out decimal precio);
                        string slot = row[colSlot]?.ToString()?.Trim() ?? "";
                        string fechaStr = row[colTiempo]?.ToString() ?? "";
                        string serverTimeStr = colServerTime != -1 ? row[colServerTime]?.ToString() ?? "" : "";
                        string orderNumber = colOrden != -1 ? row[colOrden]?.ToString() ?? "" : "";

                        var (guardado, duplicado, fueraRango, noEncontrada, filtrado) = await ProcesarFilaVenta(
                            machineId, slot, precio, fechaStr, serverTimeStr,
                            orderNumber, fechaLimite, maquinaIdEsperado);

                        if (guardado) guardados++;
                        if (duplicado) duplicados++;
                        if (fueraRango) ignoradosPorFecha++;
                        if (noEncontrada) maquinaNoEncontrada++;
                        if (filtrado) filtradosPorID++;
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

        public async Task<string> ImportarVentasDesdeJson(List<SalesReportRowDto> rows, DateTime? fechaLimite = null, string? maquinaIdEsperado = null)
        {
            int guardados = 0, duplicados = 0, maquinaNoEncontrada = 0, ignoradosPorFecha = 0, filtradosPorID = 0;

            foreach (var row in rows)
            {
                var (guardado, duplicado, fueraRango, noEncontrada, filtrado) = await ProcesarFilaVenta(
                    row.MachineId, row.Slot, row.Price, row.MachineTime, row.ServerTime,
                    row.TrSerialNumber, fechaLimite, maquinaIdEsperado);

                if (guardado) guardados++;
                if (duplicado) duplicados++;
                if (fueraRango) ignoradosPorFecha++;
                if (noEncontrada) maquinaNoEncontrada++;
                if (filtrado) filtradosPorID++;
            }

            await _context.SaveChangesAsync();
            string stats = $"PROCESADO_API: {guardados} nuevas | {duplicados} dupl | {ignoradosPorFecha} fuera_rango | {filtradosPorID} FILTRADOS_ID | {maquinaNoEncontrada} sin_maq";
            Console.WriteLine($"✅ API: {stats}");
            return stats;
        }

        /// <summary>
        /// Procesa una fila individual de venta (aplica offsets horarios, deduplicación,
        /// actualización de stock, etc.). Llamado tanto desde ImportarVentasMaquina (Excel)
        /// como desde ImportarVentasDesdeJson (API).
        /// </summary>
        private async Task<(bool guardado, bool duplicado, bool fueraRango, bool maquinaNoEncontrada, bool filtrado)> ProcesarFilaVenta(
            string machineId, string slot, decimal precio, string fechaStr, string serverTimeStr,
            string orderNumber, DateTime? fechaLimite, string? maquinaIdEsperado)
        {
            // 1. Validate machineId not empty
            if (string.IsNullOrEmpty(machineId))
                return (false, false, false, true, false);

            // 2. Check maquinaIdEsperado filter
            if (!string.IsNullOrEmpty(maquinaIdEsperado) && machineId.Trim() != maquinaIdEsperado.Trim())
                return (false, false, false, false, true);

            // 3. Look up maquina in DB with Slots/Producto
            var maquina = await _context.Maquinas
                .Include(m => m.Slots)
                .ThenInclude(s => s.Producto)
                .FirstOrDefaultAsync(m => m.IdInternoMaquina == machineId);

            if (maquina == null)
            {
                Console.WriteLine($"   ⚠️ Máquina NO encontrada en BD: '{machineId}'");
                return (false, false, false, true, false);
            }

            // 4. Parse fecha (machine time), validate relative year guard
            DateTime.TryParse(fechaStr, out DateTime fecha);
            bool usingServerTime = false;

            if (fecha < DateTime.UtcNow.AddYears(-MaxSaleAgeYears) && !string.IsNullOrEmpty(serverTimeStr))
            {
                if (DateTime.TryParse(serverTimeStr, out DateTime fechaServer))
                {
                    fecha = fechaServer;
                    usingServerTime = true;
                }
            }

            // Handle scientific notation in orderNumber
            string orderNum = orderNumber;
            if (orderNum.Contains("E+") && double.TryParse(orderNum, out double dOrder))
            {
                orderNum = dOrder.ToString("F0");
            }

            // 5. Check duplicates by orderNumber or fecha+slot+precio
            bool esDuplicado = false;

            if (!string.IsNullOrEmpty(orderNum) && orderNum != "0")
            {
                esDuplicado = await _context.Ventas.AnyAsync(v =>
                    v.IdOrdenMaquina == orderNum &&
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
                return (false, true, false, false, false);

            // 6. Apply timezone offset
            int offset = usingServerTime
                ? ServerTimeOffsetHours
                : maquina.TimezoneOffsetHours ?? _config.Value.DefaultTimezoneOffsetHours;

            DateTime fechaLocal = fecha.AddHours(offset);

            // 7. Check fechaLimite
            if (fechaLimite.HasValue && fechaLocal.Date > fechaLimite.Value.Date)
                return (false, false, true, false, false);

            // 8. Find config slot and create Venta entity
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
                IdOrdenMaquina = orderNum,
                MaquinaId = maquina.Id,
                ProductoId = prodId,
                CostoVenta = cost,
                Pagado = false
            });

            return (true, false, false, false, false);
        }

        public async Task ImportarTransbank(Stream fileStream, string nombreArchivo, DateTime? fechaLimite = null)
        {
            var tbRecords = new List<TransbankRecord>();

            try
            {
                using (var reader = ExcelReaderFactory.CreateReader(fileStream))
                {
                    // Leer sin header row porque la cartola Transbank Chile tiene
                    // filas de metadata antes de los encabezados reales
                    var conf = new ExcelDataSetConfiguration { ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = false } };
                    var tabla = reader.AsDataSet(conf).Tables[0];
                    Console.WriteLine($"💳 TRANSBANK: Analizando {tabla.Rows.Count} filas (sin headers)...");

                    // Buscar la fila de encabezados reales (contiene "Fecha" y "Monto")
                    int headerRow = -1;
                    for (int r = 0; r < Math.Min(40, tabla.Rows.Count); r++)
                    {
                        var joined = new System.Text.StringBuilder();
                        for (int c = 0; c < tabla.Columns.Count; c++)
                            joined.Append((tabla.Rows[r][c]?.ToString() ?? "") + " ");
                        var text = joined.ToString().ToUpper();
                        if (text.Contains("FECHA") && text.Contains("MONTO"))
                        {
                            headerRow = r;
                            break;
                        }
                    }

                    if (headerRow == -1) { Console.WriteLine("🔥 ERROR TB: No se encontró fila de encabezados."); return; }
                    Console.WriteLine($"   📋 Fila de encabezados: {headerRow}");

                    // Mapear columnas usando la fila de encabezados
                    var header = tabla.Rows[headerRow];
                    int colFecha = -1, colMonto = -1, colTipo = -1, colPos = -1;
                    int colMontoAlternativo = -1;
                    for (int i = 0; i < tabla.Columns.Count; i++)
                    {
                        string h = (header[i]?.ToString() ?? "").ToUpper().Trim();
                        if (h.Contains("FECHA") && (h.Contains("MOVIMIENTO") || h.Contains("VENTA"))) colFecha = i;
                        else if (h == "MONTO" || (h.Contains("MONTO") && h.Contains("VALIDO") && h.Contains("ABONO"))) colMonto = i;
                        else if (h.Contains("MONTO") && h.Contains("ORIGINAL")) colMontoAlternativo = i;
                        else if (h.Contains("TIPO") && (h.Contains("MOVIMIENTO") || h.Contains("VENTA"))) colTipo = i;
                        else if (h.Contains("TERMINAL")) colPos = i;
                    }

                    // Fallback: si el formato es el viejo (headers simples), buscar en nombres de columna
                    if (colFecha == -1 || colMonto == -1)
                    {
                        for (int i = 0; i < tabla.Columns.Count; i++)
                        {
                            string h = tabla.Columns[i].ColumnName.ToUpper();
                            if (h == "FECHA") colFecha = i;
                            else if (h == "MONTO") colMonto = i;
                            else if (h.Contains("TIPO MOVIMIENTO") || h.Contains("TIPO VENTA")) colTipo = i;
                            else if (h.Contains("TERMINAL") || h.Contains("POS")) colPos = i;
                        }
                    }

                    // Si no encontró el monto preferido, usar el alternativo
                    if (colMonto == -1 && colMontoAlternativo != -1)
                    {
                        colMonto = colMontoAlternativo;
                        Console.WriteLine("   ⚠️ Usando 'Monto original de la venta' como fallback");
                    }

                    if (colFecha == -1 || colMonto == -1)
                    {
                        Console.WriteLine($"🔥 ERROR TB: Faltan columnas. Fecha={colFecha}, Monto={colMonto}");
                        return;
                    }

                    Console.WriteLine($"   ✅ Columnas: Fecha={colFecha}, Monto={colMonto}, Tipo={colTipo}, Terminal={colPos}");

                    // Culture para parsear fechas en español chileno ("06 julio 2026 04:42 PM")
                    var culturaChile = new System.Globalization.CultureInfo("es-CL");

                    for (int r = headerRow + 1; r < tabla.Rows.Count; r++)
                    {
                        var row = tabla.Rows[r];

                        string tipo = colTipo != -1 ? row[colTipo]?.ToString()?.ToUpper() ?? "" : "VENTA";
                        if (!string.IsNullOrEmpty(tipo) && !tipo.Contains("VENTA")) continue;

                        string sMonto = row[colMonto]?.ToString()?.Replace("$", "").Replace(".", "").Replace(",", "") ?? "0";
                        if (sMonto == "-" || !decimal.TryParse(sMonto, out decimal montoTB) || montoTB <= 0) continue;

                        string sFechaRaw = row[colFecha]?.ToString()?.Trim() ?? "";
                        if (string.IsNullOrEmpty(sFechaRaw)) continue;

                        // Formato Transbank Chile: "06 julio 2026 04:42 PM" o "06 julio 2026 04:42 p.m."
                        if (DateTime.TryParse(sFechaRaw, culturaChile,
                            System.Globalization.DateTimeStyles.AssumeLocal, out DateTime fechaTB))
                        {
                            if (fechaLimite.HasValue && fechaTB.Date > fechaLimite.Value.Date) continue;

                            string posCode = colPos != -1 ? row[colPos]?.ToString()?.Trim() ?? "" : "";
                            tbRecords.Add(new TransbankRecord { Fecha = fechaTB, Monto = montoTB, PosCode = posCode });
                        }
                        else
                        {
                            Console.WriteLine($"   ⚠️ No se pudo parsear fecha: '{sFechaRaw}'");
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
                    .Where(v => !v.Pagado || v.IdOrdenMaquina == VentaConstants.TbSinVenta)
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

                        if (venta.IdOrdenMaquina == VentaConstants.TbSinVenta && !string.IsNullOrEmpty(pago.PosCode))
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
                        IdOrdenMaquina = VentaConstants.TbSinVenta,
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

            if (sumaActual == meta) return actual; // Encontrado
            if (sumaActual > meta) return null;    // Excedido
            if (index >= pool.Count) return null;  // Límite excedido

            var nuevoIntento = new List<Venta>(actual) { pool[index] };
            var resultado = BuscarRecursivo(pool, meta, nuevoIntento, index + 1);
            if (resultado != null) return resultado;

            return BuscarRecursivo(pool, meta, actual, index + 1);
        }
    }
}
