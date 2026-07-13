# 4 — Operación en Terreno: Cargas y Recargas

> Documentación viva. Refleja el estado del código a la fecha del último commit revisado.
> Documento adicional (no pedido originalmente) por tener dos máquinas de estados propias, un
> pipeline de OCR con constantes duras, y una superficie de UI móvil dedicada.

---

## 1. Resumen de Negocio

Este módulo es todo lo que pasa **fuera de la oficina**: el operario que carga producto en el camión, va a la máquina, la rellena y vuelve. Tiene dos sub-flujos entrelazados:

1. **Órdenes de carga (cargas):** la lista de picking de producto que sale de la bodega hacia las máquinas, con reconciliación de retorno (lo que no se cargó vuelve al camión y a bodega). Toca inventario y caja.
2. **Templates de recarga:** un "ciclo" de recarga por máquina que fotografía el inventario de cada slot (snapshot) para alimentar el análisis de quiebres. Opcionalmente se siembra con **OCR de una foto** de la planilla de recarga escrita a mano.

El valor: el operario en terreno no tipea nada. Saca una foto de la planilla, la IA la transcribe, y el sistema arma la orden. El estado **BORRADOR** permite planificar una carga sin comprometer stock hasta que alguien la confirme.

---

## 2. Entidades Clave (Data Model)

| Entidad | Rol | Relaciones |
| --- | --- | --- |
| **OrdenCarga** | Orden de reposición (picking). | `→ DetalleOrdenCarga` (1-N). `MaquinaId?` null = orden global/consolidada. |
| **DetalleOrdenCarga** | Línea de la orden. | `CantidadSolicitada`, `CantidadRetornada`, `CostoUnitario` (snapshot de `Producto.CostoPromedio`), `MaquinaId?` por línea. |
| **TemplateRecarga** | Ciclo de recarga. | `→ PeriodoRecarga` (1-N, uno por máquina) `→ SnapshotSlot` (1-N). |
| **PeriodoRecarga** | Un período de recarga de una máquina. | `FechaRecarga` (ancla). Guarda `FotoGuia` (≤10MB) y `FotoOcr` (≤5MB) como `varbinary(max)`. |
| **SnapshotSlot** | Foto del stock de un slot al recargar. | `CantidadInicial`, `CapacidadSlot`, `Estado` (`EstadoSlot`). |
| **ConfiguracionSlot** | Inventario vivo del slot (destino de ambos flujos). | `MaquinaId`, `ProductoId?`, `StockActual`, `CapacidadMaxima`. |

**Gotcha crítico — `PeriodoRecarga.FechaFin`:** es una **columna computada PERSISTED de SQL Server, NO un campo C#** (`TemplateRecarga.cs:61-70`). Se deriva como la `FechaRecarga` del período siguiente, o el centinela `2099-12-31`. En código, algunos mapeos usan un placeholder `FechaRecarga.AddYears(2)` explícitamente "no usado en contexto de lifecycle" (`:210`). Es frágil.

---

## 3. Reglas de Negocio y Supuestos (CRÍTICO)

### 3.1 Ciclo de vida BORRADOR de OrdenCarga (`OrdenCargaService.cs`)

Estados como **strings literales**: `"BORRADOR"` → `"PENDIENTE"` → `"FINALIZADA"`.

- **`CrearOrdenBorradorAsync`** (`:295-343`): crea borrador **sin validación ni descuento de stock**.
- **`ConfirmarOrdenAsync`** (`:345-384`): exige `Estado == "BORRADOR"` (si no, `InvalidOperationException("La orden no está en estado BORRADOR.")`), valida y **descuenta `StockBodega`** por línea, transiciona a `"PENDIENTE"`.
- **`CrearOrdenAsync`** (`:22-64`): crea directo como `"PENDIENTE"` y descuenta stock de inmediato (salvo `dto.IgnorarStock`).
- **`FinalizarOrdenAsync`** (`:69-147`): PENDIENTE → `"FINALIZADA"`. Al finalizar:
  - `CantidadRetornada` no puede exceder `CantidadSolicitada` (throw, `:85-88`).
  - Retornos se **suman de vuelta** a `StockBodega` (`:95`).
  - Cargado (`Solicitada − Retornada`) se suma a `ConfiguracionSlot.StockActual`, **topeado en `CapacidadMaxima`** (`:110-113`).
  - Escribe un `MovimientoCaja` GASTO / categoría `"MERCADERIA"` de `−costoTotal`, vinculado por `OrdenCargaId` (`:131-139`) — **el puente hacia el dominio financiero**.

> **Por qué existe BORRADOR:** permite redactar una orden consolidada **sin tocar inventario** hasta que un humano la confirme. El caller de producción es `LogisticaPredictivaService.CrearOrdenRescateZonaAsync` (`:214`): las órdenes "Rescate {zona}" auto-generadas desde quiebres proyectados (<48h) se crean como borrador para revisión humana antes de comprometer stock.

