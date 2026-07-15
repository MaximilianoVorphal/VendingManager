# 1 — Pipeline de Ingesta de Ventas

> Documentación viva. Refleja el estado del código a la fecha del último commit revisado.

---

## 1. Resumen de Negocio

Las máquinas vending registran cada venta en el portal del fabricante (**OurVend**, `os.ourvend.com`). Ese portal es la fuente de verdad de "qué se vendió, cuándo, en qué máquina y a qué precio", pero no ofrece una API oficial ni exportación programática: solo una web pensada para operarios humanos.

Este módulo es el puente que trae esos datos al sistema **de forma automática**. Cada ~2 horas, dentro del horario comercial, un proceso en segundo plano inicia sesión en el portal, descarga las ventas nuevas, las limpia (corrige fechas, elimina duplicados, asocia cada venta a su producto) y las guarda en nuestra base de datos. A partir de ahí, el resto del sistema (finanzas, logística, informes) trabaja sobre datos locales, rápidos y confiables.

El valor para el negocio: nadie tiene que entrar manualmente al portal a exportar Excel todos los días. Si el portal cambia o nos bloquea, el sistema **se repliega solo** (deja de insistir) y reanuda cuando es seguro, sin intervención humana.

---

## 2. Entidades Clave (Data Model)

| Entidad | Rol | Relaciones |
| --- | --- | --- |
| **Venta** | Registro atómico de una venta importada del portal. | `MaquinaId → Maquina` (obligatoria); `ProductoId → Producto` (nullable, se resuelve por slot). |
| **Maquina** | Máquina física. La búsqueda de coincidencia usa `IdInternoMaquina`. | Tiene `Slots` (`ConfiguracionSlot`). |
| **ConfiguracionSlot** | Configuración de cada canal/slot de la máquina (qué producto tiene, stock, precio). | `MaquinaId → Maquina`, `ProductoId → Producto`. |
| **SyncMetadata** | Almacén clave/valor para el estado del pipeline. | — |

Campos de `Venta` relevantes (`Core/Entities/Venta.cs`):

- `FechaHora` — hora de la máquina (sin corregir).
- `FechaLocal` — hora ajustada a zona local.
- `IdOrdenMaquina` — número de orden del portal; es la **clave de deduplicación** primaria.
- `IdTransaccionPago` — usado por la conciliación Transbank (marcas `TB-MATCH`, `TB-SIN-VENTA`, `TB-BUNDLE`).
- `PrecioVenta`, `NumeroSlot`, `CostoVenta`, `Pagado` (por defecto `false`).

**`SyncMetadata` guarda 5 claves:** `LastSyncAt`, `BreakerState`, `BreakerConsecutiveFailures`, `BreakerCooldownUntil`, `BreakerOpenCycleCount`.

**Relación de sistemas (no de tablas):**

