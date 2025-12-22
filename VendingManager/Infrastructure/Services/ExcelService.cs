using ExcelDataReader;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.IO;
using System.Net.Http.Json;

namespace VendingManager.Infrastructure.Services
{
    public class ExcelService : IExcelService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public ExcelService(ApplicationDbContext context, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        // =============================================
        // 0. SINCRONIZACIÓN AUTOMÁTICA (ORQUESTADOR)
        // =============================================
        public async Task<string> SincronizarDesdePortal(int maquinaId)
        {
            try
            {
                // 0. Validar Input
                if (maquinaId <= 0) return "Error: Debe seleccionar una máquina específica para sincronizar.";

                var maquina = await _context.Maquinas.FindAsync(maquinaId);
                if (maquina == null) return $"Error: Máquina con ID {maquinaId} no encontrada.";
                if (string.IsNullOrEmpty(maquina.IdInternoMaquina)) return $"Error: La máquina '{maquina.Nombre}' no tiene ID Interno configurado.";

                // 1. Configurar cliente y fechas
                var scraperUrl = _configuration["ScraperServiceUrl"] ?? "http://scraper:8000";
                var client = _httpClientFactory.CreateClient();

                var today = DateTime.Now;
                var startDate = new DateTime(today.Year, today.Month, 1).ToString("yyyy-MM-dd");
                // Pedimos hasta MAÑANA para cubrir el desfase horario con China (+11h)
                var endDate = today.AddDays(1).ToString("yyyy-MM-dd");

                var targetMachineId = maquina.IdInternoMaquina;

                var requestData = new
                {
                    machine_id = targetMachineId,
                    start_date = startDate,
                    end_date = endDate
                };

                // 2. Llamar al Scraper
                Console.WriteLine($"[Sync] Solicitando reporte a {scraperUrl}...");
                // Aumentar Timeout para Playwright
                client.Timeout = TimeSpan.FromMinutes(2);

                var response = await client.PostAsJsonAsync($"{scraperUrl}/download", requestData);

                if (!response.IsSuccessStatusCode)
                {
                    return $"Error llamando al Scraper: {response.StatusCode}";
                }

                // 3. Procesar el Stream directamente (Sin volumen compartido)
                Console.WriteLine($"[Sync] Respuesta recibida. Procesando stream...");

                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    string nombreArchivo = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? $"Report_{targetMachineId}_{today:yyyyMMdd}.xls";

                    // Si el stream no es seekable (net stream), lo copiamos a memoria para seguridad de ExcelDataReader
                    using (var ms = new MemoryStream())
                    {
                        await stream.CopyToAsync(ms);
                        ms.Position = 0;
                        await ImportarVentasMaquina(ms, nombreArchivo);
                    }
                }

                return "Sincronización Exitosa. Archivo procesado en memoria.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Sync] Error Crítico: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        // =============================================
        // 1. IMPORTAR VENTAS DE MÁQUINA (.XLS CHINO)
        // =============================================
        public async Task ImportarVentasMaquina(Stream fileStream, string nombreArchivo)
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

                    // Mapeo de columnas en INGLÉS (Corregido)
                    int colId = tabla.Columns.IndexOf("Machine ID");
                    int colSlot = tabla.Columns.IndexOf("Slot Number");
                    int colPrecio = tabla.Columns.IndexOf("Price");
                    int colTiempo = tabla.Columns.IndexOf("Machine Time");
                    int colOrden = tabla.Columns.IndexOf("Order Number");

                    if (colId == -1)
                    {
                        Console.WriteLine("🔥 ERROR: No encuentro la columna 'Machine ID'.");
                        return;
                    }

                    int guardados = 0;
                    int duplicados = 0;
                    int maquinaNoEncontrada = 0;
                    int filasVacias = 0;

                    foreach (DataRow row in tabla.Rows)
                    {
                        string machineId = row[colId]?.ToString() ?? "";
                        if (string.IsNullOrEmpty(machineId))
                        {
                            filasVacias++;
                            continue;
                        }

                        // 1. Buscar Máquina PRIMERO
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

                        // 2. Parsear DATOS PRIMERO (Necesitamos fecha/precio para el chequeo alternativo)
                        decimal.TryParse(row[colPrecio]?.ToString(), out decimal precio);
                        string slot = row[colSlot]?.ToString()?.Trim() ?? ""; // AHORA ES STRING
                        DateTime.TryParse(row[colTiempo]?.ToString(), out DateTime fecha);

                        string orderNumber = colOrden != -1 ? row[colOrden]?.ToString() ?? "" : "";

                        // FIX: Detectar notación científica (ej: 2.41028E+09)
                        if (orderNumber.Contains("E+") && double.TryParse(orderNumber, out double dOrder))
                        {
                            orderNumber = dOrder.ToString("F0");
                        }

                        // 3. Chequear Duplicados (Lógica Híbrida)
                        bool esDuplicado = false;

                        // A) Si tiene un ID de Orden válido (no vacio, no "0"), confiamos en él.
                        if (!string.IsNullOrEmpty(orderNumber) && orderNumber != "0")
                        {
                            esDuplicado = await _context.Ventas.AnyAsync(v =>
                                v.IdOrdenMaquina == orderNumber &&
                                v.MaquinaId == maquina.Id);
                        }
                        // B) Si NO tiene ID (es "0" o vacío), usamos Fecha + Slot + Precio (Máquinas de Café)
                        else
                        {
                            // Buscamos si ya existe una venta EXACTAMENTE igual en fecha y máquina
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

                        // FIX: Ajuste horario según máquina (MOVIDO ANTES DEL PROCESAMIENTO)
                        int offset = machineId.Trim() == "2410280012" ? 1 : -11;
                        DateTime fechaLocal = fecha.AddHours(offset);

                        // 4. Procesar Venta (Insertar)
                        var configSlot = maquina.Slots.FirstOrDefault(s => s.NumeroSlot == slot); // String comparison

                        // FECHA DE CORTE HISTÓRICA: Si es anterior al 18 de Diciembre 2025 (HORA LOCAL), NO asignamos producto ni descontamos stock.
                        if (fechaLocal.Date < new DateTime(2025, 12, 18))
                        {
                            configSlot = null;
                        }

                        int? prodId = configSlot?.ProductoId;
                        decimal cost = configSlot?.Producto?.CostoPromedio ?? 0;

                        // NUEVO: Decrementar Stock si existe configuración
                        if (configSlot != null)
                        {
                            configSlot.StockActual--;
                            // Opcional: Validar no bajar de 0? El requerimiento no lo explicita, pero es buena práctica.
                            // Por ahora lo dejamos permitir negativo para reconciliación posterior o si el stock estaba mal.
                        }

                        _context.Ventas.Add(new Venta
                        {
                            FechaHora = fecha,
                            FechaLocal = fechaLocal,
                            PrecioVenta = precio,
                            NumeroSlot = slot,
                            IdOrdenMaquina = orderNumber,
                            MaquinaId = maquina.Id,
                            ProductoId = prodId, // Guardamos qué era
                            CostoVenta = cost,   // Guardamos cuánto costaba
                            Pagado = false
                        });
                        guardados++;
                    }

                    await _context.SaveChangesAsync();
                    Console.WriteLine($"✅ MÁQUINA: {guardados} nuevas. DETALLES: {duplicados} duplicados | {maquinaNoEncontrada} maq_no_existe | {filasVacias} vacías.");
                }
            }
            catch (Exception ex) { Console.WriteLine($"❌ ERROR MÁQUINA: {ex.Message}"); }
        }

