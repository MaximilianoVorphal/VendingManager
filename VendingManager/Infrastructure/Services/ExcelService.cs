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
                    int colServerTime = tabla.Columns.IndexOf("Server time");
                    if (colServerTime == -1) colServerTime = tabla.Columns.IndexOf("Server Time");
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

                        // FALLBACK: Si la fecha es absurda (ej: 2013) y tenemos Server Time, usamos eso.
                        bool usandingServerTime = false;
                        if (fecha.Year < 2024 && colServerTime != -1)
                        {
                            if (DateTime.TryParse(row[colServerTime]?.ToString(), out DateTime fechaServer))
                            {
                                fecha = fechaServer;
                                usandingServerTime = true;
                                // Console.WriteLine($"   ⚠️FIX: Usando ServerTime {fechaServer} para {machineId}");
                            }
                        }

                        string orderNumber = colOrden != -1 ? row[colOrden]?.ToString() ?? "" : "";

                        // FIX: Detectar notación científica (ej: 2.41028E+09)
                        if (orderNumber.Contains("E+") && double.TryParse(orderNumber, out double dOrder))
                        {
                            orderNumber = dOrder.ToString("F0");
                        }

                        // 3. Chequear Duplicados (Lógica Híbrida)
                        // 3. Chequear Duplicados (CORREGIDO)
                        bool esDuplicado = false;

                        // MODIFICACIÓN: Agregamos validación de fecha al chequeo por ID de Orden
                        if (!string.IsNullOrEmpty(orderNumber) && orderNumber != "0")
                        {
                            esDuplicado = await _context.Ventas.AnyAsync(v =>
                                v.IdOrdenMaquina == orderNumber &&
                                v.MaquinaId == maquina.Id &&
                                // 🔥 NUEVO: Solo es duplicado si ocurrió dentro de las últimas 24 horas de la fecha que estamos procesando
                                v.FechaHora >= fecha.AddHours(-24) && 
                                v.FechaHora <= fecha.AddHours(24)
                            );
                        }
                        else
                        {
                            // B) Lógica de respaldo (se mantiene igual)
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

                        // FIX: Ajuste horario según máquina
                        // FIX: Ajuste horario según máquina
                        int offset;
                        if (usandingServerTime) 
                        {
                            // CÁLCULO: ServerTime 02:00 (día 24) -> Transbank 12:00 (día 23) = -14 Horas de diferencia
                            offset = -14; 
                        }
                        else 
                        {
                            // Cuando la máquina tiene la hora buena, tu lógica de +1 funciona perfecto.
                            offset = machineId.Trim() == "2410280012" ? 1 : -11;
                        }

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

                // =================================================================================
                // PASADA 1: MATCH SIMPLE (1 a 1) - LA MÁS FIABLE
                // =================================================================================
                foreach (var pago in tbRecords)
                {
                    // VENTANA DE TOLERANCIA: 
                    // Usamos una ventana amplia (90 min) porque priorizamos encontrar el producto.
                    // La fiabilidad se da por la cercanía (OrderBy) y no solo por estar "dentro" del rango.
// DENTRO DE ImportarTransbank -> PASADA 1

// 1. Ampliamos la ventana al MÁXIMO (12 horas atrás y adelante)
// Esto permite cruzar la venta de las 14:00 con el registro de las 12:09 sin problemas.
                    var inicioVentana = pago.Fecha.AddHours(-12);
                    var finVentana = pago.Fecha.AddHours(12);

                    var candidato = ventasCandidatas
                        .Where(v => !v.Pagado &&
                                    !ventasAsignadasEnEstaSesion.Contains(v.Id) &&
                                    v.PrecioVenta == pago.Monto && // EL PRECIO ES LO QUE MANDA
                                    v.FechaLocal >= inicioVentana &&
                                    v.FechaLocal <= finVentana)
                        // 2. Aquí está el truco:
                        // Si hay varias opciones, elegimos la que tenga el ID más bajo (la primera que entró)
                        // o la que esté más cerca en tiempo. Al haber "compresión", el tiempo no es fiable,
                        // pero el OrderBy temporal sigue siendo la mejor apuesta inicial.
                        .Select(v => new
                        {
                            Venta = v,
                            DiffSegundos = Math.Abs((v.FechaLocal - pago.Fecha).TotalSeconds)
                        })
                        .OrderBy(x => x.DiffSegundos) 
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
                        pago.Matched = true; // Marcar cobro como resuelto para que no entre en la Pasada 2
                    }
                }

                // =================================================================================
                // PASADA 2: MATCH COMBINADO (CARRITO DE COMPRAS) - SOLO PARA LO QUE SOBRÓ
                // =================================================================================

                // 1. Identificar qué nos quedó sin conciliar
                var cobrosPendientes = tbRecords.Where(r => !r.Matched).OrderBy(r => r.Fecha).ToList();
                var ventasLibres = ventasCandidatas.Where(v => !v.Pagado && !ventasAsignadasEnEstaSesion.Contains(v.Id)).ToList();

                Console.WriteLine($"🛒 INICIANDO PASADA 2 (CARRITO): {cobrosPendientes.Count} cobros vs {ventasLibres.Count} ventas libres.");

                foreach (var cobro in cobrosPendientes)
                {
                    // A. DEFINIR VENTANA ESTRICTA (Ej: -30 seg a +120 seg del cobro)
                    // El cliente paga y LUEGO la máquina dispensa los productos uno por uno.
                    var inicio = cobro.Fecha.AddSeconds(-60);
                    var fin = cobro.Fecha.AddSeconds(120);

                    // B. BUSCAR CANDIDATOS EN ESA VENTANA
                    var candidatosCercanos = ventasLibres
                        .Where(v => v.FechaLocal >= inicio && v.FechaLocal <= fin)
                        .ToList();

                    // Solo intentamos si hay al menos 2 productos (si fuera 1, lo habría atrapado la Pasada 1)
                    if (candidatosCercanos.Count < 2) continue;

                    // C. ALGORITMO COMBINATORIO (Busca qué subconjunto suma el monto exacto)
                    var comboGanador = EncontrarCombinacionExacta(candidatosCercanos, cobro.Monto);

                    if (comboGanador != null)
                    {
                        // ✅ ¡MATCH DE CARRITO ENCONTRADO!
                        foreach (var venta in comboGanador)
                        {
                            venta.Pagado = true;
                            venta.IdTransaccionPago = "TB-BUNDLE"; // Marca especial para auditoría
                            venta.IdOrdenMaquina = $"BUNDLE-{cobro.PosCode}-{cobro.Fecha:HHmm}"; // Opcional: Agrupar visualmente

                            ventasAsignadasEnEstaSesion.Add(venta.Id); // Bloquear
                            ventasLibres.Remove(venta); // Sacar de la lista local para no reusar
                        }

                        cobro.Matched = true; // Marcar cobro como resuelto
                        matches++;
                        Console.WriteLine($" 🛒 BUNDLE MATCH: ${cobro.Monto} = {string.Join("+", comboGanador.Select(v => v.PrecioVenta))}");
                    }
                }

                // =================================================================================
                // PASO FINAL: REGISTRAR LO QUE REALMENTE SOBRÓ (FANTASMAS)
                // =================================================================================
                var cobrosSinNada = tbRecords.Where(r => !r.Matched).ToList();

                foreach (var pago in cobrosSinNada)
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

        private List<Venta>? EncontrarCombinacionExacta(List<Venta> candidatos, decimal meta)
        {
            // Optimización: Si la suma total es menor que la meta, ni intentar.
            if (candidatos.Sum(v => v.PrecioVenta) < meta) return null;

            // Límite de seguridad: No combinar más de 5 productos para evitar falsos positivos matemáticos
            if (candidatos.Count > 5) candidatos = candidatos.Take(5).ToList();

            return BuscarRecursivo(candidatos, meta, new List<Venta>(), 0);
        }

        private List<Venta>? BuscarRecursivo(List<Venta> pool, decimal meta, List<Venta> actual, int index)
        {
            decimal sumaActual = actual.Sum(v => v.PrecioVenta);

            if (sumaActual == meta) return actual; // ¡Encontrado!
            if (sumaActual > meta) return null;    // Nos pasamos
            if (index >= pool.Count) return null;  // Se acabaron los candidatos

            // Opción A: Incluir el ítem actual
            var nuevoIntento = new List<Venta>(actual) { pool[index] };
            var resultado = BuscarRecursivo(pool, meta, nuevoIntento, index + 1);
            if (resultado != null) return resultado;

            // Opción B: Saltar el ítem actual
            return BuscarRecursivo(pool, meta, actual, index + 1);
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
                        ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
                    };
                    var dataSet = reader.AsDataSet(conf);
                    var tabla = dataSet.Tables[0];
                    Console.WriteLine($"📦 CATÁLOGO: Procesando {tabla.Rows.Count} productos...");

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
                            // CREAR NUEVO (Solo si tenemos un barcode válido para identificarlo a futuro)
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

                            // Actualizar precio solo si es > 0
                            if (precio > 0) producto.PrecioVenta = precio;

                            // El costo SIEMPRE se actualiza
                            if (colCost != -1)
                            {
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

        private decimal ParseDecimal(object? value)
        {
            if (value == null || value == DBNull.Value) return 0;
            string s = value.ToString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(s)) return 0;

            // Limpieza básica (quitar $, espacios)
            s = s.Replace("$", "").Replace(" ", "");

            // Intentar parseo directo
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal result))
            {
                return result;
            }

            return 0;
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