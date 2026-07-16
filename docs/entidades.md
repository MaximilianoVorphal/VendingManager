# Modelo de Datos — VendingManager

> **26 entidades de dominio** + **12 tablas históricas de auditoría (rollback)**.  
> ORM: Entity Framework Core sobre SQL Server 2022.

---

## Diagrama de Dominios

Las entidades se agrupan en seis dominios de negocio: **Inventario**, **Ventas**, **Finanzas**, **Operaciones**, **Proveedores**, y **Auditoría y Sistema**.

```
                    ┌──────────────────────────────────────────────────┐
                    │                 INVENTARIO                       │
                    │  Producto ◄── ProductoEAN                        │
                    │     │                                            │
                    │     └── ProductoCosto (historial CPP)            │
                    │                                                  │
                    │     Maquina ◄── ConfiguracionSlot ◄── Producto   │
                    │     │                                            │
                    │     └── ZonaLogistica                            │
                    └──────────┬───────────────────────────────────────┘
                               │
                ┌──────────────┼──────────────────┐
                ▼              ▼                   ▼
    ┌───────────────────┐ ┌────────────┐ ┌────────────────────┐
    │      VENTAS       │ │ FINANZAS   │ │    OPERACIONES     │
    │                   │ │            │ │                    │
    │  Venta ──► Maquina│ │ Compra     │ │ OrdenCarga         │
    │         ──► Prod. │ │ └──DetalleC│ │ └──DetalleOrdenCarga
    │                   │ │            │ │                    │
    │                   │ │ MovCaja    │ │ TemplateRecarga    │
    │                   │ │ Transferenc│ │ └──PeriodoRecarga  │
    │                   │ │ Rendicion  │ │    └──SnapshotSlot │
    │                   │ │ GastoRecur │ │                    │
    │                   │ │ AcctPeriod │ │                    │
    │                   │ │ Devolucion │ │                    │
    └───────────────────┘ └──────┬─────┘ └────────────────────┘
                                 │
                    ┌────────────┴─────────────┐
                    ▼                          ▼
       ┌──────────────────────┐  ┌─────────────────────────┐
       │     PROVEEDORES      │  │  AUDITORÍA Y SISTEMA    │
       │                      │  │                         │
       │  ProveedorCatalog    │  │  Auditoria              │
       │       ↑              │  │  User                   │
       │  ProveedorAlias      │  │  Informe                │
       │                      │  │  SyncMetadata           │
       └──────────────────────┘  └─────────────────────────┘
```

---

## Catálogo de Entidades

### Dominio: Inventario

Gestiona el catálogo de productos, su identificación por código de barras, el costo histórico, las máquinas expendedoras y la configuración de sus slots.

| Entidad | Propósito | Relaciones |
|---------|-----------|------------|
| **Producto** | Catálogo maestro de productos. Almacena nombre, SKU, categoría, precio de venta, stock en bodega y costo promedio (CPP). | → ProductoEAN, → ProductoCosto, → ConfiguracionSlot, → DetalleCompra, → Venta |
| **ProductoEAN** | Mapeo de código de barras (EAN-8 a EAN-13) y SKU de proveedor al producto del catálogo. Soporta packs con `PackSize`. | → Producto |
| **ProductoCosto** | Historial de costos por producto con vigencia (`FechaDesde`/`FechaHasta`). `FechaHasta` = null indica el costo vigente. Tabla que permite congelar el costo al momento de cada venta. | → Producto |
| **Maquina** | Máquina expendedora. Identificada por `IdInternoMaquina` (Excel) y `CodigoTerminalPos` (Transbank). Asignable a una zona logística. | → ConfiguracionSlot, → ZonaLogistica |
| **ConfiguracionSlot** | Configuración individual de cada slot de una máquina: producto asignado, stock actual, capacidad máxima, stock mínimo y precio de venta. | → Maquina, → Producto |
| **ZonaLogistica** | Zona geográfica para optimización de rutas de recarga. Cada zona tiene un `CostoBaseViaje` estimado. | → Maquina |

### Dominio: Ventas

Registro de las ventas individuales realizadas por cada máquina.

| Entidad | Propósito | Relaciones |
|---------|-----------|------------|
| **Venta** | Venta individual registrada por la máquina. Incluye fecha/hora original y fecha local ajustada, slot, precio y costo de venta congelado, identificador de orden de máquina (para detección de duplicados) y estado de pago. | → Maquina, → Producto |

### Dominio: Finanzas

Gestión de tesorería, compras a proveedores, transferencias a trabajadores, rendiciones de gastos, gastos recurrentes, períodos contables y devoluciones.