        // =============================================
        // 2. IMPORTAR TRANSBANK (DOBLE PASADA: ESTRICTA + HOLGADA)
        // =============================================
        // =============================================
        // 2. IMPORTAR TRANSBANK (MODO FIABILIDAD TOTAL)
        // =============================================
        public async Task ImportarTransbank(Stream fileStream, string nombreArchivo)
        {
            // 1. LEER EXCEL (Tu lógica original de lectura es correcta, la mantenemos)
            var tbRecords = new List<TransbankRecord>();

            try
            {
                using (var reader = ExcelReaderFactory.CreateReader(fileStream))
                {
                    var conf = new ExcelDataSetConfiguration { ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true } };
                    var tabla = reader.AsDataSet(conf).Tables[0];
                    Console.WriteLine($"💳 TRANSBANK: Analizando {tabla.Rows.Count} filas...");

                    // --- TU LÓGICA DE MAPEO DE COLUMNAS (Mantenida igual) ---
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

                    // --- PARSEO A MEMORIA ---
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
                            string posCode = colPos != -1 ? row[colPos]?.ToString()?.Trim() ?? "" : "";
                            tbRecords.Add(new TransbankRecord { Fecha = fechaTB, Monto = montoTB, PosCode = posCode });
                        }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine($"Error leyendo Excel: {ex.Message}"); return; }

