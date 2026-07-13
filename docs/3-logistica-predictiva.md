# 3 — Logística Predictiva

> Documentación viva. Refleja el estado del código a la fecha del último commit revisado.
> No inventa reglas: cada fórmula, umbral y valor hardcodeado está citado con su ubicación.

---

## 1. Resumen de Negocio

Este módulo responde: **¿qué máquina hay que ir a rellenar, cuándo, con qué, y vale la pena el viaje?**

Una máquina que se queda sin stock no vende: es plata que se fuga cada hora que pasa vacía. Pero mandar un vehículo a rellenar cuesta (bencina, peajes, tiempo). El módulo predice, a partir del ritmo de ventas de cada producto, **cuántas horas faltan para que un slot se vacíe**, detecta quiebres que ya ocurrieron, estima la plata perdida, y agrupa las máquinas por **zona logística** para decidir si el viaje a esa zona se paga solo.

La clave del cálculo: una máquina **no vende las 24 horas**. Vende en horario comercial. Por eso la velocidad de vaciado se calcula sobre **14 horas operativas (08:00–22:00), no 24** — usar 24 subestimaría el ritmo real y llegaríamos tarde a rellenar.

El resultado operativo: un "optimizador de rutas" que rankea zonas por rentabilidad y genera **órdenes de carga en borrador** para las máquinas en riesgo, listas para que un humano confirme.

---

## 2. Entidades Clave (Data Model)

| Entidad | Rol | Relaciones |
| --- | --- | --- |
| **Maquina** | Máquina física. | `ZonaLogisticaId` (int?, **nullable**) `→ ZonaLogistica`; tiene `Slots`. |
| **ZonaLogistica** | Agrupación geográfica de máquinas. | `CostoBaseViaje` (decimal) = costo fijo de enviar un vehículo. FK `Maquina.ZonaLogisticaId` con `OnDelete(SetNull)`. |
| **ConfiguracionSlot** | Slot vivo de una máquina. | `StockActual`, `CapacidadMaxima` (default **10**), `StockMinimo` (default **2**), `PrecioVenta`. Las ventas **no** tienen FK a slot. |
| **SnapshotSlot** | Foto del inventario en el momento de una recarga. | Pertenece a `PeriodoRecarga`. `CantidadInicial`, `CapacidadSlot`, `Estado` (`EstadoSlot`). |
| **TemplateRecarga** | Ciclo de recarga por máquina. | `Estado` (`EstadoTemplate`: `Pendiente=0`, `Terminado=2`). Solo el **último Terminado** por máquina alimenta el stock-crítico. |
| **OrdenCarga** | Orden de reposición. | `Estado` string (`BORRADOR`/`PENDIENTE`/`FINALIZADA`). |

**Relación central:** `Maquina → ZonaLogistica` (nullable — una máquina puede no tener zona, y ese caso "Sin zona" recibe trato especial en la rentabilidad, ver §3.3).

**Seed de zonas** (hardcodeado, `ApplicationDbContext.cs:270-274`): Zona Norte = **25000**, Zona Centro = **15000**, Zona Sur = **20000** (`CostoBaseViaje`, `decimal(18,2)`).

---

## 3. Reglas de Negocio y Supuestos (CRÍTICO)

### 3.1 La ventana operativa: 14 horas (`HorarioOperativoHelper.cs`)

Fuente única de verdad del horario:

- `InicioOperativo = 8`, `FinOperativo = 22` (`:10-11`) → ventana **[08:00, 22:00) = 14 horas/día**.
- `HorasEnRangoOperativo(desde, hasta)` (`:18-48`) recorre hora por hora sumando **solo** las horas donde `hour >= 8 && hour < 22`; salta las horas muertas/nocturnas. Devuelve 0 si `hasta <= desde`.

> El literal `Hour >= 8 && Hour < 22` está **duplicado** en `LogisticaPredictivaService.cs:52-53` y `TemplateRecargaAnalyticsService.cs:68`, no centralizado.

### 3.2 Velocidad de vaciado — la fórmula ×14 (`LogisticaPredictivaService.BuildSlotDto`, `:130-187`)

