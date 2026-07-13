# 1 — Pipeline de Ingesta de Ventas

> Documentación viva. Refleja el estado del código a la fecha del último commit revisado.
> No inventa reglas: cada valor "mágico" o hardcodeado está marcado como tal con su ubicación.

---

## 1. Resumen de Negocio

Las máquinas vending registran cada venta en el portal del fabricante (**OurVend**, `os.ourvend.com`). Ese portal es la fuente de verdad de "qué se vendió, cuándo, en qué máquina y a qué precio", pero no ofrece una API oficial ni exportación programática: solo una web pensada para operarios humanos, protegida por un WAF (firewall de aplicación de Aliyun) que bloquea el tráfico que "parece" un robot.

Este módulo es el puente que trae esos datos al sistema **de forma automática y sin ser detectado**. Cada ~2 horas, dentro del horario comercial, un proceso en segundo plano inicia sesión en el portal imitando a un navegador real, descarga las ventas nuevas, las limpia (corrige fechas, elimina duplicados, asocia cada venta a su producto) y las guarda en nuestra base de datos. A partir de ahí, el resto del sistema (finanzas, logística, informes) trabaja sobre datos locales, rápidos y confiables.

El valor para el negocio: nadie tiene que entrar manualmente al portal a exportar Excel todos los días. Si el portal cambia o nos bloquea, el sistema **se repliega solo** (deja de insistir para no delatarse) y reanuda cuando es seguro, sin intervención humana.

---

## 2. Entidades Clave (Data Model)

| Entidad | Rol | Relaciones |
| --- | --- | --- |
| **Venta** | Registro atómico de una venta importada del portal. | `MaquinaId → Maquina` (obligatoria); `ProductoId → Producto` (nullable, se resuelve por slot). |
| **Maquina** | Máquina física. La búsqueda de coincidencia usa `IdInternoMaquina`. | Tiene `Slots` (`ConfiguracionSlot`). |
| **ConfiguracionSlot** | Configuración de cada canal/slot de la máquina (qué producto tiene, stock, precio). | `MaquinaId → Maquina`, `ProductoId → Producto`. |
| **SyncMetadata** | Almacén clave/valor (Key ≤100, Value ≤500) para el estado del pipeline. | — |

Campos de `Venta` relevantes (`Core/Entities/Venta.cs`):

- `FechaHora` — hora **cruda** de la máquina (sin corregir).
- `FechaLocal` — hora **ajustada** a zona local (ver §3, offset horario).
- `IdOrdenMaquina` — número de orden del portal; es la **clave de deduplicación** primaria.
- `IdTransaccionPago` — usado por la conciliación Transbank (marcas `TB-MATCH`, `TB-SIN-VENTA`, `TB-BUNDLE`).
- `PrecioVenta`, `NumeroSlot`, `CostoVenta`, `Pagado` (por defecto `false`).

**`SyncMetadata` guarda exactamente 5 claves** (`Infrastructure/Services/LastSyncTracker.cs:17-21`):
`LastSyncAt`, `BreakerState`, `BreakerConsecutiveFailures`, `BreakerCooldownUntil`, `BreakerOpenCycleCount`. Las fechas se serializan con `RoundtripKind` para preservar UTC (`:140,208`).

**Relación de sistemas (no de tablas):**

```
OurVend (os.ourvend.com)
      │  scraping stealth
      ▼
services/scraper  (FastAPI + Playwright, Python, puerto 8000)
      │  HTTP JSON
      ▼
ScraperClient  ──►  SyncOrchestratorService  ──►  SalesImportService  ──►  tabla Ventas
      ▲                                                                        │
      │                                                              decrementa StockActual
AutomatedReportService (BackgroundService)                            en ConfiguracionSlot
      │  usa
      ▼
PollScheduler + PollingCircuitBreaker + LastSyncTracker
```

---

## 3. Reglas de Negocio y Supuestos (CRÍTICO)

### 3.1 Cadencia del polling (`PollScheduler.cs`)

Valores por defecto, todos **hardcodeados** como `static readonly`:

