# 2 — Inteligencia Financiera

> Documentación viva. Refleja el estado del código a la fecha del último commit revisado.

---

## 1. Resumen de Negocio

Este módulo responde la pregunta que le importa al gerente: **¿estamos ganando plata, cuánto, y de dónde sale o se fuga?**

Toma tres flujos de dinero y los reconcilia:

1. **Ingresos** — lo que venden las máquinas (viene del pipeline de ingesta).
2. **Costo de la mercadería** — cuánto costó lo que se vendió, usando costo promedio ponderado.
3. **Gastos** — compras a proveedores (facturas), gastos recurrentes (arriendo, internet, sueldos), peajes, comisiones, mermas.

Con eso arma el estado de resultado: **margen bruto** (ingresos − costo) y **utilidad operacional** (margen − gastos). Además digitaliza facturas de proveedores por foto (OCR con IA) para que cargar una compra sea sacar una foto, no tipear línea por línea, y mantiene un **cierre de período contable** que "congela" un mes una vez cuadrado para que nadie edite el pasado.

El valor: reemplaza la planilla Excel manual del contador por un sistema que calcula el resultado en vivo, con trazabilidad de cada peso hasta su factura o transferencia de origen.

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

## 3. Reglas de Negocio y Supuestos

### 3.1 Costo de producto = Promedio Ponderado (CPP), NO FIFO

Fórmula:

```
CPP = ((StockActual × CostoPromedioActual) + (NuevaCantidad × NuevoCosto))
      / (StockActual + NuevaCantidad)
```

- Solo se aplica cuando `nuevoStockTotal > 0` y la línea no es pendiente.
- La fórmula está **centralizada** en `Core/Domain/CalculadoraCostos.cs` como único punto de verdad, eliminando la duplicación que existía anteriormente.
- Reversión al editar/eliminar: `CostoPromedio = Max(0, (valorTotalActual − valorARestar) / nuevoStock)`; si `nuevoStock ≤ 0` → stock y costo se resetean a 0.

**`ProductoCosto`** es un ledger temporal paralelo: en cada compra se cierra la fila abierta (`FechaHasta = FechaCompra`) y se inserta una nueva abierta con `Costo = CostoUnitario`. Se usa para consultar el costo a una fecha puntual en analítica: fila donde `FechaDesde <= fecha && (FechaHasta == null || FechaHasta > fecha)`, ordenada `FechaDesde DESC`; si no hay, el caller cae a `CostoPromedio`.

### 3.2 Márgenes y utilidad

Existen dos servicios que calculan el resultado financiero desde perspectivas complementarias:

**A) `SalesAnalyticsService.GetInformeFinancieroAsync` → `InformeFinancieroDto`:**

```
MargenBruto       = ingresosVentas − costoVentas
UtilidadNeta      = margenBruto − mermas − gastosOperativos
MargenPorcentaje  = ingresos > 0 ? ((ingresos − costo) / ingresos) × 100 : 0
```

- Costo por venta usa `v.CostoVenta`, con fallback a `v.Producto.CostoPromedio` si es 0.
- Excluye ventas fantasma; solo cuenta ventas `Pagado`.
- `gastosOperativos` y `mermas` solo se computan a nivel global; las categorías y mermas salen del catálogo central `CategoriasGasto`.

**B) `CajaBusinessService.GetResumenAsync` → `CajaResumenDto`:**

```
margenBruto          = monthIngresosVentas − monthCostoVenta
utilidadOperacional  = margenBruto − mermasAbs − totalGastosOps
costoTransbank       = cantVentasTB × TransbankFee
```

Ambos servicios comparten el catálogo central `CategoriasGasto` en `Shared/` (buckets `Variables`, `Fijos`, `Operacionales`) y usan la misma fórmula `CalcularUtilidadOperacional`.

### 3.3 Ciclo de período contable (`Abierto=0`, `Cerrado=1`)

4 compuertas secuenciales antes de cerrar:

1. Todas las Transferencias deben estar `Verificada`.
2. Todas las Compras deben estar `Verificada`.
3. `saldoADevolver == 0`, donde `diferencia = totalTransferido − totalCompras − totalGastos` y `saldoADevolver = diferencia − devuelto`.
4. Auto-concilia transferencias con ítems vinculados y exige que todas estén `Conciliado`.

`Cerrado` es terminal y de solo lectura: bloquea edición de compras, gastos y el propio período.

**Enforcement del candado:** `CajaBusinessService.IsMonthLockedAsync(month, year)` consulta si existe algún `AccountingPeriod` con `Estado == Cerrado` cuyo rango (`FechaInicio`..`FechaFin`) cubra total o parcialmente el mes consultado. Si el mes está cubierto por un período cerrado, el candado devuelve `true` y se rechaza:
- El registro de nuevos `MovimientoCaja` (`RegistrarMovimientoAsync`).
- El campo `IsLocked` del resumen de caja (`CajaResumenDto`), usado por el frontend para deshabilitar acciones.