| Entidad | Propósito | Relaciones |
|---------|-----------|------------|
| **MovimientoCaja** | Movimiento de tesorería. Monto negativo = egreso, positivo = ingreso. Clasificado por tipo (GASTO, APORTE, RETIRO) y categoría. Admite vinculación opcional con Compra, OrdenCarga, GastoRecurrente y Rendicion. | → Rendicion, → Compra, → OrdenCarga, → GastoRecurrente, → Transferencia, → Devolucion |
| **Compra** | Factura o boleta de compra a proveedor. Incluye `MontoTotal`, estado (PAGADA/PENDIENTE), tipo de factura (MERCADERIA/GASTO_GENERAL), imagen de comprobante (almacenada en DB como `byte[]`), flag de verificación y trazabilidad de transferencia. | → DetalleCompra, → Transferencia, → ProveedorCatalog |
| **DetalleCompra** | Línea individual de una compra: producto, cantidad, costo unitario, subtotal. Incluye campos EAN y SKU extraídos por OCR para aprendizaje automático. | → Compra, → Producto |
| **Transferencia** | Transferencia de dinero a un trabajador para rendir cuentas. Ciclo de vida: Pendiente → EnUso → Conciliado. Vinculable a rendición, período contable y movimiento de caja. Incluye comprobante y optimistic concurrency via `RowVersion`. | → Rendicion, → AccountingPeriod, → MovimientoCaja, → Compra |
| **Rendicion** | Rendición de gastos que agrupa una o más transferencias y múltiples gastos (MovimientosCaja) por trabajador. Ciclo de vida: Abierta → Cerrada. | → Transferencia, → MovimientoCaja, → Devolucion |
| **GastoRecurrente** | Gasto fijo mensual estimado (chip de máquina, bencina, arriendo POS). El sistema alerta si no se ha registrado en el mes actual. Vinculable opcionalmente a una máquina. | → MovimientoCaja |
| **AccountingPeriod** | Período contable que agrupa transferencias, compras y gastos por lapso de tiempo, independiente del trabajador. Reemplaza el enfoque de rendiciones por trabajador. Estados: Abierto → Cerrado. | → Transferencia, → Devolucion |
| **Devolucion** | Devolución de efectivo de un trabajador a la caja. Postea un MovimientoCaja inverso (positivo) como parte de una transacción atómica. Vinculable a rendición o período contable. | → Rendicion, → AccountingPeriod, → MovimientoCaja |

### Dominio: Operaciones

Gestión de órdenes de reposición (carga) y plantillas de recarga con captura de estado de slots.

| Entidad | Propósito | Relaciones |
|---------|-----------|------------|
| **OrdenCarga** | Orden de reposición de stock para una o más máquinas. Estados: BORRADOR → PENDIENTE → FINALIZADA. | → DetalleOrdenCarga, → Maquina |
| **DetalleOrdenCarga** | Línea de carga: producto, cantidad solicitada (descontada de stock en bodega), cantidad retornada (sobras) y costo unitario congelado al crear la orden. | → OrdenCarga, → Producto |
| **TemplateRecarga** | Plantilla que define un ciclo de reposición completo. Cada máquina tiene su propio período. Estados: Pendiente (configuración) → Terminado (cerrado, alimenta alerta stock-crítico). | → PeriodoRecarga |
| **PeriodoRecarga** | Período de recarga de una máquina dentro de un template. Usa `FechaRecarga` como ancla; `FechaFin` es columna calculada persistida en SQL Server. Incluye imágenes opcionales (foto guía y foto OCR). | → TemplateRecarga, → Maquina, → SnapshotSlot |
| **SnapshotSlot** | Captura del estado de un slot al momento de la recarga: producto, cantidad inicial, capacidad máxima y estado (Vacío/Pendiente/Lleno). Permite cálculos precisos de agotamiento. | → PeriodoRecarga, → ConfiguracionSlot, → Producto |

### Dominio: Proveedores

Catálogo canónico de proveedores con resolución de nombres desde OCR de facturas.

| Entidad | Propósito | Relaciones |
|---------|-----------|------------|
| **ProveedorCatalog** | Identidad canónica de proveedor, curada por el dueño. Nombre único y autoritativo al que se resuelven los alias OCR. | → ProveedorAlias, → Compra |
| **ProveedorAlias** | Alias de proveedor extraído por OCR. Cada string crudo se normaliza para búsqueda exacta O(1). Primer registro (`CreatedAt`) y última vez visto (`LastSeenAt`) para aprendizaje continuo. | → ProveedorCatalog |

### Dominio: Auditoría y Sistema

Registro de cambios, usuarios, archivos subidos y metadatos de sincronización.

| Entidad | Propósito | Relaciones |
|---------|-----------|------------|
| **Auditoria** | Registro de cambios sobre entidades del sistema. Almacena usuario, acción, entidad afectada (`EntityId` + `EntityType`), estado before/after en JSON y detalle textual. | — |
| **User** | Usuario del sistema con autenticación por username/password hash y roles. | — |
| **Informe** | Archivo binario subido al sistema (informes, reportes). Almacenado como `byte[]` en DB con metadatos de nombre, extensión, MIME type y carpeta. | — |
| **SyncMetadata** | Pares clave-valor para metadatos de sincronización entre sistemas. | — |

