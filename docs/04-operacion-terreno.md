# 4 — Operación en Terreno: Cargas y Recargas

> Documentación viva. Refleja el estado del código a la fecha del último commit revisado.
> Cubre dos máquinas de estados propias, un pipeline de OCR, y una superficie de UI móvil dedicada.

---

## 1. Resumen de Negocio

Este módulo cubre lo que pasa **fuera de la oficina**: el operario que carga producto en el camión, va a la máquina, la rellena y vuelve. Tiene dos sub-flujos entrelazados:

1. **Órdenes de carga (cargas):** la lista de picking de producto que sale de la bodega hacia las máquinas, con reconciliación de retorno (lo que no se cargó vuelve a bodega). Toca inventario y caja.
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

---

## 3. Reglas de Negocio y Supuestos

### 3.1 Ciclo de vida BORRADOR de OrdenCarga

Estados: `"BORRADOR"` → `"PENDIENTE"` → `"FINALIZADA"`.

- **Crear borrador:** sin validación ni descuento de stock.
- **Confirmar:** exige `BORRADOR`, valida y **descuenta `StockBodega`** por línea, transiciona a `PENDIENTE`.
- **Crear directo como PENDIENTE:** descuenta stock de inmediato.
- **Finalizar (PENDIENTE → FINALIZADA):**
  - `CantidadRetornada` no puede exceder `CantidadSolicitada`.
  - Retornos se **suman de vuelta** a `StockBodega`.
  - Cargado (`Solicitada − Retornada`) se suma a `ConfiguracionSlot.StockActual`, **topeado en `CapacidadMaxima`**.
  - Escribe un `MovimientoCaja` GASTO / categoría `"MERCADERIA"` — **el puente hacia el dominio financiero**.

**Por qué existe BORRADOR:** permite redactar una orden consolidada **sin tocar inventario** hasta que un humano la confirme. Las órdenes auto-generadas desde quiebres proyectados (<48h) se crean como borrador para revisión humana antes de comprometer stock.

**Sugerencia de carga:** slots donde `StockActual < CapacidadMaxima`, ordenados por código numérico.

### 3.2 Máquina de estados de TemplateRecarga (`Pendiente=0`, `Terminado=2`)

- **Terminar:** exige `Pendiente`; al terminar **sincroniza SnapshotSlots → ConfiguracionSlots**. Defaults para slots nuevos: `CapacidadMaxima = 10`, `StockMinimo = 2`, `PrecioVenta = 0`.
- **Reabrir:** exige `Terminado` → vuelve a `Pendiente`.
- Solo el **último `Terminado` por máquina** alimenta el análisis de quiebre.
- Concurrencia optimista vía `TemplateRecarga.RowVersion` `[Timestamp]`.

`EstadoSlot` enum: `Vacio=0`, `Pendiente=1`, `Lleno=2`.

### 3.3 Pipeline de OCR fotográfico

Endpoint `POST api/OrdenCarga/from-photo` postea la imagen al microservicio de scraping en `/api/ocr/recarga`.

**Lado Python:** modelo **Gemini**, prompt en español que extrae pares `{slot_number, quantity}`; cantidad 0 es válida; en duplicados gana el último escrito.

**Matching en C#:**

- Cantidades **topeadas en 100** (valores mayores se consideran error de OCR).
- **Matching por offset primero** (para máquinas de extensión cuyos slots se numeran corridos en la foto), con confianza 0.7.
- **Fallback fuzzy:** slots alfanuméricos exigen match exacto; slots numéricos usan distancia Levenshtein ≤ 2 con confianza ≥ 0.6.

---

## 4. Flujo Técnico

**Servicios:** `OrdenCargaService`, `TemplateRecargaService`, `TemplateRecargaLifecycleService`, `TemplateRecargaAnalyticsService`, `RecargaOcrService`, `OrdenCargaExcelService`.

**Controladores:** `OrdenCargaController`, `TemplateRecargaController`.

**Páginas Blazor:**

- `Cargas.razor(.cs)` — confirmación de borrador.
- `RecargaMovil.razor.cs` — móvil, navegación por vistas (`List`/`Overview`/`PickMachine`/`EditSlots`).
- `ConciliacionMovil.razor.cs`, `TemplatesRecarga.razor`.

**Componentes:** `FotoGuiaPanel`, `FotoRecargaModal`.