            if (!tbRecords.Any()) return;

            // ============================================================
            // 🛡️ INICIO DE LA LÓGICA DE ALTA FIABILIDAD
            // ============================================================

            // ORDENAR CRONOLÓGICAMENTE: Vital para que el matching sea justo.
            tbRecords = tbRecords.OrderBy(x => x.Fecha).ToList();

            // MAPA DE MÁQUINAS (Optimizado)
            var maquinaMap = await _context.Maquinas
                .Where(m => !string.IsNullOrEmpty(m.CodigoTerminalPos))
                .ToDictionaryAsync(m => m.CodigoTerminalPos, m => m.Id);

            // TRANSACCIÓN DE BASE DE DATOS: Si falla algo, no se guarda nada.
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // A. DEFINIR VENTANA DE TIEMPO GLOBAL PARA CARGAR DATOS
                var minDate = tbRecords.Min(t => t.Fecha).AddHours(-48); // Miramos 2 días atrás por si acaso
                var maxDate = tbRecords.Max(t => t.Fecha).AddHours(48);

                // B. SNAPSHOT EN MEMORIA: Traer TODAS las ventas candidatas de la BD
                // Traemos las "No Pagadas" y también las "TB-EXTRA" (fantasmas previos) por si hay que corregirlas
                var ventasCandidatas = await _context.Ventas
                    .Where(v => v.FechaLocal >= minDate && v.FechaLocal <= maxDate)
                    .Where(v => !v.Pagado || v.IdOrdenMaquina == "TB-SIN-VENTA") // Incluimos fantasmas para re-conciliar si aparecen datos tarde
                    .ToListAsync();

                Console.WriteLine($"🧠 MEMORIA: Cargadas {ventasCandidatas.Count} ventas candidatas contra {tbRecords.Count} pagos.");

                // C. CONTROL DE EXCLUSIVIDAD EN RAM
                var ventasAsignadasEnEstaSesion = new HashSet<int>();
                var nuevasVentasFantasma = new List<Venta>();

                int matches = 0;
                int fantasmas = 0;

