# 3 — Logística Predictiva

> Documentación viva. Refleja el estado del código a la fecha del último commit revisado.

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
| **ZonaLogistica** | Agrupación geográfica de máquinas. | `CostoBaseViaje` (decimal) = costo fijo de enviar un vehículo. |
| **ConfiguracionSlot** | Slot vivo de una máquina. | `StockActual`, `CapacidadMaxima` (default **10**), `StockMinimo` (default **2**), `PrecioVenta`. |
| **SnapshotSlot** | Foto del inventario en el momento de una recarga. | Pertenece a `PeriodoRecarga`. `CantidadInicial`, `CapacidadSlot`, `Estado` (`EstadoSlot`). |
| **TemplateRecarga** | Ciclo de recarga por máquina. | `Estado` (`EstadoTemplate`: `Pendiente=0`, `Terminado=2`). Solo el **último Terminado** por máquina alimenta el stock-crítico. |
| **OrdenCarga** | Orden de reposición. | `Estado` string (`BORRADOR`/`PENDIENTE`/`FINALIZADA`). |

**Relación central:** `Maquina → ZonaLogistica` (nullable — una máquina puede no tener zona, y ese caso "Sin zona" recibe trato especial en la rentabilidad).

---

## 3. Reglas de Negocio y Supuestos

### 3.1 La ventana operativa: 14 horas

Fuente única de verdad del horario:

- `InicioOperativo = 8`, `FinOperativo = 22` → ventana **[08:00, 22:00) = 14 horas/día**.
- `HorasEnRangoOperativo(desde, hasta)` recorre hora por hora sumando **solo** las horas dentro de la ventana operativa; salta las horas nocturnas. Devuelve 0 si `hasta <= desde`.

### 3.2 Velocidad de vaciado — la fórmula ×14

Pre-filtro de ventas: `FechaLocal >= desde` (con `desde = Now.AddDays(-diasHistorial)`, default **14 días**), **excluye ventas fantasma**, y **filtro por hora del día** dentro de [08:00, 22:00).

Fórmula:

```
horasActivas     = HorasEnRangoOperativo(primeraVenta, ultimaVenta)
if (horasActivas < 1) horasActivas = 1        // guarda para ventanas sub-hora
velocidadPorHora = unidadesVendidas / horasActivas
velocidadDiaria  = velocidadPorHora × 14      // ×14 horas operativas, NO ×24
```

Si no hay primera/última venta o 0 unidades → `velocidad = 0`.

`TemplateRecargaAnalyticsService` calcula velocidad por clave `(MaquinaId, ProductoId)` como tasa **por hora** (`count / horasActivas`); el escalado ×14 ocurre después en el DTO.

### 3.3 Predicción de días-a-quiebre y rentabilidad de viaje

```
t                  = StockActual <= 0 ? 0 : StockActual / velocidad
diasHastaQuiebre   = t
esCritico          = t < UmbralCriticoDias                            // UmbralCriticoDias = 2.0
diasVacios         = Clamp(ventanaProyeccionDias − t, 0, ventanaProyeccionDias)
margen             = Max(0, PrecioVenta − (CostoPromedio ?? 0))
lcp                = margen × velocidad × diasVacios                  // Lucro Cesante Proyectado
```

- `UmbralCriticoDias = 2.0` ("Quiebre proyectado < 48h").
- `ventanaProyeccionDias` default = 3.

**Rollup por zona y "vale la pena viajar":**

```
LcpMaquina        = Σ LcpSlot
LcpTotal (zona)   = Σ LcpMaquina
costoBase         = zona?.CostoBaseViaje ?? 0
EsRentableViajar  = zona != null && LcpTotal > costoBase           // "Sin zona" (null) → forzado false
```

Zonas ordenadas por `LcpTotal − CostoBaseViaje` descendente.

### 3.4 Detección de quiebre de stock — dos métodos complementarios

**Método A — inferencia por silencio (`SalesAnalyticsService.GetStockoutAnalysisAsync`):**

- `umbralHorasSilencio` default = **14 horas operativas** (~24h de reloj).
- Por máquina: `ultimaActividad` = última venta de la máquina.
- Por (máquina, producto): `horasDiferencia = HorasEnRangoOperativo(ultimaVentaProducto, ultimaActividadMaquina)`; **`posibleQuiebre = horasDiferencia > 14`** — se infiere quiebre cuando un producto dejó de vender >14h operativas antes de la última actividad de la máquina.
- Usa el helper operativo (14h/día): `VelocidadDiaria = VelocidadPorHora × 14`; `DiasSinStock = HorasSinStock / 14`.
- Pérdida: `dineroPerdido = velocidadPorHora × horasSinStock × precioPromedio`.
- Dead slots: slots configurados con producto pero **sin ventas** en el período → `EsDeadSlot = true`, `PosibleQuiebre = StockActual <= StockMinimo`.