**Sugerencia de carga** (`GetSugerenciaCargaAsync`, `:196-271`): slots donde `StockActual < CapacidadMaxima`, ordenados por `NumericStringComparer` (códigos numéricos en orden numérico, alfanuméricos después).

### 3.2 Máquina de estados de TemplateRecarga (`EstadoTemplate`: `Pendiente=0`, `Terminado=2`)

- El valor **1 (`Activo`) es legacy** y se maneja defensivamente en 3 lugares (`TemplateRecargaLifecycleService.cs:93,230`).
- **`TerminarAsync`** (`:28-55`): exige `Pendiente` (si no, throw); al terminar **sincroniza SnapshotSlots → ConfiguracionSlots**. Defaults para slots nuevos: `CapacidadMaxima = CapacidadSlot > 0 ? CapacidadSlot : 10`, `StockMinimo = 2`, `PrecioVenta = 0` (`:160-169`).
- **`ReabrirAsync`** (`:57-84`): exige `Terminado` → vuelve a `Pendiente`.
- **`NormalizarEstado`** (`:230-233`): fuerza cualquier valor fuera de {0,2} a `Terminado(2)`.
- **Regla de alimentación de stock-crítico:** solo el **último `Terminado` por máquina** alimenta el análisis de quiebre (`GetLatestTerminadoTemplateSlotsAsync`, `:86-96`; también acepta el legacy `Estado == 1`).
- Concurrencia optimista vía `TemplateRecarga.RowVersion` `[Timestamp]` (`:37`).

`EstadoSlot` enum: `Vacio=0`, `Pendiente=1`, `Lleno=2`.

### 3.3 Pipeline de OCR fotográfico (constantes duras)

Endpoint `POST api/OrdenCarga/from-photo` (`OrdenCargaController.cs:147`) → `RecargaOcrService.ExtractRecargaDataAsync` postea la imagen a `{ScraperServiceUrl}/api/ocr/recarga` (`RecargaOcrService.cs:60`).

**Lado Python** (`gemini_ocr.py:170-195`): modelo **`gemini-3-flash-preview`**, prompt en español que extrae pares `{slot_number, quantity}`; cantidad 0 es válida; en duplicados gana el último escrito.

**Matching en C#** (`RecargaOcrService.cs`):

- Cantidades **topeadas en 100** (>100 = error OCR) (`:105-111`).
- **Matching por offset primero** (máquinas de extensión muestran 1-100 en la foto para slots 101-200): `offset = min(máquina) − min(OCR)`, aplicado solo si `0 < offset <= 1000`, confianza **0.7** (`:137-172`).
- **Fallback fuzzy**: slots alfanuméricos exigen match exacto (conf 1.0); slots numéricos usan Levenshtein **distancia ≤ 2**, confianza `1 − dist/maxLen`, aceptado solo si **≥ 0.6** (`:229-276`).

---

## 4. Flujo Técnico

**Servicios:** `OrdenCargaService`, `TemplateRecargaService`, `TemplateRecargaLifecycleService`, `TemplateRecargaAnalyticsService`, `RecargaOcrService`, `OrdenCargaExcelService`.

**Controladores:** `OrdenCargaController`, `TemplateRecargaController`.

**Páginas Blazor:**

- `Cargas.razor(.cs)` — confirmación de borrador en `ConfirmarBorrador` (`:359`).
- `RecargaMovil.razor.cs` — móvil, enum `View` (`List`/`Overview`/`PickMachine`/`EditSlots`), colapsa la navbar por vista (`:38-53`).
- `ConciliacionMovil.razor.cs`, `TemplatesRecarga.razor`.

**Componentes:** `FotoGuiaPanel`, `FotoRecargaModal`.

---

## 5. Riesgos y Deuda Técnica Conocida

- **`OrdenCargaController` y `TemplateRecargaController` SIN `[Authorize]`** (ver docs 1 y 3): `ConfirmarOrden` (`:130`), `finalizar`, `CrearOrden` (`:14`, además sin `[HttpPost]`) son alcanzables sin autenticar. Igual `TemplateRecarga.analyze` / `slot-timeline`.
- **Estado legacy `Activo=1`** persiste en DB y requiere normalización defensiva en 3 lugares.
- **`FechaFin` como columna computada PERSISTED** (no campo C#) es frágil; el placeholder `FechaRecarga.AddYears(2)` en `MapToDto` (`:210`) es un parche.
- **Strings de estado libres** en OrdenCarga (`BORRADOR`/`PENDIENTE`/`FINALIZADA`) — sin enum, propenso a typos.
- **Confianza en la salida de Gemini:** el OCR de recarga no valida más allá del tope de 100 y los umbrales de matching.