                foreach (var pago in tbRecords)
                {
                    // VENTANA DE TOLERANCIA: 
                    // Usamos una ventana amplia (90 min) porque priorizamos encontrar el producto.
                    // La fiabilidad se da por la cercanía (OrderBy) y no solo por estar "dentro" del rango.
                    var inicioVentana = pago.Fecha.AddMinutes(-90);
                    var finVentana = pago.Fecha.AddMinutes(90);

                    // 🔍 BUSCAR MEJOR CANDIDATO DISPONIBLE
                    var candidato = ventasCandidatas
                        .Where(v => !v.Pagado &&
                                    !ventasAsignadasEnEstaSesion.Contains(v.Id) && // Que no se haya usado ya
                                    v.PrecioVenta == pago.Monto && // Coincidencia exacta de precio
                                    v.FechaLocal >= inicioVentana &&
                                    v.FechaLocal <= finVentana)
                        .Select(v => new
                        {
                            Venta = v,
                            DiffSegundos = Math.Abs((v.FechaLocal - pago.Fecha).TotalSeconds)
                        })
                        .OrderBy(x => x.DiffSegundos) // EL MÁS CERCANO GANA
                        .FirstOrDefault();

                    if (candidato != null)
                    {
                        // ✅ MATCH EXITOSO
                        var venta = candidato.Venta;
                        venta.Pagado = true;
                        venta.IdTransaccionPago = "TB-MATCH"; // Marca de auditoría

                        // Si la diferencia es grande (>5 min), asumimos que la hora de Transbank es la real para reportes financieros,
                        // pero mantenemos FechaHora original para saber cuándo salió el producto.
                        if (candidato.DiffSegundos > 300)
                        {
                            // Opcional: Ajustar fecha contable, pero no la operativa
                            venta.FechaLocal = pago.Fecha;
                        }

                        // FIX: Corregir máquina si era un fantasma previo que ahora encontramos
                        if (venta.IdOrdenMaquina == "TB-SIN-VENTA" && !string.IsNullOrEmpty(pago.PosCode))
                        {
                            if (maquinaMap.TryGetValue(pago.PosCode, out int mappedId) && venta.MaquinaId != mappedId)
                                venta.MaquinaId = mappedId;
                        }

                        ventasAsignadasEnEstaSesion.Add(venta.Id); // BLOQUEAR: Nadie más puede usar esta venta
                        matches++;
                    }
                    else
                    {
                        // ⚠️ COBRO FANTASMA (El dinero existe, el producto no)
                        // Para ser fiables, debemos registrar este dinero.
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

                            // MARCAS CLARAS DE ERROR
                            NumeroSlot = "ERR",
                            IdOrdenMaquina = "TB-SIN-VENTA",
                            ProductoId = null,
                            CostoVenta = 0,
                            MaquinaId = targetMaquinaId
                        };

                        nuevasVentasFantasma.Add(fantasma);
                        Console.WriteLine($" ⚠️ FANTASMA DETECTADO: ${pago.Monto} el {pago.Fecha}");
                    }
                }

                // D. GUARDADO FINAL (Commit)
                if (nuevasVentasFantasma.Any())
                {
                    _context.Ventas.AddRange(nuevasVentasFantasma);
                }

                await _context.SaveChangesAsync(); // Guarda los updates (Pagado=true) y los inserts (Fantasmas)
                await transaction.CommitAsync();   // Sella la transacción

                Console.WriteLine($"✅ PROCESO FINALIZADO: {matches} conciliados | {fantasmas} sin respaldo en máquina.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(); // 🛑 SI ALGO FALLA, REVERTIMOS TODO
                Console.WriteLine($"❌ ERROR CRÍTICO (ROLLBACK): {ex.Message}");
                throw;
            }
        }
        // ====================================================
        // HELPERS PRIVADOS
        // ====================================================

        private class TransbankRecord
        {
            public DateTime Fecha { get; set; }
            public decimal Monto { get; set; }
            public string PosCode { get; set; } = "";
            public bool Matched { get; set; } = false;
        }

        private async Task<Venta?> FindBestMatch(decimal monto, DateTime fechaTB, DateTime inicio, DateTime fin, bool includePaid, HashSet<int> processedIds)
        {
            var query = _context.Ventas
                .Where(v => v.PrecioVenta == monto &&
                            v.FechaLocal >= inicio &&
                            v.FechaLocal <= fin);

            if (!includePaid)
            {
                query = query.Where(v => !v.Pagado);
            }

            var candidatos = await query.ToListAsync();

            // Filter out already processed in this session
            candidatos = candidatos.Where(v => !processedIds.Contains(v.Id)).ToList();

            if (!candidatos.Any()) return null;

            return candidatos
                .OrderBy(v => Math.Abs((v.FechaLocal - fechaTB).TotalSeconds))
                .First();
        }

        private async Task<Venta?> FindBestMatchWithTolerance(decimal montoTB, DateTime fechaTB, DateTime inicio, DateTime fin, bool includePaid, HashSet<int> processedIds)
        {
            var query = _context.Ventas
                .Where(v => v.PrecioVenta > montoTB &&
                            v.PrecioVenta <= (montoTB / 0.90m) &&
                            v.FechaLocal >= inicio &&
                            v.FechaLocal <= fin);

            if (!includePaid)
            {
                query = query.Where(v => !v.Pagado);
            }

            var candidatos = await query.ToListAsync();

            // Filter out already processed
            candidatos = candidatos.Where(v => !processedIds.Contains(v.Id)).ToList();

            var match = candidatos
                .Where(v => montoTB >= v.PrecioVenta * 0.94m)
                .OrderBy(v => Math.Abs((v.FechaLocal - fechaTB).TotalSeconds))
                .FirstOrDefault();

            return match;
        }

        private async Task<bool> TryMatchCombinado(TransbankRecord rec, DateTime inicio, DateTime fin, HashSet<int> processedIds)
        {
            var candidatosMix = await _context.Ventas
                .Where(v => v.FechaLocal >= inicio &&
                            v.FechaLocal <= fin &&
                            !v.Pagado)
                .OrderBy(v => v.FechaLocal)
                .ToListAsync();

            // Filter out already processed
            candidatosMix = candidatosMix.Where(v => !processedIds.Contains(v.Id)).ToList();

            if (candidatosMix.Count < 2) return false;

            var combinations = FindBestCombinations(candidatosMix, rec.Monto);
            if (combinations.Any())
            {
                var mejorCombo = combinations.First();
                foreach (var v in mejorCombo)
                {
                    v.Pagado = true;
                    v.FechaLocal = rec.Fecha; // Synced
                    processedIds.Add(v.Id);
                }

                rec.Matched = true;
                string ids = string.Join(",", mejorCombo.Select(v => v.Id));
                decimal totalLocal = mejorCombo.Sum(v => v.PrecioVenta);
                string tipoMatch = totalLocal == rec.Monto ? "Exacto" : "AJUSTE";

                Console.WriteLine($"   ✅ Match [COMBINADO {tipoMatch}]: ${rec.Monto} (vs ${totalLocal}) | TB:{rec.Fecha} -> IDs:[{ids}]");
                return true;
            }
            return false;
        }

        private async Task LogDebugExtended(decimal monto, DateTime fechaTB)
        {
            Console.WriteLine($"      -> No hay ventas en rango horario estricto (+/- 60m).");
            DateTime inicioDebug = fechaTB.AddHours(-24);
            DateTime finDebug = fechaTB.AddHours(24);

            var rawCandidatos = await _context.Ventas
                .Where(v => v.PrecioVenta == monto &&
                            v.FechaLocal >= inicioDebug &&
                            v.FechaLocal <= finDebug)
                .ToListAsync();

            var candidatosLejanos = rawCandidatos
                .OrderBy(v => Math.Abs((v.FechaLocal - fechaTB).TotalMinutes))
                .Take(3)
                .ToList();

            if (candidatosLejanos.Any())
            {
                Console.WriteLine($"      💡 PISTA DEBUG: Encontré ventas con el MISMO precio (${monto}) fuera del rango:");
                foreach (var c in candidatosLejanos)
                {
                    double diffHoras = (c.FechaLocal - fechaTB).TotalHours;
                    Console.WriteLine($"         -> Id:{c.Id} | Local:{c.FechaLocal} | Diferencia: {diffHoras:F1} horas | Pagado:{c.Pagado}");
                }
            }
        }

        // =============================================
        // 3. IMPORTAR CATÁLOGO DE PRODUCTOS (PLANTILLA)
        // =============================================
        public async Task<string> ImportarCatalogoProductos(Stream fileStream, string nombreArchivo)
        {
            try
            {
                using (var reader = ExcelReaderFactory.CreateReader(fileStream))
                {
                    var conf = new ExcelDataSetConfiguration
                    {
                        ConfigureDataTable = _ => new ExcelDataReader.ExcelDataTableConfiguration { UseHeaderRow = true }
                    };
                    var dataSet = reader.AsDataSet(conf);
                    var tabla = dataSet.Tables[0];
                    Console.WriteLine($"📦 CATÁLOGO: Procesando {tabla.Rows.Count} productos...");

                    // Mapeo de columnas (Insensible a mayúsculas) (Prioriza la primera aparición)
                    // Mapeo de columnas (Insensible a mayúsculas) (Prioriza la primera aparición)
                    int colId = -1, colBarcode = -1, colName = -1, colPrice = -1, colCost = -1, colSupplier = -1, colType = -1, colStock = -1;

                    Console.WriteLine("📋 COLUMNAS ENCONTRADAS:");
                    for (int i = 0; i < tabla.Columns.Count; i++)
                    {
                        string h = tabla.Columns[i].ColumnName.Trim().ToLower();
                        Console.WriteLine($"   [{i}] '{tabla.Columns[i].ColumnName}' -> '{h}'");

                        if (colId == -1 && (h.Contains("system id") || h == "id")) colId = i;
                        else if (colBarcode == -1 && h.Contains("product barcode")) colBarcode = i;
                        else if (colName == -1 && h.Contains("product name")) colName = i;
                        else if (colPrice == -1 && (h.Contains("unit price") || h.Contains("reference price"))) colPrice = i;
                        else if (colCost == -1 && h.Contains("cost price")) colCost = i;
                        else if (colSupplier == -1 && h.Contains("supplier")) colSupplier = i;
                        else if (colType == -1 && h.Contains("type")) colType = i;
                        else if (colStock == -1 && h.Contains("current stock")) colStock = i;
                    }

                    Console.WriteLine($"🔍 Mapeo: ID={colId}, Barcode={colBarcode}, Name={colName}, Cost={colCost}, Stock={colStock}");

                    Console.WriteLine($"🔍 Mapeo: ID={colId}, Barcode={colBarcode}, Name={colName}, Cost={colCost}");

                    // VALIDACIÓN MÍNIMA: Necesitamos AL MENOS (ID O Barcode) Y Name
                    if ((colId == -1 && colBarcode == -1) || colName == -1)
                    {
                        string errorMsg = "🔥 ERROR CATÁLOGO: Faltan columnas clave (ID o Barcode) y Name. Verifique el archivo.";
                        Console.WriteLine(errorMsg);
                        return errorMsg;
                    }

                    int nuevos = 0;
                    int actualizados = 0;

                    foreach (DataRow row in tabla.Rows)
                    {
                        string nombre = row[colName]?.ToString()?.Trim() ?? "Sin Nombre";
                        if (string.IsNullOrEmpty(nombre)) continue; // Fila vacía

                        // INTENTO 1: BUSCAR POR ID INTERNO (Prioridad Máxima)
                        Producto? producto = null;
                        if (colId != -1)
                        {
                            string sId = row[colId]?.ToString()?.Trim() ?? "";
                            if (int.TryParse(sId, out int id) && id > 0)
                            {
                                producto = await _context.Productos.FindAsync(id);
                            }
                        }

                        string barcode = "";
                        if (producto == null && colBarcode != -1)
                        {
                            // INTENTO 2: BUSCAR POR CÓDIGO DE BARRAS
                            var rawBarcode = row[colBarcode];
                            barcode = rawBarcode?.ToString()?.Trim() ?? "";

                            // Conversión Notación Científica
                            if (double.TryParse(barcode, out double dBarcode))
                            {
                                barcode = dBarcode.ToString("F0");
                            }

                            if (!string.IsNullOrEmpty(barcode))
                            {
                                producto = await _context.Productos.FirstOrDefaultAsync(p => p.CodigoBarras == barcode);
                            }
                        }

                        decimal precio = ParseDecimal(colPrice != -1 ? row[colPrice] : null);
                        decimal costo = ParseDecimal(colCost != -1 ? row[colCost] : null);
                        int stock = (int)ParseDecimal(colStock != -1 ? row[colStock] : null); // Reusing ParseDecimal for safety

                        string proveedor = colSupplier != -1 ? row[colSupplier]?.ToString()?.Trim() ?? "" : "";
                        string categoria = colType != -1 ? row[colType]?.ToString()?.Trim() ?? "" : "";

                        if (producto == null)
                        {
                            // CREAR NUEVO (Solo si tenemos un barcode válido para identificarlo a futuro, o si decidimos permitir crear sin barcode)
                            // Para mantener integridad, exigimos Barcode para crear nuevos.
                            if (string.IsNullOrEmpty(barcode))
                            {
                                Console.WriteLine($"   ⚠️ Imposible crear nuevo: Barcode vacio para '{nombre}'");
                                continue;
                            }

                            producto = new Producto
                            {
                                CodigoBarras = barcode,
                                Nombre = nombre,
                                PrecioVenta = precio,
                                CostoPromedio = costo,
                                Proveedor = proveedor,
                                Categoria = categoria,
                                StockBodega = (colStock != -1) ? stock : 0,
                                SKU = barcode
                            };
                            _context.Productos.Add(producto);
                            nuevos++;
                        }
                        else
                        {
                            // ACTUALIZAR EXISTENTE
                            producto.Nombre = nombre;
                            // Actualizamos codigo de barras solo si vino en el excel y es diferente (y no vacio)
                            if (!string.IsNullOrEmpty(barcode) && producto.CodigoBarras != barcode)
                            {
                                producto.CodigoBarras = barcode;
                            }

                            // Actualizar precio solo si es > 0, para no sobreescribir con 0 si la col no estaba
                            if (precio > 0) producto.PrecioVenta = precio;

                            // El costo SIEMPRE se actualiza
                            if (colCost != -1)
                            {
                                if (actualizados < 3) Console.WriteLine($"   🕵️ DEBUG: '{producto.Nombre}' Costo Antes: {producto.CostoPromedio} -> ExcelRaw: '{row[colCost]}' -> Parsed: {costo}");
                                producto.CostoPromedio = costo;
                            }

                            if (!string.IsNullOrEmpty(proveedor)) producto.Proveedor = proveedor;
                            if (!string.IsNullOrEmpty(categoria)) producto.Categoria = categoria;

                            // Actualizar Stock si la columna existe
                            if (colStock != -1)
                            {
                                producto.StockBodega = stock;
                            }

                            actualizados++;
                        }
                    }

                    await _context.SaveChangesAsync();
                    string resultado = $"✅ PROCESO COMPLETADO: {actualizados} productos actualizados, {nuevos} productos nuevos creados.";
                    Console.WriteLine(resultado);
                    return resultado;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ERROR CATÁLOGO: {ex.Message}");
                throw; // Rethrow para que el controlador lo note
            }
        }

        // HEPER PARSEO ROBUSTO
        private decimal ParseDecimal(object? value)
        {
            if (value == null || value == DBNull.Value) return 0;

            // 1. Si ya es numérico, retornar directo
            if (value is decimal d) return d;
            if (value is double db) return (decimal)db;
            if (value is int i) return (decimal)i;
            if (value is float f) return (decimal)f;

            // 2. Si es string, limpiar y parsear
            string s = (value.ToString() ?? "").Trim();
            if (string.IsNullOrEmpty(s)) return 0;

            // Limpieza de símbolos
            s = s.Replace("$", "").Replace("€", "").Trim();

            // Intento 1: Cultura Actual
            if (decimal.TryParse(s, out decimal res)) return res;

            // Intento 2: Invariante (Punto decimal)
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out res)) return res;

            return 0; // Fallback
        }

        // =============================================
        // HELPERS PARA COMBINACIONES (FUZZY / TOLERANCIA)
        // =============================================
        private List<List<Venta>> FindBestCombinations(List<Venta> candidates, decimal targetTotal)
        {
            var results = new List<List<Venta>>();

            // Ordenamos por fecha
            var sorted = candidates.OrderBy(v => v.FechaLocal).ToList();

            // Iteramos cada elemento como posible "inicio" de la combinación
            for (int i = 0; i < sorted.Count; i++)
            {
                var startNode = sorted[i];

                // Definimos una ventana de tiempo estricta (5 mins)
                var window = new List<Venta> { startNode };
                for (int j = i + 1; j < sorted.Count; j++)
                {
                    if ((sorted[j].FechaLocal - startNode.FechaLocal).TotalMinutes <= 5)
                        window.Add(sorted[j]);
                    else break;
                }

                // Generar todos los subconjuntos DE LA VENTANA (Power Set)
                // Como la ventana es pequeña (ej: max 10 items), 2^10 = 1024 iteraciones -> Rápido.
                // Limitamos tamaño del subset a 5 items.
                var validSubsets = GetSubsets(window, 5);

                foreach (var subset in validSubsets)
                {
                    decimal sum = subset.Sum(v => v.PrecioVenta);

                    // 1. MATCH EXACTO
                    if (sum == targetTotal)
                    {
                        results.Insert(0, subset); // Prioridad máxima
                        return results;
                    }

                    // 2. MATCH CON COMISIÓN (Tolerancia ~4-5%)
                    // Transbank a veces deposita menos (Neto vs Bruto).
                    // Ejemplo: Venta $1600 -> TB $1536 (96%).
                    // Rango aceptable: TB debe ser <= Suma y >= Suma * 0.94
                    if (targetTotal < sum && targetTotal >= sum * 0.94m)
                    {
                        // Verificación Doble: Que la diferencia se parezca a una comisión (2% a 5%)
                        decimal diff = sum - targetTotal;
                        decimal pct = diff / sum; // ej: 0.04

                        if (pct >= 0.02m && pct <= 0.06m)
                        {
                            results.Add(subset);
                        }
                    }
                }
            }

            // Si no hay exactos, devolvemos los aproximados (si existen)
            return results;
        }

        // Generador de Subsets (Iterativo)
        private List<List<Venta>> GetSubsets(List<Venta> set, int maxSize)
        {
            var subsets = new List<List<Venta>>();
            int count = set.Count;
            int powerSetCount = 1 << count; // 2^n

            for (int i = 1; i < powerSetCount; i++) // Empezar de 1 para saltar vacío
            {
                var subset = new List<Venta>();
                for (int j = 0; j < count; j++)
                {
                    if ((i & (1 << j)) > 0)
                    {
                        subset.Add(set[j]);
                    }
                }

                if (subset.Count <= maxSize)
                {
                    subsets.Add(subset);
                }
            }
            return subsets;
        }
        // =============================================
        // 4. EXPORTAR CATÁLOGO DE PRODUCTOS (PLANTILLA)
        // =============================================
        public async Task<byte[]> ExportarCatalogoProductos()
        {
            try
            {
                var productos = await _context.Productos.OrderBy(p => p.Nombre).ToListAsync();

                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("Productos");

                    // HEADERS
                    worksheet.Cell(1, 1).Value = "System ID"; // NEW COLUMN
                    worksheet.Cell(1, 2).Value = "Product Barcode";
                    worksheet.Cell(1, 3).Value = "Product Name";
                    // col 4 Reference Price REMOVED
                    worksheet.Cell(1, 4).Value = "Cost Price";
                    worksheet.Cell(1, 5).Value = "Supplier";
                    worksheet.Cell(1, 6).Value = "Type";
                    worksheet.Cell(1, 7).Value = "Current Stock";

                    // ESTILO HEADERS
                    var headerRange = worksheet.Range("A1:G1"); // Revised Range
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
                    headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thick;

                    int row = 2;
                    foreach (var p in productos)
                    {
                        worksheet.Cell(row, 1).Value = p.Id; // ID INTERNO

                        // Forzar formato texto para Barcode
                        worksheet.Cell(row, 2).Style.NumberFormat.Format = "@";
                        worksheet.Cell(row, 2).Value = p.CodigoBarras;

                        worksheet.Cell(row, 3).Value = p.Nombre;
                        // Cell 4 Price Removed
                        worksheet.Cell(row, 4).Value = p.CostoPromedio;
                        worksheet.Cell(row, 5).Value = p.Proveedor;
                        worksheet.Cell(row, 6).Value = p.Categoria;
                        worksheet.Cell(row, 7).Value = p.StockBodega;

                        row++;
                    }

                    // AUTO-FIT COLUMNS
                    worksheet.Columns().AdjustToContents();

                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        return stream.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ERROR EXPORT: {ex.Message}");
                throw;
            }
        }
    }
}