---

## Tablas Históricas (Rollback)

Cada entidad principal del dominio tiene una contraparte `*History` que almacena el antes/después de cada modificación en JSON, junto con metadatos de auditoría. El `AuditSaveChangesInterceptor` de EF Core escribe estas tablas automáticamente en cada `SaveChanges`.

Todas las tablas históricas comparten la misma estructura base:

| Campo | Tipo | Propósito |
|-------|------|-----------|
| `Id` | int (PK) | Identificador único |
| `EntityId` | int | ID de la entidad modificada |
| `Action` | string | Tipo de cambio (`INSERT`, `UPDATE`, `DELETE`) |
| `BeforeJson` | nvarchar(max) | Estado previo en JSON (null en INSERT) |
| `AfterJson` | nvarchar(max) | Estado posterior en JSON (null en DELETE) |
| `Timestamp` | datetime | Momento del cambio |
| `Usuario` | string | Usuario que realizó la operación |

Además, cada tabla replica las columnas escalares de su entidad principal para consultas sin necesidad de deserializar JSON.

| Entidad Principal | Tabla Histórica | Ubicación |
|-------------------|-----------------|-----------|
| Compra | CompraHistory | `Entities/History/` |
| ConfiguracionSlot | ConfiguracionSlotHistory | `Entities/History/` |
| GastoRecurrente | GastoRecurrenteHistory | `Entities/History/` |
| Maquina | MaquinaHistory | `Entities/History/` |
| MovimientoCaja | MovimientoCajaHistory | `Entities/History/` |
| OrdenCarga | OrdenCargaHistory | `Entities/History/` |
| Producto | ProductoHistory | `Entities/History/` |
| ProveedorCatalog | ProveedorCatalogHistory | `Entities/History/` |
| User | UserHistory | `Entities/History/` |
| Venta | VentaHistory | `Entities/History/` |
| Rendicion | RendicionHistory | `Entities/` |
| Transferencia | TransferenciaHistory | `Entities/` |

> **Nota:** `RendicionHistory` y `TransferenciaHistory` residen en la raíz `Entities/` por razones de consistencia histórica, pero siguen el mismo patrón de auditoría.

---

## Decisiones de Modelado

### Costo de Inventario

- **Costo promedio ponderado (CPP)**, no FIFO ni UEPS. Cada compra actualiza `Producto.CostoPromedio` y registra un `ProductoCosto` con vigencia para mantener trazabilidad histórica.
- El costo de venta se **congela al momento de la venta** en `Venta.CostoVenta` a partir del `ProductoCosto` vigente en esa fecha.

### Control de Stock

- **No existe ajuste directo de stock.** El stock de bodega (`Producto.StockBodega`) solo se modifica mediante:
  - **Compras**: incrementan stock.
  - **Órdenes de Carga**: descuentan stock de bodega al finalizar.
- El stock en máquina (`ConfiguracionSlot.StockActual`) se actualiza exclusivamente vía **Órdenes de Carga** y **Ventas** (decremento automático).

### Trazabilidad y Auditoría

- **Trazabilidad total:** el `AuditSaveChangesInterceptor` —un interceptor de EF Core— captura automáticamente cada `INSERT`, `UPDATE` y `DELETE` sobre las entidades principales y persiste el cambio en la tabla `*History` correspondiente.
- El before/after se almacena como JSON para consulta flexible y posible restauración (rollback).
- La tabla `Auditoria` proporciona un registro complementario de acciones de alto nivel con metadatos de usuario y entidad.

### Imágenes y Archivos

- Los comprobantes de compra (`Compra.FacturaImagen`) y transferencia (`Transferencia.ComprobanteImagen`) se almacenan como `byte[]` (`varbinary(max)`) en la base de datos, garantizando que viajen con las copias de seguridad. `Transferencia` acompaña los bytes con `ComprobanteImagenContentType` y `ComprobanteImagenFileName`; la antigua columna de ruta en disco ya no existe.
- Los informes (`Informe.Contenido`) y fotos de recarga (`PeriodoRecarga.FotoGuia`, `PeriodoRecarga.FotoOcr`) siguen el mismo patrón de almacenamiento binario en DB.

### Optimistic Concurrency

- `Transferencia` y `Rendicion` usan `RowVersion` (timestamp de SQL Server) para optimistic concurrency en transiciones de estado críticas.
- `TemplateRecarga` también incluye `RowVersion` para proteger las transiciones de estado del ciclo de vida.

### Resolución de Proveedores por OCR

- `ProveedorAlias` almacena strings crudos de OCR con un campo `RawNameNormalized` indexado para búsqueda exacta O(1), siguiendo el mismo patrón que `ProductoEAN` para resolución de códigos de barras.
- Ambos sistemas permiten aprendizaje automático: cada compra registra `LastSeenAt` para identificar patrones de uso.
