using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.IO;

namespace VendingManager.Infrastructure.Services
{
    public class ExcelService : IExcelService
    {
        private readonly ApplicationDbContext _context;

        public ExcelService(ApplicationDbContext context)
        {
            _context = context;
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
                        int.TryParse(row[colSlot]?.ToString(), out int slot);
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

                        // 4. Procesar Venta (Insertar)
                        var configSlot = maquina.Slots.FirstOrDefault(s => s.NumeroSlot == slot);
                        int? prodId = configSlot?.ProductoId;
                        decimal cost = configSlot?.Producto?.CostoPromedio ?? 0;

                        _context.Ventas.Add(new Venta
                        {
                            FechaHora = fecha,
                            FechaLocal = fecha.AddHours(-11), // 👈 FIX: Ajuste automático a hora local (Chile)
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
        // 2. IMPORTAR TRANSBANK (CONCILIACIÓN +11H)
        // =============================================
        public async Task ImportarTransbank(Stream fileStream, string nombreArchivo)
        {
            try
            {
                using (var reader = ExcelReaderFactory.CreateReader(fileStream))
                {
                    var conf = new ExcelDataSetConfiguration
                    {
                        ConfigureDataTable = _ => new ExcelDataReader.ExcelDataTableConfiguration { UseHeaderRow = true }
                    };
                    var tabla = reader.AsDataSet(conf).Tables[0];
                    Console.WriteLine($"💳 TRANSBANK: Analizando {tabla.Rows.Count} filas...");

                    // Buscar columnas dinámicamente
                    int colFecha = -1, colHora = -1, colMonto = -1, colTipo = -1;

                    for (int i = 0; i < tabla.Columns.Count; i++)
                    {
                        string h = tabla.Columns[i].ColumnName.ToUpper();
                        if (h == "FECHA") colFecha = i;
                        else if (h == "HORA") colHora = i;
                        else if (h == "MONTO") colMonto = i; // "MONTO", no "MONTO CUOTA"
                        else if (h.Contains("TIPO MOVIMIENTO") || h.Contains("TIPO VENTA")) colTipo = i;
                    }

                    if (colFecha == -1 || colMonto == -1)
                    {
                        Console.WriteLine("🔥 ERROR TB: No encuentro columnas FECHA o MONTO.");
                        return;
                    }

                    int matches = 0;

                    foreach (DataRow row in tabla.Rows)
                    {
                        // 1. Filtro básico: Solo Ventas y Montos > 0
                        string tipo = colTipo != -1 ? row[colTipo]?.ToString()?.ToUpper() ?? "" : "VENTA";
                        if (!tipo.Contains("VENTA"))
                        {
                            // Console.WriteLine($"   Skipped: Tipo '{tipo}' no es VENTA"); // Uncomment if needed, usually noisy
                            continue;
                        }

                        string sMonto = row[colMonto]?.ToString()?.Replace("$", "")?.Replace(".", "")?.Replace(",", "") ?? "0";
                        if (!decimal.TryParse(sMonto, out decimal montoTB) || montoTB <= 0)
                        {
                            Console.WriteLine($"   ⚠️ Skipped Row: Monto inválido '{sMonto}'");
                            continue;
                        }

                        // 2. Construir Fecha Real (Fecha + Hora)
                        string sFecha = row[colFecha]?.ToString() ?? "";
                        string sHora = colHora != -1 ? row[colHora]?.ToString() ?? "00:00:00" : "00:00:00";
                        string fechaStringFull = $"{sFecha} {sHora}".Trim();

                        string[] formatos = { "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "dd-MM-yyyy HH:mm", "dd-MM-yyyy HH:mm:ss" };

                        // Intentar parseo exacto con formatos chilenos/latinos
                        bool fechaValida = DateTime.TryParseExact(fechaStringFull, formatos,
                                                                  System.Globalization.CultureInfo.InvariantCulture,
                                                                  System.Globalization.DateTimeStyles.None,
                                                                  out DateTime fechaTB);

                        if (!fechaValida)
                        {
                            Console.WriteLine($"   ⚠️ Skipped Row: Fecha inválida '{fechaStringFull}' (Esperado: dd/MM/yyyy)");
                            continue;
                        }

                        // Proceed (if logic was wrapped in if(TryParse) before, now we continue straight)
                        {
                            // === NUEVA LÓGICA: USAR FECHA LOCAL DIRECTA ===
                            // Ya no sumamos 11 horas, porque la BD tiene la FechaLocal correcta.
                            // Ampliamos tolerancia a +/- 60 minutos por si las máquinas tienen reloj desfasado.

                            DateTime inicio = fechaTB.AddMinutes(-60);
                            DateTime fin = fechaTB.AddMinutes(60);

                            // 3. BUSCAR EN BD USANDO FECHA LOCAL
                            var query = _context.Ventas
                                .Where(v => v.PrecioVenta == montoTB &&
                                            v.FechaLocal >= inicio &&
                                            v.FechaLocal <= fin &&
                                            !v.Pagado); // IMPORTANTE: Filtrar pagados en la query

                            var candidatos = await query.ToListAsync();

                            // Encontrar el más cercano en tiempo
                            var venta = candidatos
                                .OrderBy(v => Math.Abs((v.FechaLocal - fechaTB).TotalSeconds))
                                .FirstOrDefault();

                            if (venta != null)
                            {
                                venta.Pagado = true; // ¡MATCH!
                                matches++;
                                Console.WriteLine($"   ✅ Match: ${montoTB} | TB:{fechaTB} -> LOCAL:{venta.FechaLocal} (Id:{venta.Id}) | Diff: {(venta.FechaLocal - fechaTB).TotalMinutes:F1} min");

                                // GUARDAR INMEDIATAMENTE para que la siguiente iteración no tome la misma venta
                                await _context.SaveChangesAsync();
                            }
                            else
                            {
                                // ============================================================
                                // 4. INTENTO DE MATCH POR SUMA (COMBINACION DE VENTAS)
                                // ============================================================
                                // Caso: El usuario compra 2 productos (ej: $700 + $1000) y paga $1700 en una sola transacción.
                                // Buscamos combinaciones de ventas NO PAGADAS en el rango que sumen el monto.

                                bool matchCombinado = false;

                                // Traer todos los candidatos NO pagados en el rango de tiempo (+/- 60 min)
                                // Nota: Ampliamos un poco la búsqueda interna para dar flexibilidad a combinaciones
                                var candidatosMix = await _context.Ventas
                                    .Where(v => v.FechaLocal >= inicio &&
                                                v.FechaLocal <= fin &&
                                                !v.Pagado)
                                    .OrderBy(v => v.FechaLocal)
                                    .ToListAsync();

                                // Solo intentamos si hay candidatos y el monto es razonable
                                if (candidatosMix.Count >= 2)
                                {
                                    // NUEVO ALGORITMO: Búsqueda recursiva de combinaciones (Hasta 5 items)
                                    // Reglas: Suma exacta y todos los items dentro de una ventana de 5 minutos
                                    var combinations = FindBestCombinations(candidatosMix, montoTB);

                                    if (combinations.Any())
                                    {
                                        // Tomamos la primera válida (la función ya prioriza por orden de fecha)
                                        var mejorCombo = combinations.First();

                                        // Aplicar Cambios
                                        foreach (var v in mejorCombo)
                                        {
                                            v.Pagado = true;
                                        }

                                        matches++;
                                        matchCombinado = true;

                                        string detalles = string.Join("+", mejorCombo.Select(v => $"${v.PrecioVenta}"));
                                        string ids = string.Join(",", mejorCombo.Select(v => v.Id));

                                        decimal totalLocal = mejorCombo.Sum(v => v.PrecioVenta);
                                        string tipoMatch = totalLocal == montoTB ? "Exacto" : "AJUSTE POR COMISIÓN";

                                        Console.WriteLine($"   ✅ Match ({tipoMatch}): ${montoTB} (vs Local ${totalLocal} [{detalles}]) | TB:{fechaTB} -> IDs:[{ids}]");

                                        await _context.SaveChangesAsync();
                                    }
                                }

                                if (matchCombinado)
                                {
                                    // Si hubo match combinado, continuamos al siguiente ciclo (foreach row)
                                    // Para saltar el bloque de "NO MATCH" que viene abajo
                                    continue;
                                }
                                // --- DIAGNOSTICO DE NO MATCH ---
                                Console.WriteLine($"   ⚠️ NO MATCH: ${montoTB} | TB:{fechaTB}");

                                // 1. Ver si hay algo cerca en el rango estándar (+/- 60 min)
                                var posibles = await _context.Ventas
                                    .Where(v => v.FechaLocal >= inicio && v.FechaLocal <= fin)
                                    .OrderBy(v => v.FechaLocal)
                                    .Take(5)
                                    .ToListAsync();

                                if (posibles.Any())
                                {
                                    foreach (var c in posibles)
                                    {
                                        string motivo = c.Pagado ? "YA PAGADO" : "MONTO DIFERENTE";
                                        Console.WriteLine($"      -> RECHAZADO ({motivo}): Id:{c.Id} | Local:{c.FechaLocal} | $: {c.PrecioVenta}");
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"      -> No hay ventas en rango horario estricto (+/- 60m).");

                                    // 2. BÚSQUEDA AMPLIADA DE DEBUG (+/- 24 Horas)
                                    DateTime inicioDebug = fechaTB.AddHours(-24);
                                    DateTime finDebug = fechaTB.AddHours(24);

                                    var candidatosLejanos = await _context.Ventas
                                        .Where(v => v.PrecioVenta == montoTB &&
                                                    v.FechaLocal >= inicioDebug &&
                                                    v.FechaLocal <= finDebug)
                                        .OrderBy(v => Math.Abs((v.FechaLocal - fechaTB).TotalMinutes)) // Más cercanos primero
                                        .Take(3)
                                        .ToListAsync();

                                    if (candidatosLejanos.Any())
                                    {
                                        Console.WriteLine($"      💡 PISTA DEBUG: Encontré ventas con el MISMO precio (${montoTB}) fuera del rango:");
                                        foreach (var c in candidatosLejanos)
                                        {
                                            double diffHoras = (c.FechaLocal - fechaTB).TotalHours;
                                            Console.WriteLine($"         -> Id:{c.Id} | Local:{c.FechaLocal} | Diferencia: {diffHoras:F1} horas | Pagado:{c.Pagado}");
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine($"      ❌ DEBUG: No encontré NINGUNA venta de ${montoTB} en +/- 24 horas.");
                                    }
                                }
                            }
                        }
                    }

                    await _context.SaveChangesAsync();
                    Console.WriteLine($"✅ TRANSBANK: {matches} ventas conciliadas (pasaron a TRUE).");
                }
            }
            catch (Exception ex) { Console.WriteLine($"❌ ERROR TRANSBANK: {ex.Message}"); }
        }
        // =============================================
        // 3. IMPORTAR CATÁLOGO DE PRODUCTOS (PLANTILLA)
        // =============================================
        public async Task ImportarCatalogoProductos(Stream fileStream, string nombreArchivo)
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
                    int colBarcode = -1, colName = -1, colPrice = -1, colCost = -1, colSupplier = -1, colType = -1;

                    Console.WriteLine("📋 COLUMNAS ENCONTRADAS:");
                    for (int i = 0; i < tabla.Columns.Count; i++)
                    {
                        string h = tabla.Columns[i].ColumnName.Trim().ToLower();
                        Console.WriteLine($"   [{i}] '{tabla.Columns[i].ColumnName}' -> '{h}'");

                        if (colBarcode == -1 && h.Contains("product barcode")) colBarcode = i;
                        else if (colName == -1 && h.Contains("product name")) colName = i;
                        else if (colPrice == -1 && h.Contains("unit price")) colPrice = i;
                        else if (colCost == -1 && h.Contains("cost price")) colCost = i;
                        else if (colSupplier == -1 && h.Contains("supplier")) colSupplier = i;
                        else if (colType == -1 && h.Contains("type")) colType = i;
                    }

                    Console.WriteLine($"🔍 Mapeo: Barcode={colBarcode}, Name={colName}, Price={colPrice}");

                    if (colBarcode == -1 || colName == -1)
                    {
                        Console.WriteLine("🔥 ERROR CATÁLOGO: Faltan columnas clave (Barcode o Name).");
                        return;
                    }

                    int nuevos = 0;
                    int actualizados = 0;

                    foreach (DataRow row in tabla.Rows)
                    {
                        var rawBarcode = row[colBarcode];
                        string barcode = rawBarcode?.ToString()?.Trim() ?? "";

                        // Conversión Notación Científica (Ej: 7,80161E+11 -> 780161000000)
                        if (double.TryParse(barcode, out double dBarcode))
                        {
                            // "F0" fuerza formato numérico completo sin decimales ni E+
                            barcode = dBarcode.ToString("F0");
                        }

                        // Debug log para ver qué está leyendo
                        // Console.WriteLine($"   -> Leído: '{rawBarcode}' -> Parseado: '{barcode}'");

                        if (string.IsNullOrEmpty(barcode))
                        {
                            Console.WriteLine("   ⚠️ Fila saltada: Barcode vacío o nulo.");
                            continue;
                        }

                        string nombre = row[colName]?.ToString()?.Trim() ?? "Sin Nombre";
                        string proveedor = colSupplier != -1 ? row[colSupplier]?.ToString()?.Trim() ?? "" : "";
                        string categoria = colType != -1 ? row[colType]?.ToString()?.Trim() ?? "" : "";

                        // Parseo de precios ROBUSTO
                        decimal precio = ParseDecimal(colPrice != -1 ? row[colPrice] : null);
                        decimal costo = ParseDecimal(colCost != -1 ? row[colCost] : null);

                        var producto = await _context.Productos.FirstOrDefaultAsync(p => p.CodigoBarras == barcode);

                        if (producto == null)
                        {
                            // CREAR NUEVO
                            producto = new Producto
                            {
                                CodigoBarras = barcode,
                                Nombre = nombre,
                                PrecioVenta = precio, // USAR VALOR PARSEADO
                                CostoPromedio = costo, // USAR VALOR PARSEADO
                                Proveedor = proveedor,
                                Categoria = categoria,
                                StockBodega = 0, // Por defecto
                                SKU = barcode
                            };
                            _context.Productos.Add(producto);
                            nuevos++;
                        }
                        else
                        {
                            // ACTUALIZAR EXISTENTE (Solo metadatos, NO stock)
                            producto.Nombre = nombre;
                            producto.PrecioVenta = precio;
                            producto.CostoPromedio = costo; // MAPEO CORRECTO
                            producto.Proveedor = proveedor;
                            producto.Categoria = categoria;
                            // ¡IMPORTANTE! No tocamos StockBodega
                            actualizados++;
                        }
                    }

                    await _context.SaveChangesAsync();
                    Console.WriteLine($"✅ CATÁLOGO: {nuevos} nuevos | {actualizados} actualizados.");
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
    }
}