- Intervalo base: **2 horas** (`DefaultInterval`, `:14`).
- Jitter (variación aleatoria): **±25 minutos**, uniforme en `[-jitter, +jitter)` (`:15`, `:186-191`).
- Ventana comercial: **08:00 a 21:00** hora de Chile (`America/Santiago`, DST-safe) (`:12`, `:22-23`).
- Duración máxima estimada de un ciclo: **3 minutos** (`DefaultMaxCycleDuration`, `:20`).
- Un ciclo solo dispara si **cabe entero antes del cierre**: `timeOfDay + maxCycleDuration > windowEnd → no dispara` (`:170`).
- Si el disparo cae fuera de la ventana, se difiere: a la apertura de hoy (si aún no abrió) o a la de mañana, más un offset estrictamente positivo (≥1s) para no caer exacto en el instante de apertura (`:174-216`).

> **Nota de config drift:** en `appsettings.json`/`AutomatedReportService` la ventana y valores se leen de la sección `PollingApi` con fallbacks propios (IntervalMinutes 120, JitterMaxMinutes 30, WindowStart 08:00, WindowEnd 21:00). Ver §5.

### 3.2 Circuit Breaker — el repliegue automático (`PollingCircuitBreaker.cs`)

Estados: `Closed`, `Degraded`, `Open`, `HalfOpen`, `Halted`.

Umbrales por defecto **hardcodeados**:

- Umbral de fallos consecutivos: **3** (`:88`).
- Backoff degradado: `baseInterval × 2`, tope **6h**, con jitter **±10%** (`:85`, `:302-310`).
- Cooldown base al abrir (Open): **24h** (`:86`).
- Cooldown máximo: **96h** en código (`:87`) — pero `appsettings` lo sube a **168h** (ver drift en §6).
- Ciclos Open máximos antes de detenerse: **5** (`:89`).

Transiciones (`Record`, `:233-300`):

- Éxito (Ok/Empty) en Closed/Degraded → **Closed**, resetea todos los contadores.
- Fallo: `ConsecutiveFailures++`. Si llega a 3 → **Open** con cooldown 24h; si no → **Degraded** con backoff calculado.
- **Open**: `CanAttempt` devuelve `false` hasta que `now >= CooldownUntil`; ahí transiciona a **HalfOpen** (única vía de reanudación) (`:213-219`).
- **HalfOpen**: éxito → Closed (reset total); fallo → `OpenCycleCount++`. Si llega a 5 → **Halted** (terminal, requiere reset manual); si no → Open con cooldown escalado `baseOpenCooldown × 2^OpenCycleCount`, tope al máximo (`:278-319`).
- Llamar `Record` mientras está Open lanza `InvalidOperationException` (`:239-242`); en Halted es no-op.

**Persistencia:** el estado del breaker sobrevive reinicios/redeploys porque se guarda en `SyncMetadata` tras cada ciclo. Si la DB estaba caída al arrancar (snapshot de fallback), el servicio **no persiste** el estado para no pisar el real (`AutomatedReportService.cs:260-277`).

### 3.3 Un ciclo de poll (`AutomatedReportService.RunOnePollCycleAsync`, `:186-300`)

1. Re-chequeo de ventana: fuera de 08–21 CLT → se salta.
2. `CanAttempt` del breaker.
3. `desde = LastSync ?? now.AddDays(-2)`, `hasta = now`; llama `SincronizarDesdePortalApi`.
4. `breaker.Record(...)` y **persiste snapshot inmediatamente** (`:255,280`).
5. `SetLastSync` **solo** en resultado Ok/Empty (`:286-288`) — así "tiempo desde último éxito" no miente durante una degradación.

### 3.4 Importación de una venta (`SalesImportService.ProcesarFilaVenta`, `:132-247`)

Comparte lógica entre la vía Excel y la vía JSON.

- **Match de máquina:** `IdInternoMaquina == machineId`. Sin match → contada como `sin_maq`.
- **Corrección de fecha:** se parsea la hora de máquina; si `fecha.Year < 2024` y hay hora de servidor, se usa la del servidor (`usandoServerTime = true`). **El año 2024 es un número mágico hardcodeado** (`:160`).
- **Deduplicación** (`:177-195`):
  - Con `orderNumber` presente y ≠ "0": match por `IdOrdenMaquina == orderNum && MaquinaId && FechaHora dentro de ±24h`.
  - Sin orderNumber: match por `MaquinaId && FechaHora == fecha && NumeroSlot == slot && PrecioVenta == precio`.