```
OurVend (os.ourvend.com)
      │  scraping automatizado
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

## 3. Reglas de Negocio y Supuestos

### 3.1 Cadencia del polling

Parámetros por defecto:

- Intervalo base: **2 horas**.
- Jitter (variación aleatoria): **±25 minutos**, distribución uniforme.
- Ventana comercial: **08:00 a 21:00** hora de Chile (`America/Santiago`, DST-safe).
- Duración máxima estimada de un ciclo: **3 minutos**.
- Un ciclo solo dispara si **cabe entero antes del cierre**: `horaActual + duraciónMáxima > cierreVentana → no dispara`.
- Si el disparo cae fuera de la ventana, se difiere: a la apertura de hoy (si aún no abrió) o a la de mañana, con un offset positivo (≥1s) para no caer exacto en el instante de apertura.

### 3.2 Circuit Breaker — el repliegue automático

Estados: `Closed`, `Degraded`, `Open`, `HalfOpen`, `Halted`.

Umbrales por defecto:

- Umbral de fallos consecutivos: **3**.
- Backoff degradado: `intervaloBase × 2`, tope **6h**, con jitter **±10%**.
- Cooldown base al abrir (Open): **24h**.
- Cooldown máximo: **168h** (configurable en `appsettings`).
- Ciclos Open máximos antes de detenerse: **5**.

Transiciones:

- Éxito (Ok/Empty) en Closed/Degraded → **Closed**, resetea todos los contadores.
- Fallo: `ConsecutiveFailures++`. Si llega a 3 → **Open** con cooldown 24h; si no → **Degraded** con backoff calculado.
- **Open**: `CanAttempt` devuelve `false` hasta que `now >= CooldownUntil`; ahí transiciona a **HalfOpen** (única vía de reanudación).
- **HalfOpen**: éxito → Closed (reset total); fallo → `OpenCycleCount++`. Si llega a 5 → **Halted** (terminal, requiere reset manual); si no → Open con cooldown escalado `baseOpenCooldown × 2^OpenCycleCount`, tope al máximo.

**Persistencia:** el estado del breaker sobrevive reinicios/redeploys porque se guarda en `SyncMetadata` tras cada ciclo.

### 3.3 Un ciclo de poll

1. Re-chequeo de ventana: fuera de 08–21 CLT → se salta.
2. `CanAttempt` del breaker.
3. `desde = LastSync ?? now.AddDays(-2)`, `hasta = now`; sincroniza desde el portal.
4. El breaker registra el resultado y persiste snapshot inmediatamente.
5. `SetLastSync` **solo** en resultado Ok/Empty — así "tiempo desde último éxito" no miente durante una degradación.

### 3.4 Importación de una venta

Comparte lógica entre la vía Excel y la vía JSON.

- **Match de máquina:** `IdInternoMaquina == machineId`. Sin match → contada como `sin_maq`.
- **Corrección de fecha:** se parsea la hora de máquina; si la fecha es incoherente y hay hora de servidor disponible, se usa la del servidor.
- **Deduplicación:**
  - Con `orderNumber` presente y ≠ "0": match por `IdOrdenMaquina == orderNum && MaquinaId && FechaHora dentro de ±24h`.
  - Sin orderNumber: match por `MaquinaId && FechaHora == fecha && NumeroSlot == slot && PrecioVenta == precio`.
- **Offset horario:** se aplica una compensación de zona horaria para alinear la hora de máquina con la hora local de Chile.
- **Filtro de rango:** si `fechaLocal.Date > fechaLimite.Date` → `fuera_rango`.
- **Slot/producto:** si `fechaLocal.Date < CajaStartDate` se anula el slot (sin efecto en producto/stock antes del inicio de caja). Si hay slot: `ProductoId = configSlot.ProductoId`, `CostoVenta = Producto.CostoPromedio ?? 0`, y **decrementa `configSlot.StockActual--`**.

La vía de importación de ventas **no usa `ProductMatchingService`**. El producto se resuelve por slot según la configuración de la máquina. `ProductMatchingService` (fuzzy/EAN/SKU, umbral 0.6) se usa en OCR/compras, no aquí.

### 3.5 Compensación de zona horaria

Las máquinas están **12 horas adelante** respecto a Chile/servidor. Por eso:

- La sincronización vía Excel pide el rango con `endDate.AddDays(1)`: el día X de la máquina cae como X+1 en OurVend.
- El rango Excel se **limita a 32 días** para mantener volúmenes manejables.

### 3.6 Clasificación de resultado

El microservicio Python clasifica cada sincronización como `ok` / `empty` / `blocked` / `error` / `timeout`. La detección de bloqueo por WAF se basa en marcadores conocidos en las respuestas HTTP. El `SyncOrchestratorService` clasifica **primero por el campo `Status`** del scraper y solo cae al conteo de filas como respaldo.

### 3.7 Estado BORRADOR (pertenece a Cargas, no a este pipeline)

El estado `BORRADOR` **no es parte de la ingesta de ventas** — vive en el módulo de órdenes de carga. Se documenta en detalle en [04-operacion-terreno.md](./04-operacion-terreno.md). Resumen: existe para poder **redactar una orden consolidada sin tocar inventario** hasta que un humano la confirme. La ingesta de ventas nunca crea BORRADOR.

---

## 4. Flujo Técnico

### Servicio de scraping (Python — `services/scraper/`)

FastAPI en `0.0.0.0:8000`. Endpoints:

- `GET /api/sales/report` — vía JSON/browser. Descarga ventas con límite de filas y clasifica el resultado.
- `POST /download` — vía Excel/Playwright.
- `GET /api/machines/status`, `POST /api/ocr/invoice`, `POST /api/ocr/recarga`, `GET /health`.

**Login:** RSA PKCS#1 v1.5 — obtiene clave pública, cifra la contraseña y autentica contra `Account/Login`; el éxito se verifica por la presencia de un marcador específico en la respuesta.

**Automatización del navegador:** Chromium headless con Playwright, viewport 1920×1080, UA Chrome/120, locale `es-CL`, y ejecución de requests dentro de la página para consistencia de fingerprint TLS.

### Lado .NET

- **`ScraperClient`** (`Infrastructure/Clients/ScraperClient.cs`): cliente HTTP con timeouts por endpoint (12 min Excel, 3 min JSON). Estado de máquinas cacheado en `IMemoryCache` TTL **6h**. URL por defecto `http://scraper:8000`. JSON en `SnakeCaseLower`.
- **`SyncOrchestratorService`**: orquesta la sincronización desde el portal (vía Excel o JSON), devuelve `SyncResult` estructurado. **No** actualiza `LastSyncTracker` (eso lo hace el caller).
- **`SalesImportService`**: importa ventas desde Excel, JSON, y conciliación de pagos Transbank (subset-sum). Escribe en `Ventas`.
- **`AutomatedReportService`** (`BackgroundService`): orquesta el loop de polling.
- **`LastSyncTracker`**: lee/escribe `SyncMetadata`. `StalenessThreshold = 4.5h` para determinar si los datos están frescos.
- **`VentasController`** (`[Authorize]`): endpoints de sincronización manual y consulta.

### UI Blazor

El pipeline es headless (segundo plano). La superficie de UI directa es mínima: disparo manual de sync y visualización del estado de sincronización vía `VentasController`. El estado del último sync (`LastSyncTracker`) alimenta indicadores en el panel.

---

## 5. Configuración (`appsettings.json` → sección `PollingApi`)

`AutomatedReportService` lee la sección con estos parámetros:

| Clave | Función |
| --- | --- |
| `UsePollingApi` | Si `true` → polling automático; si no → sincronización programada a las 23:00. |
| `IntervalMinutes` | Intervalo base entre polls (default 120). |
| `JitterMaxMinutes` | Variación aleatoria máxima (default 30). |
| `MaxCycleMinutes` | Duración máxima estimada por ciclo (default 3). |
| `WindowStart` / `WindowEnd` | Ventana comercial (default 08:00 / 21:00). |
| `BaseIntervalForBackoffMinutes` | Intervalo base para backoff degradado (default 7). |
| `DegradedBackoffCapMinutes` | Tope de backoff degradado (default 360). |
| `ConsecutiveFailureThreshold` | Fallos para abrir el breaker (default 3). |
| `BaseOpenCooldownHours` | Cooldown base al abrir (default 24). |
| `MaxOpenCooldownHours` | Cooldown máximo (default 168). |
| `MaxOpenCycles` | Ciclos Open máximos (default 5). |

Credenciales por variables de entorno (`docker-compose.yml`): `OURVEND_USER`, `OURVEND_PASS`, `GEMINI_API_KEY`.
