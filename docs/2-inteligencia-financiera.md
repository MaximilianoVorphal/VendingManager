# 2 — Inteligencia Financiera

> Documentación viva. Refleja el estado del código a la fecha del último commit revisado.
> No inventa reglas: cada valor hardcodeado y cada divergencia entre implementaciones está señalada.

---

## 1. Resumen de Negocio

Este módulo responde la pregunta que le importa al gerente: **¿estamos ganando plata, cuánto, y de dónde sale o se fuga?**

Toma tres flujos de dinero y los reconcilia:

1. **Ingresos** — lo que venden las máquinas (viene del pipeline de ingesta).
2. **Costo de la mercadería** — cuánto costó lo que se vendió, usando costo promedio ponderado.
3. **Gastos** — compras a proveedores (facturas), gastos recurrentes (arriendo, internet, sueldos), peajes, comisiones, mermas.

Con eso arma el estado de resultado: **margen bruto** (ingresos − costo) y **utilidad operacional** (margen − gastos). Además digitaliza facturas de proveedores por foto (OCR con IA) para que cargar una compra sea sacar una foto, no tipear línea por línea, y mantiene un **cierre de período contable** que "congela" un mes una vez cuadrado para que nadie edite el pasado.

El valor: reemplaza la planilla Excel manual del contador por un sistema que calcula el resultado en vivo, con trazabilidad de cada peso hasta su factura o transferencia de origen.

> **⚠️ Importante para el gerente:** el sistema **NO calcula depreciación de máquinas ni prorrateo de activos**. Esa funcionalidad (EBITDA con vida útil de activos) fue **eliminada** del sistema (ver §6). Lo que hoy se muestra etiquetado "EBITDA" es en realidad la utilidad operacional **sin** ningún término de depreciación.

---

## 2. Entidades Clave (Data Model)

| Entidad | Rol | Notas |
| --- | --- | --- |
| **Compra** | Factura de proveedor. | `Estado` (`PAGADA`/`PENDIENTE`, default `PAGADA`), `TipoFactura` (`MERCADERIA`/`GASTO_GENERAL`). FKs nullable a `TransferenciaId`, `ProveedorCatalogId` (null = pendiente de asignar). |
| **DetalleCompra** | Línea de una factura. | `Cantidad`, `CostoUnitario`, `Subtotal` (`decimal(18,2)`), `Ean`/`Sku` (aprendizaje OCR). |
| **ProductoCosto** | Ledger temporal de costos. | `Costo`, `FechaDesde`, `FechaHasta?` (null = fila vigente/abierta). |
| **GastoRecurrente** | Plantilla de gasto mensual. | `MontoEstimado`, `Categoria` (default `INTERNET`), `Activo`, `MaquinaId?` (null = global). |
| **MovimientoCaja** | Asiento del libro de caja. | Convención de signo: **positivo = entra, negativo = sale**. FKs a `OrdenCargaId`, `CompraId`, `GastoRecurrenteId`, `RendicionId`. |
| **AccountingPeriod** | Período contable (mes). | `Estado` (`Abierto=0`/`Cerrado=1`, terminal). |
| **Devolucion** | Devolución de saldo. | `Monto` siempre positivo; postea un `MovimientoCaja` inverso. |
| **Informe** | Archivo de reporte subido (blob). | Sin lógica financiera; solo CRUD de archivos. |

**Relaciones clave:** `Compra → Transferencia → AccountingPeriod` (una compra se ancla a un período **indirectamente** vía la transferencia que la pagó). `Compra → DetalleCompra` (1-N). Cada compra pagada genera un `MovimientoCaja` y actualiza `Producto.CostoPromedio` + una fila `ProductoCosto`.

---

## 3. Reglas de Negocio y Supuestos (CRÍTICO)

### 3.1 Costo de producto = Promedio Ponderado (CPP), NO FIFO

Fórmula, citada de `Core/Domain/CalculadoraCostos.cs` (`ApplyPurchase`, `:17`):

```
CPP = ((StockActual × CostoPromedioActual) + (NuevaCantidad × NuevoCosto))
      / (StockActual + NuevaCantidad)
```