- **Offset horario** **[HARDCODED]** (`:200-211`): si `usandoServerTime` → `offset = -14`; si no → `offset = (machineId == "2410280012") ? 1 : -11`. `FechaLocal = fecha.AddHours(offset)`.
- **Filtro de rango:** si `fechaLocal.Date > fechaLimite.Date` → `fuera_rango`.
- **Slot/producto:** si `fechaLocal.Date < CajaStartDate` se anula el slot (sin efecto en producto/stock antes del inicio de caja). Si hay slot: `ProductoId = configSlot.ProductoId`, `CostoVenta = Producto.CostoPromedio ?? 0`, y **decrementa `configSlot.StockActual--`** (`:225-231`).

> **Importante:** la vía de importación de ventas **NO usa `ProductMatchingService`**. El producto se resuelve por slot según la configuración de la máquina. `ProductMatchingService` (fuzzy/EAN/SKU, umbral 0.6) se usa en OCR/compras, no aquí.

### 3.5 Compensación de zona horaria (el "+1 día")

Las máquinas están **12 horas adelante** respecto a Chile/servidor. Por eso:

- `SincronizarDesdePortal` (vía Excel) pide el rango con `endDate.AddDays(1)`: el día X de la máquina cae como X+1 en OurVend (`SyncOrchestratorService.cs:51-57`).
- El rango Excel se **limita a 32 días** (`:44-47`).

### 3.6 Clasificación de resultado (`classify_response`, Python `report_sales_api.py:515-559`)

Devuelve `ok` / `empty` / `blocked` / `error` / `timeout`. Detección de WAF por marcadores **hardcodeados** `["acw_sc","acw_tc","aliyungf_tc","阿里云","js_challenge","no-browser"]` (`:509-512`); HTTP 429/403 → `blocked`; redirección a login → `blocked`. El `SyncOrchestratorService` clasifica **primero por el campo `Status`** del scraper y solo cae al conteo de filas como respaldo (`:161-199`, "FIX-3").

### 3.7 Estado BORRADOR (pertenece a Cargas, no a este pipeline)

El estado `BORRADOR` **no es parte de la ingesta de ventas** — vive en el módulo de órdenes de carga. Se documenta en detalle en `4-operacion-terreno-recargas.md`. Resumen: existe para poder **redactar una orden consolidada sin tocar inventario** hasta que un humano la confirme (`OrdenCargaService.cs:295-384`). La ingesta de ventas nunca crea BORRADOR.

---

## 4. Flujo Técnico

### Servicio de scraping (Python — `services/scraper/`)

FastAPI en `0.0.0.0:8000` (`scraper_api.py:207-209`). Endpoints:

- `GET /api/sales/report` — vía JSON/browser. Llama `fetch_sales_via_browser(..., rows=2000)`, clasifica, y en fallo devuelve **HTTP 503** con `{status, reason}` (`:147-200`).
- `POST /download` — vía Excel/Playwright (`:29-60`).
- `GET /api/machines/status`, `POST /api/ocr/invoice`, `POST /api/ocr/recarga`, `GET /health`.

**Login:** RSA PKCS#1 v1.5 — `GetPubKey` → cifra password → `Account/Login`; éxito verificado por `"YSTemplet" in resp.text` (`report_sales_api.py:64-76,127`).

**Stealth** (`fetch_sales_via_browser`, `:565-809`): Chromium headless con `playwright_stealth`, args `--no-sandbox --disable-blink-features=AutomationControlled`, viewport 1920×1080, UA Chrome/120, locale `es-CL`, script que oculta `navigator.webdriver`, y `fetch` **dentro de la página** para que el fingerprint JA3/TLS sea consistente con el navegador real.

### Lado .NET

- **`ScraperClient`** (`Infrastructure/Clients/ScraperClient.cs`): `HttpClient.Timeout = Infinite` (cada llamada define su deadline). Descarga Excel 12 min (`:58`); reporte JSON 3 min (`:110`, 503 → `WafBlockedException`); estado de máquinas cacheado en `IMemoryCache` TTL **6h** (`:15-16`). URL por defecto `http://scraper:8000`. JSON en `SnakeCaseLower`.
- **`SyncOrchestratorService`**: `SincronizarDesdePortal` (Excel), `SincronizarDesdePortalApi(int, DateTime?)`, y `SincronizarDesdePortalApi(desde, hasta, ct)` que devuelve `SyncResult` estructurado. **No** actualiza `LastSyncTracker` (eso lo hace el caller).
- **`SalesImportService`**: `ImportarVentasMaquina` (Excel), `ImportarVentasDesdeJson`, `ImportarTransbank` (conciliación de pagos con subset-sum). Escribe en `Ventas`.
- **`AutomatedReportService`** (`BackgroundService`, registrado `Program.cs:163`): orquesta el loop de polling o el legacy.
- **`LastSyncTracker`**: lee/escribe `SyncMetadata`. `StalenessThreshold = 4.5h` hardcodeado; `GetHealthStatus` → Healthy solo si breaker Closed **y** último éxito dentro de 4.5h.
- **`VentasController`** (`[Authorize]`, `VentasController.cs:11`): endpoints de sincronización manual y consulta.