Pre-filtro de ventas (`:47-53`): `FechaLocal >= desde` (con `desde = Now.AddDays(-diasHistorial)`, default **14 días**), **excluye fantasmas** `IdOrdenMaquina != "TB-EXTRA"` y `!= "TB-SIN-VENTA"`, y **filtro por hora del día** `FechaLocal.Hour >= 8 && < 22`.

Fórmula (`:141-148`):

```
horasActivas     = HorasEnRangoOperativo(primeraVenta, ultimaVenta)
if (horasActivas < 1) horasActivas = 1        // guarda para ventanas sub-hora
velocidadPorHora = unidadesVendidas / horasActivas
velocidad        = velocidadPorHora × 14      // ×14 horas operativas, NO ×24
```

Si no hay primera/última venta o 0 unidades → `velocidad = 0`.

> **Doc obsoleta:** el comentario XML de `LogisticaPredictivaService` (`:22-25`) todavía describe el reparto de velocidad entre slots que comparten producto — comportamiento **ya eliminado** (`:138-139`). La velocidad ya no se divide entre slots.

`TemplateRecargaAnalyticsService` (`:61-77`) calcula velocidad por clave `(MaquinaId, ProductoId)` como tasa **por hora** (`count / horasActivas`); el escalado ×14/×24 ocurre después en el DTO (ver §3.6).

### 3.3 Predicción de días-a-quiebre y rentabilidad de viaje (`:160-170`, `:109-125`)

```
t                  = StockActual <= 0 ? 0 : StockActual / velocidad   // velocidad ya es diaria (×14)
diasHastaQuiebre   = t
esCritico          = t < UmbralCriticoDias                            // UmbralCriticoDias = 2.0 [HARDCODED]
diasVacios         = Clamp(ventanaProyeccionDias − t, 0, ventanaProyeccionDias)
margen             = Max(0, PrecioVenta − (CostoPromedio ?? 0))
lcp                = margen × velocidad × diasVacios                  // Lucro Cesante Proyectado
```

- `UmbralCriticoDias = 2.0` **hardcodeado** ("Quiebre proyectado < 48h", `:14`).
- `ventanaProyeccionDias` default = 3 (`:31`).
- `UnidadesFaltantes = Max(0, CapacidadMaxima − StockActual)` (`:180`).

**Rollup por zona y "vale la pena viajar":**

```
LcpMaquina        = Σ LcpSlot
LcpTotal (zona)   = Σ LcpMaquina
costoBase         = zona?.CostoBaseViaje ?? 0
EsRentableViajar  = zona != null && LcpTotal > costoBase           // "Sin zona" (null) → forzado false
```

Zonas ordenadas por `LcpTotal − CostoBaseViaje` descendente.

### 3.4 Detección de quiebre de stock — DOS métodos distintos

**Método A — inferencia por silencio (`SalesAnalyticsService.GetStockoutAnalysisAsync`, `:511-716`):**

- `umbralHorasSilencio` default = **24** (`:512`).
- Por máquina: `ultimaActividad` = última venta de la máquina.
- Por (máquina, producto): `horasDiferencia = (ultimaActividadMaquina − ultimaVentaProducto).TotalHours`; **`posibleQuiebre = horasDiferencia > 24`** (`:595-596`) — se infiere quiebre cuando un producto dejó de vender >24h antes de la última actividad de la máquina.
- **Usa horas de reloj crudas (÷24), NO el helper operativo**: `VelocidadDiaria = VelocidadPorHora × 24` (`StockoutAnalysisDto.cs:76`).
- Pérdida: `dineroPerdido = velocidadPorHora × horasSinStock × precioPromedio`.
- Dead slots (`:670-709`): slots configurados con producto pero **sin ventas** en el período → `EsDeadSlot = true`, `PosibleQuiebre = StockActual <= StockMinimo`.

**Método B — inferencia por depleción de snapshot (`TemplateRecargaAnalyticsService.AnalizarMaquinaEnPeriodo`, `:408-579`):**

- **`posibleQuiebre = cantidadVendida >= cantidadInicial`** (`:461`) — vendió ≥ el stock inicial del snapshot. Requiere `cantidadInicial > 0`.
- `fechaAgotamiento = ventasSlot[cantidadInicial − 1].FechaLocal` (la venta que vació el slot).
- Velocidad efectiva: prefiere la de producto `(maquinaId, productoId)` (operativa ×14), fallback per-slot.
- Pérdida con velocidad de producto usa `HorasEnRangoOperativo` (14h); el **fallback usa horas de reloj crudas** — la inconsistencia entre regímenes de horas es intencional según comentarios (`:508-509,525`).