- Solo se aplica cuando `nuevoStockTotal > 0` y la línea no es `EsPendiente`.
- La fórmula está **centralizada** en `Core/Domain/CalculadoraCostos.cs` (clase estática, `ApplyPurchase` + `RevertPurchase`). Los 6 sitios inline originales fueron reemplazados por 5 llamadas a este único punto de verdad: `ApplyPurchase` en `CompraService.cs:99,255,601` y `ContabilidadService.cs:229`; `RevertPurchase` en `CompraService.cs:409`. El caller (`CompraService`, `ContabilidadService`) mantiene la responsabilidad del ledger `ProductoCosto`.
- Reversión al editar/eliminar (`RevertPurchase`): `CostoPromedio = Math.Max(0, (valorTotalActual − valorARestar) / nuevoStock)`; si `nuevoStock ≤ 0` → stock y costo se resetean a 0.

**`ProductoCosto`** es un ledger temporal paralelo: en cada compra se cierra la fila abierta (`FechaHasta = FechaCompra`) y se inserta una nueva abierta con `Costo = CostoUnitario` (`CompraService.cs:116-137`). Se usa para consultar el costo a una fecha puntual en analítica. Lookup en `ProductoCostoExtensions.GetCostoAtAsync` (`:12`): fila donde `FechaDesde <= fecha && (FechaHasta == null || FechaHasta > fecha)`, ordenada `FechaDesde DESC`; si no hay, el caller cae a `CostoPromedio`.

> `RecalcularCostosHistoricosAsync` (`VentasService.cs:63`) está marcado **`[Obsolete]`** ("deprecated. Use ProductoCosto-based sync instead") pero sigue en el binario.

### 3.2 Depreciación / prorrateo: NO EXISTE

No hay lógica de `prorrate` / `deprecia` / `amortiz` en `GastoRecurrenteService` ni `ContabilidadService` (grep limpio fuera de migraciones). Los gastos recurrentes se contabilizan **por monto completo del mes, no prorrateados** (ver §3.5). Ver §6 para la eliminación de EBITDA.

### 3.3 Márgenes / utilidad — DOS implementaciones independientes

Existen **dos motores de resultado** que no comparten fuente de verdad:

**A) `SalesAnalyticsService.GetInformeFinancieroAsync`** (`:241-308`) → `InformeFinancieroDto`:

```
MargenBruto       = ingresosVentas − costoVentas
UtilidadNeta      = CalcularUtilidadOperacional(margenBruto, mermas, gastosOperativos)
                  = margenBruto − mermas − gastosOperativos
MargenPorcentaje  = ingresos > 0 ? ((ingresos − costo) / ingresos) × 100 : 0
```

- Costo por venta: `v.CostoVenta`, con fallback a `v.Producto.CostoPromedio` si es 0.
- Excluye ventas fantasma `TB-EXTRA` / `TB-SIN-VENTA`; solo cuenta ventas `Pagado`.
- `gastosOperativos` y `mermas` solo se computan cuando `maquinaId == 0`; las categorías salen de `CategoriasGasto.Operacionales` (`:277-283`) y las mermas de `Categoria == "MERMA"` (`:288-295`). Filtra por `Fecha >= CajaStartDate` y `Monto < 0`.

**B) `CajaBusinessService.GetResumenAsync`** (`:41-134`) → `CajaResumenDto`:

```
margenBruto          = monthIngresosVentas − monthCostoVenta
utilidadOperacional  = margenBruto − mermasAbs − totalGastosOps   ← resultado operacional
utilidadNetaReal     = utilidadOperacional
costoTransbank       = cantVentasTB × TransbankFee   (fee default 80)
```

- Buckets de categoría via `CategoriasGasto.Variables` (`:90`) y `CategoriasGasto.Fijos` (`:96`); `MERCADERIA` (`:74`) y `MERMA` (`:82`) siguen como literales de query.
- **Bloqueo de mes muerto:** `IsMonthLockedStatic` calcula un `lockDate` pero **siempre `return false`** ("Actualmente deshabilitado", `:196`). El único candado real es el cierre de período.

> **Resuelto en consolidacion-financiera (jul 2026):** ambos servicios consumen el catálogo central `CategoriasGasto` en `Shared/` (buckets `Variables`, `Fijos`, `Operacionales` = unión de ambos), y usan la misma fórmula `CalcularUtilidadOperacional`. Ya no hay listas duplicadas que mantener en sync.