El método está unificado en `CajaBusinessService` como fuente única de verdad; `CajaService` expone el mismo método vía `ICajaService.IsMonthLockedAsync` por delegación.

### 3.4 Gastos recurrentes — manual, mensual, monto completo

- Lista gastos activos que **aún no** tienen un `MovimientoCaja` con ese `GastoRecurrenteId` en el mes.
- Al aplicar, evita duplicados por mes, luego postea **un** `MovimientoCaja`: `Monto = −Math.Abs(montoReal ?? MontoEstimado)`, `Tipo="GASTO"`, `Categoria = gasto.Categoria`, con fecha del día actual acotada al fin de mes.
- Vinculación a máquina vía `MaquinaId?` opcional (global si null).

### 3.5 Compras y OCR de facturas

- **Estados de Compra** son strings (`PAGADA`/`PENDIENTE`). Va a caja cuando `Estado == "PAGADA"`.
- **Resolución de categoría:** `MERCADERIA` → `"MERCADERIA"`; si no, `SubcategoriaGasto` explícita; si no, inferencia por palabra clave del proveedor (bencina/copec → `LOGISTICA`; peaje/autopista → `PEAJES`; default `GASTOS GENERALES`).
- **OCR:** postea la imagen al microservicio de scraping en `/api/ocr/invoice`. EAN validado 8–13 dígitos. División por pack cuando `ProductoEAN.PackSize > 1`.
  - Python: modelo **Gemini**. El prompt incluye reglas tributarias chilenas: IVA **19%**, ILA **18%** (azucaradas) / **10%** (zero/light); invariante `costo_unitario = neto × 1.19`. Combustible → ítem único sin desglose.
- **Match de proveedor:** exacto por alias → exacto canónico normalizado → fuzzy tokenizado. Umbral default **0.6**. `ProveedorAlias` con clave normalizada única; `ProveedorCatalog` es el canónico curado por el dueño.
- **Devoluciones:** una por período/rendición abierto; `Monto > 0`; no puede exceder el saldo disponible; postea `MovimientoCaja` inverso con `Tipo="APORTE"`, `Categoria="DEVOLUCION_RENDICION"`. Las categorías estructurales se excluyen de los totales de gasto vía `CategoriasGasto.Estructurales`.

---

## 4. Flujo Técnico

**Servicios:**

- `ContabilidadService` — orquesta transferencias, cuadres, compras/gastos vinculados, períodos, verificación, devoluciones y la grilla de conciliación global.
- `CompraService` — registro de compras + impacto en inventario/costo (CPP + `ProductoCosto`).
- `SalesAnalyticsService` — motor de informe financiero y analítica de productos (clasificación ABC 80/95, estrella/joya/cacho).
- `CajaBusinessService` — motor de resumen de caja (margen, utilidad operacional, fee Transbank).
- `GastoRecurrenteService` — aplicación mensual de gastos recurrentes.
- `FacturaOcrService` — puente al OCR Python.
- `ProveedorMatchingService` — matching de proveedores.
- `InformesService` — CRUD de archivos de reporte (sin matemática).

**Controladores:** `CajaController`, `ComprasController` (+ rol admin), `ContabilidadController`, `GastoRecurrenteController` (+ `Roles="Admin"` en mutaciones), `ProveedoresController` (+ `Policy="RequireAdmin"`), `VentasController`.

**Páginas Blazor:**

- `InformeVentas.razor` → `/informe-ventas` (KPIs Utilidad/Margen).
- `CajaV2.razor` → `/caja` + `/caja-v2` (Margen bruto, Resultado operacional, utilidad operacional). `Caja.razor` → `/caja-legacy`.
- `Compras.razor` → `/compras`; `NuevaCompra.razor` → `/compras/nueva`; `EditarCompra.razor` → `/compras/editar/{Id}`.
- `PurchaseReport.razor` → `/informe-compras`.
- `AnalisisProductos.razor` → `/analisis-productos`.
- `Conciliacion.razor` → `/contabilidad` (+ `ConciliacionMovil.razor`).
- `Admin/Proveedores.razor` → `/admin/proveedores`.
- La UI de GastoRecurrente vive dentro de `CajaV2.razor` / `Caja.razor` (sin página propia).

---

## 5. Configuración

Parámetros relevantes configurados en `VendingConfig.cs`: `CajaStartDate`, `TransbankFee`, `PeriodCacheDurationMinutes = 5`. Umbral de matching de proveedor 0.6; clasificación ABC 80/95; tolerancia Levenshtein 2/3.