> **Riesgo de sobre-estimación:** el DTO expone `VelocidadDiaria => VelocidadPorHora × 24` (`StockoutAnalysisDto.cs:76,194`), pero en la vía template `VelocidadPorHora` ya es una tasa operativa (base 14h). Multiplicar una tasa base-14 por ×24 mezcla regímenes y puede inflar la velocidad diaria mostrada.

### 3.5 Sugerencias de compra (`PurchasingService.GetPurchaseSuggestionAsync`, `:136-204`)

```
if (dias <= 0) dias = RotacionStockMinimoDias        // = 30
fechaInicio = Now.Date.AddDays(-dias)
sugerido    = ventasPeriodo − (stockEnMaquinas + StockBodega)   // clamp < 0 → 0
```

- `ventasPeriodo` = unidades vendidas en la ventana; `stockEnMaquinas` = Σ `StockActual`.
- **Sin lógica de horas operativas; asume que el próximo período ≈ demanda de los últimos `dias`.**
- **Cacheado 5 días** (`AbsoluteExpirationRelativeToNow = FromDays(5)`, `:143`) — riesgo de sugerencias rancias.
- El campo DTO `VentasUltimos30Dias` (`PurchaseSuggestionDto.cs:7`) es un **nombre engañoso**: se puebla con `ventasPeriodo` para cualquier ventana `dias`, no siempre 30.

### 3.6 Umbrales de stock crítico (`PurchasingService.GetStockCriticoAsync`, `:42-134`)

- Vía ConfiguracionSlots: **`StockActual <= StockMinimo && ProductoId != 0`** (`:111`). `StockMinimo` default = 2 por slot.
- Vía Template: **`CantidadInicial <= 2`** hardcodeado (`:88`) — el "2" no está atado a `StockMinimo`.
- `Fuente` del DTO = `"template"` o `"configuracion"` según la vía.
- Flag `UseTemplateInventoryForStockCritico = false` (`VendingConfig.cs:24`) conmuta la fuente.

### 3.7 Clasificación de productos (`SalesAnalyticsService.GetAnalisisProductosAsync`, `:424-479`)

```
RotacionDiaria = CantidadVendida / diasDelPeriodo
Estrella  si RotacionDiaria >= RotacionAlta (1.0)
Joya/Cacho si < RotacionMedia (0.2), dividido por MargenAlto (0.50)
ABC: <=80 → A, <=95 → B, else C     // cutoffs hardcodeados
```

Umbrales `RotacionAlta=1.0`, `RotacionMedia=0.2`, `MargenAlto=0.50` desde `AnalyticsThresholds.cs:5-7`.

---

## 4. Optimización de Rutas (`OptimizadorRutas.razor.cs`)

> **No existe algoritmo de ruteo / TSP / grafo.** El "optimizador" es una **compuerta de rentabilidad + ranking**, no un solver de rutas.

- Solo consulta `api/LogisticaPredictiva/zonas` y muestra las zonas (`:35-36`).
- Heurística = LCP vs costo de viaje (semáforo Rentable/Pérdida), calculado server-side (§3.3). No hay secuenciación, distancia ni capacidad de vehículo.
- Defaults hardcodeados: `DiasHistorial = 14`, `VentanaDias = 3` (`:24-25`).
- `CriticosDe(zona)` cuenta `EsCritico && UnidadesFaltantes > 0`. "Generar Orden" se deshabilita con 0 críticos.
- **Generación de borrador** (`LogisticaPredictivaService.GenerarOrdenCargaBorradorAsync`, `:189-222`): de una zona, toma slots `EsCritico && UnidadesFaltantes > 0`, consolida por `(MaquinaId, ProductoId)`, `Cantidad = Σ UnidadesFaltantes`; crea **una** orden BORRADOR `"Rescate {zona} {dd/MM/yyyy}"`. Lanza si la zona no existe o no hay críticos.

---

## 5. Páginas Blazor (UI del dominio)