**Método B — inferencia por depleción de snapshot (`TemplateRecargaAnalyticsService`):**

- **`posibleQuiebre = cantidadVendida >= cantidadInicial`** — vendió ≥ el stock inicial del snapshot. Requiere `cantidadInicial > 0`.
- `fechaAgotamiento` = la venta que vació el slot.
- Velocidad efectiva: prefiere la de producto `(maquinaId, productoId)` (operativa ×14), fallback per-slot.

### 3.5 Sugerencias de compra

```
if (dias <= 0) dias = RotacionStockMinimoDias        // = 30
fechaInicio = Now.Date.AddDays(-dias)
sugerido    = ventasPeriodo − (stockEnMaquinas + StockBodega)   // clamp < 0 → 0
```

- `ventasPeriodo` = unidades vendidas en la ventana; `stockEnMaquinas` = Σ `StockActual`.
- Asume que el próximo período ≈ demanda de los últimos `dias`.
- Cacheado 5 días para evitar consultas excesivas a base de datos.

### 3.6 Umbrales de stock crítico

- Vía ConfiguracionSlots: **`StockActual <= StockMinimo && ProductoId != 0`**. `StockMinimo` default = 2 por slot.
- Vía Template: **`CantidadInicial <= 2`**.
- `Fuente` del DTO = `"template"` o `"configuracion"` según la vía.
- Flag `UseTemplateInventoryForStockCritico` conmuta la fuente.

### 3.7 Clasificación de productos

```
RotacionDiaria = CantidadVendida / diasDelPeriodo
Estrella  si RotacionDiaria >= RotacionAlta (1.0)
Joya/Cacho si < RotacionMedia (0.2), dividido por MargenAlto (0.50)
ABC: <=80 → A, <=95 → B, else C
```

Umbrales desde `AnalyticsThresholds.cs`: `RotacionAlta=1.0`, `RotacionMedia=0.2`, `MargenAlto=0.50`.

---

## 4. Optimización de Rutas

> **No existe algoritmo de ruteo / TSP / grafo.** El "optimizador" es una **compuerta de rentabilidad + ranking**, no un solver de rutas.

- Consulta `api/LogisticaPredictiva/zonas` y muestra las zonas.
- Heurística = LCP vs costo de viaje (semáforo Rentable/Pérdida), calculado server-side. No hay secuenciación, distancia ni capacidad de vehículo.
- Defaults: `DiasHistorial = 14`, `VentanaDias = 3`.
- `CriticosDe(zona)` cuenta `EsCritico && UnidadesFaltantes > 0`. "Generar Orden" se deshabilita con 0 críticos.
- **Generación de borrador:** de una zona, toma slots `EsCritico && UnidadesFaltantes > 0`, consolida por `(MaquinaId, ProductoId)`, `Cantidad = Σ UnidadesFaltantes`; crea **una** orden BORRADOR `"Rescate {zona} {dd/MM/yyyy}"`. Lanza si la zona no existe o no hay críticos.

---

## 5. Páginas Blazor (UI del dominio)

- **`OptimizadorRutas.razor`** (`/optimizador-rutas`, `[Authorize]`): tarjetas de zona con badge Rentable/Pérdida, `CostoBaseViaje`, `LcpTotal`, Δ, nº máquinas y nº slots críticos; tabla "Detalle de Rescate" + botón "Generar Orden de Carga" → BORRADOR.
- **`StockoutDashboard.razor(.cs)`** (`/stockout-dashboard`, `[Authorize]`): dos vías de datos (template `api/TemplateRecarga/{id}/analyze` y manual `api/Ventas/stockout-analysis`). Defaults `FechaInicio = Hoy−7`, `FechaFin = Hoy`, `UmbralHoras = 24`. Tabla de alertas agrupada por producto, KPIs, timeline top-6 por días sin stock, gráfico de barras de ventas diarias, y un **"mapa de agotamiento"** (esquema físico de la máquina que reproduce la depleción con un scrubber). Bandas de alerta: `>72h Crítico, >48h Alto, >24h Medio`.
- **`GestionMaquinas.razor`** (`/maquinas`, `[Authorize(Roles = Admin)]`): CRUD de máquinas incl. dropdown `ZonaLogisticaId` — cómo se asignan máquinas a zonas para todo el dominio.
- **`AnalisisProductos.razor`** (`/analisis-productos`): ranking de productos, ABC/Pareto, tendencia MoM/WoW.