### 3.4 Ciclo de período contable (`AccountingPeriodEstado`: `Abierto=0`, `Cerrado=1`)

`ClosePeriodoAsync` (`ContabilidadService.cs:838-914`) — 3 compuertas secuenciales antes de cerrar:

1. Todas las Transferencias `Verificada` (`:850-856`).
2. Todas las Compras `Verificada` (`:859-866`).
3. `saldoADevolver == 0`, donde `diferencia = totalTransferido − totalCompras − totalGastos` y `saldoADevolver = diferencia − devuelto` (`:869-887`).
4. Auto-concilia transferencias con ítems vinculados y luego exige que todas estén `Conciliado` (`:890-910`).

`Cerrado` es terminal y de solo lectura: bloquea edición de compras, gastos y el propio período (`:591,654,827`).

### 3.5 Gastos recurrentes — manual, mensual, monto completo (`GastoRecurrenteService.cs`)

- `GetPendientesDelMesAsync(mes, año)` (`:66`) lista gastos activos que **aún no** tienen un `MovimientoCaja` con ese `GastoRecurrenteId` en el mes.
- `AplicarGastoAsync` (`:112`) evita duplicados por mes, luego postea **un** `MovimientoCaja`: `Monto = −Math.Abs(montoReal ?? MontoEstimado)`, `Tipo="GASTO"`, `Categoria = gasto.Categoria`, con fecha del día actual acotada al fin de mes.
- **Sin reparto entre meses, sin calendario de depreciación.** Vinculación a máquina vía `MaquinaId?` opcional (global si null).

### 3.6 Compras y OCR de facturas

- **Estados de Compra** son strings libres (`PAGADA`/`PENDIENTE`), no enum. Va a caja cuando `Estado == "PAGADA" && PagadaCaja` (`CompraService.cs:161`).
- **Resolución de categoría** (`ResolverCategoriaMovimiento`, `:738-760`): `MERCADERIA` → `"MERCADERIA"`; si no, `SubcategoriaGasto` explícita; si no, **inferencia por palabra clave del proveedor** (frágil): bencina/copec/shell/petro → `LOGISTICA`; peaje/autopista/tag/costanera/vespucio → `PEAJES`; default `GASTOS GENERALES`.
- **OCR** (`FacturaOcrService.cs:28`): postea la imagen a `{ScraperServiceUrl}/api/ocr/invoice`. EAN validado 8–13 dígitos. División por pack cuando `ProductoEAN.PackSize > 1`.
  - Python (`gemini_ocr.py`): modelo **`gemini-3-flash-preview`**, thinking `HIGH`. El prompt **hardcodea reglas tributarias chilenas**: IVA **19%**, ILA **18%** (azucaradas) / **10%** (zero/light); invariante `costo_unitario = neto × 1.19` (`:60,67-68,85`). Combustible → ítem único sin desglose.
  - **El lado .NET nunca valida ni recomputa impuestos: confía en la salida de Gemini.**
- **Match de proveedor** (`ProveedorMatchingService.MatchAsync`, `:35`): exacto por alias (conf 1.0) → exacto canónico normalizado (conf 1.0) → fuzzy tokenizado. Umbral default **0.6**. `ProveedorAlias` con clave normalizada única indexada; `ProveedorCatalog` es el canónico curado por el dueño.
- **Devoluciones** (`RegistrarDevolucionAsync`, `:985-1136`): una por período/rendición abierto; `Monto > 0`; no puede exceder el saldo disponible; postea `MovimientoCaja` inverso con `Tipo="APORTE"`, `Categoria="DEVOLUCION_RENDICION"`. `DEVOLUCION_RENDICION` y el legacy `RETIRO_CAPITAL` son **categorías estructurales** excluidas de los totales de gasto via `CategoriasGasto.Estructurales` (`:754`).

---

## 4. Flujo Técnico

**Servicios:**

- `ContabilidadService` — orquesta transferencias, cuadres, compras/gastos vinculados, períodos, verificación, devoluciones y la grilla de conciliación global (`GetConciliacionGlobalAsync`, `:1140`).
- `CompraService` — registro de compras + impacto en inventario/costo (CPP + `ProductoCosto`).
- `SalesAnalyticsService` — motor de informe financiero A y analítica de productos (clasificación ABC 80/95, estrella/joya/cacho por `AnalyticsThresholds`).
- `CajaBusinessService` — motor de resumen de caja B (margen, utilidad operacional, fee Transbank).
- `GastoRecurrenteService` — aplicación mensual de gastos recurrentes.
- `FacturaOcrService` — puente al OCR Python.
- `ProveedorMatchingService` — matching de proveedores.
- `InformesService` — CRUD de archivos de reporte (sin matemática).