### UI Blazor

El pipeline es headless (segundo plano). La superficie de UI directa es mínima: disparo manual de sync y visualización del estado de sincronización vía `VentasController`. El estado del último sync (`LastSyncTracker`) alimenta indicadores en el panel.

---

## 5. Configuración (`appsettings.json` → sección `PollingApi`)

`AutomatedReportService` lee la sección con estos fallbacks (`:58-92`):

| Clave | Fallback |
| --- | --- |
| `UsePollingApi` | (si `true` → polling; si no → legacy 23:00) |
| `IntervalMinutes` | 120 |
| `JitterMaxMinutes` | 30 |
| `MaxCycleMinutes` | 3 |
| `WindowStart` / `WindowEnd` | 08:00 / 21:00 |
| `BaseIntervalForBackoffMinutes` | 7 |
| `DegradedBackoffCapMinutes` | 360 |
| `ConsecutiveFailureThreshold` | 3 |
| `BaseOpenCooldownHours` | 24 |
| `MaxOpenCooldownHours` | **168** |
| `MaxOpenCycles` | 5 |

Credenciales por variables de entorno (`docker-compose.yml:48-50`): `OURVEND_USER`, `OURVEND_PASS`, `GEMINI_API_KEY`.

---

## 6. Riesgos y Deuda Técnica Conocida

- **Endpoints sin `[Authorize]`:** `OrdenCargaController`, `InformesController`, `MaquinasController`, `TemplateRecargaController`, `ProductosController` no tienen atributo de autorización. (`VentasController` **sí** lo tiene.) `OrdenCargaController.CrearOrden` además carece de `[HttpPost]`. `VentasController.SubirVentasMaquina` (`:37`) tampoco declara `[HttpPost]` (depende de convención).
- **Fragilidad de zona horaria:** los offsets `-11 / +1 / -14` y el "+1 día" son compensaciones hardcodeadas para un desfase de 12h entre máquina y Chile (`SalesImportService.cs:200-211`, `SyncOrchestratorService.cs:51-57`). Una máquina nueva con otro reloj rompería el supuesto.
- **Número mágico año 2024** como corte de validez de la hora de máquina (`SalesImportService.cs:160`).
- **Mezcla de `DateTime.Now` vs `DateTime.UtcNow`:** los endpoints de sync manual guardan `SetLastSync(DateTime.Now)` (local), mientras el loop usa `DateTime.UtcNow` (`AutomatedReportService.cs:288`). Kinds mezclados pueden sesgar el cálculo de staleness.
- **Config drift del breaker:** `MaxOpenCooldown` es 96h en código pero 168h en `appsettings`/fallback.
- **Legacy 23:00** sigue presente (vía Excel) cuando `UsePollingApi = false` (`AutomatedReportService.cs:19,302-329`).
- **Valores hardcodeados en el scraper Python:** `BASE_URL`, group UUID `53aed77a-...`, máquina por defecto `2410280012`.
- **Pendientes anotados en el scraper (no son TODO formales):** URL de `download_reserve` "pendiente de verificar" (`report_sales_api.py:439,1183`); supuesto "precios ¿vienen en centavos? Verificar" (`:861`); statement muerto `return all_rows` duplicado (`:260-262`).
- **Caché de estado de máquinas en proceso** (`IMemoryCache`): un deploy multi-instancia necesitaría caché distribuida.

> **Fase 2 / detección:** no existen marcadores literales `TODO`/`FIXME`/`Fase 2` en el código de ingesta. Los "pendientes" son notas en español dentro del scraper. Las restricciones anti-detección (rows=2000, delays ≥1s, Chromium stealth, fetch in-page para JA3, jitter + ventana comercial) son **decisiones de diseño intencionales**, no deuda.