- **`OptimizadorRutas.razor`** (`/optimizador-rutas`, `[Authorize]`): tarjetas de zona con badge Rentable/Pérdida, `CostoBaseViaje`, `LcpTotal`, Δ, nº máquinas y nº slots críticos; tabla "Detalle de Rescate" + botón "Generar Orden de Carga" → BORRADOR.
- **`StockoutDashboard.razor(.cs)`** (`/stockout-dashboard`, `[Authorize]`): dos vías de datos (template `api/TemplateRecarga/{id}/analyze` y manual `api/Ventas/stockout-analysis`). Defaults `FechaInicio = Hoy−7`, `FechaFin = Hoy`, `UmbralHoras = 24`. Tabla de alertas agrupada por producto, KPIs, timeline top-6 por días sin stock, gráfico de barras de ventas diarias, y un **"mapa de agotamiento"** (esquema físico de la máquina que reproduce la depleción con un scrubber; `CalculateStockAtTime = StockInicial − ventasAntesDe(t)`). Umbrales de color de slot hardcodeados (`ratio <= 0.001 → danger`, `<= 0.4 → warning`); bandas `>72 Crítico, >48 Alto, >24 Medio`. Contiene un **DTO local duplicado** de `StockoutAnalysisDto` (`:1081-1201`).
- **`GestionMaquinas.razor`** (`/maquinas`, `[Authorize(Roles = Admin)]`): CRUD de máquinas incl. dropdown `ZonaLogisticaId` — cómo se asignan máquinas a zonas para todo el dominio.
- **`AnalisisProductos.razor`** (`/analisis-productos`): ranking de productos, ABC/Pareto, tendencia MoM/WoW. **Sin `@attribute [Authorize]`** (ver §6).

---

## 6. Riesgos y Deuda Técnica Conocida

- **Endpoints/páginas SIN autorización:**
  - `TemplateRecargaController` **sin `[Authorize]`** (ni clase ni método): sus `GET {id}/analyze` (`:174`) y `{templateId}/slot-timeline` (`:191`) — los que llama el StockoutDashboard — son **públicamente alcanzables**, mientras `VentasController` y `LogisticaPredictivaController` sí están protegidos.
  - `AnalisisProductos.razor` **sin `@attribute [Authorize]`**, a diferencia de las otras páginas del dominio.
  - `InventarioController.SubirCatalogo` (`:12`) **sin `[HttpPost]`** (la clase sí es `[Authorize]`).
- **Dos métodos de detección de quiebre** (A silencio ÷24 reloj vs B snapshot mezclando ×14 operativo) con regímenes de horas inconsistentes — documentado en comentarios pero es una divergencia analítica real.
- **`VelocidadDiaria => × 24`** aplicado a una tasa que en la vía template ya es base-14 (posible sobre-estimación de velocidad diaria).
- **Supuestos hardcodeados:**
  - Ventana operativa **08:00–22:00 / 14h** hardcodeada en el helper **y** duplicada como literal en dos servicios — no hay horario por-máquina pese a que el docstring de `TemplateRecarga` lo insinúa.
  - `UmbralCriticoDias = 2.0` y el `<= 2` de stock-crítico template son literales.
  - Costos de viaje por zona hardcodeados en el seed (25000/15000/20000).
  - Fallback de fin de período de 90 días hardcodeado.
- **Nombres/docs engañosos:** doc XML de `LogisticaPredictivaService` describe reparto de slots eliminado; `PurchaseSuggestionDto.VentasUltimos30Dias` guarda una ventana variable.
- **Cachés rancias:** sugerencias de compra cacheadas 5 días; stats de dashboard 60s.
- **Config muerta:** `VendingConfig.RotacionUmbralCritico = 7` no se referencia en los servicios revisados (candidato a config muerta).
- **Supuestos simplificadores de velocidad:** las ventas no tienen FK a slot (velocidad es por máquina-producto); la guarda `horasActivas < 1 → 1` puede inflar velocidad en ventanas muy cortas; la exclusión de fantasmas depende de strings mágicos `"TB-EXTRA"`/`"TB-SIN-VENTA"` dispersos.
- **Stub Fase 2:** `TemplateRecargaAnalyticsService.GetSlotTimelineAsync` (`:305`) marcado "full implementation belongs in PR 2" (aunque ya devuelve datos).