**Controladores** (todos `[Authorize]` salvo el señalado en §6): `CajaController`, `ComprasController` (+ rol admin), `ContabilidadController`, `GastoRecurrenteController` (+ `Roles="Admin"` en mutaciones), `ProveedoresController` (+ `Policy="RequireAdmin"`), `VentasController`.

**Páginas Blazor** (`src/VendingManager.Web/Pages`):

- `InformeVentas.razor` → `/informe-ventas` (KPIs Utilidad/Margen).
- `CajaV2.razor` → `/caja` + `/caja-v2` (Margen bruto, "Resultado operacional" en `:517`, utilidad operacional/neta). `Caja.razor` → `/caja-legacy`.
- `Compras.razor` → `/compras`; `NuevaCompra.razor` → `/compras/nueva`; `EditarCompra.razor` → `/compras/editar/{Id}`.
- `PurchaseReport.razor` → `/informe-compras`.
- `AnalisisProductos.razor` → `/analisis-productos`.
- `Conciliacion.razor` → `/contabilidad` (+ `ConciliacionMovil.razor`).
- `Admin/Proveedores.razor` → `/admin/proveedores`.
- La UI de GastoRecurrente vive dentro de `CajaV2.razor` / `Caja.razor` (sin página propia).

---

## 5. Configuración (`VendingConfig.cs`)

Defaults hardcodeados relevantes: `CajaStartDate = 2026-01-01` (`:5`), `TransbankFee = 80` (`:7`), `PeriodCacheDurationMinutes = 5` (`:36`). Umbral de matching 0.6; ABC 80/95; Levenshtein 2/3.

---

## 6. Riesgos y Deuda Técnica Conocida

- **EBITDA / depreciación ELIMINADO (Fase diferida).** La migración `20260713165734_CleanupEbitda` (13-jul-2026) dropeó las tablas `DepreciacionesMaquina` y `DepreciacionesMaquinaHistory`, la columna `MovimientosCaja.MaquinaId` (+ FK/índice) y las columnas `Maquinas.FechaBaja` / `FechaInstalacion`. Los campos del modelo de depreciación ya no existen en código vivo. ✅ **Resuelto en consolidacion-financiera:** las etiquetas cosméticas restantes ("EBITDA" en UI y comentarios) fueron reemplazadas por "Resultado operacional".
- **`InformesController` SIN `[Authorize]`** (`:11-13`): sus endpoints de descarga/subida de reportes financieros están **sin autenticar**, a diferencia de todos los demás controladores financieros.
- ✅ **Resuelto — Dos motores de utilidad/margen:** `SalesAnalyticsService` y `CajaBusinessService` ahora comparten el catálogo `CategoriasGasto` y la misma fórmula `CalcularUtilidadOperacional` via `Shared/CategoriasGasto.cs`.
- ✅ **Resuelto — CPP duplicado:** centralizado en `Core/Domain/CalculadoraCostos.cs`. Ya no hay sitios inline en `CompraService` ni `ContabilidadService`.
- **Impuestos hardcodeados solo en el prompt Python** (IVA 19%, ILA 18%/10%); .NET confía sin recomputar.
- **Bloqueo de mes muerto:** `IsMonthLockedStatic` siempre devuelve `false`; los períodos cerrados son la única inmutabilidad real.
- **Método obsoleto aún embarcado:** `RecalcularCostosHistoricosAsync` (`VentasService.cs:63`).
- **Inferencia de categoría por keyword del proveedor** (`CompraService.cs:750-758`) es matching de strings frágil.
- **Concurrencia:** solo `ActualizarMontoTransferenciaAsync` maneja `DbUpdateConcurrencyException` (`:560`); otras rutas de mutación no.
- **Sin leasing/intereses:** no hay marcadores "Fase 2 / leasing / interés" en el código (grep limpio). Nunca se implementó; lo más cercano fue el modelo de depreciación ya eliminado.
