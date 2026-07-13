# API Reference — VendingManager

> API REST con 19 controladores y 100+ endpoints. Arquitectura: controladores delgados, lógica de negocio en servicios de Infrastructure.

## Autenticación

El sistema utiliza **autenticación por cookies** con el esquema `CookieAuthenticationDefaults.AuthenticationScheme`. Al iniciar sesión, el servidor emite un cookie HttpOnly con los claims del usuario (nombre, identificador, rol).

**Roles:**
- `Admin` — acceso completo a todas las funciones del sistema
- `User` — acceso a funciones operativas estándar

Las contraseñas se almacenan con **BCrypt** (`BCrypt.Net.BCrypt.HashPassword`). El endpoint de login incorpora un retardo aleatorio (100-300 ms) ante credenciales inválidas como mitigación contra ataques de timing.

## Formato de respuesta

Todas las respuestas utilizan **JSON** como formato de intercambio. Los errores siguen el estándar **RFC 7807 Problem Details** (`ProblemDetails`) con los campos `title`, `detail`, `status`.

Casos de error comunes:
- `400 Bad Request` — validación de entrada fallida
- `401 Unauthorized` — credenciales inválidas o sesión expirada
- `404 Not Found` — recurso inexistente
- `409 Conflict` — violación de restricción (duplicado, concurrencia)
- `500 Internal Server Error` — error inesperado del servidor

Los endpoints de exportación retornan archivos Excel (`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`). Los endpoints de imágenes retornan el tipo MIME correspondiente (`image/jpeg`, `application/pdf`, etc.).

---

## AccountController

Autenticación y sesión de usuario. Sin restricción de autorización.

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| POST | `/api/account/login` | — | Inicio de sesión con username y password. Retorna cookie de autenticación |
| GET | `/logout` | — | Cierre de sesión. Redirige a `/` |
| GET | `/api/account/user` | — | Información del usuario autenticado actual (`name`, `role`). `name` es `null` si no hay sesión activa |

---

## AuditoriaController

Auditoría de operaciones e historial de cambios. Solo administradores.

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| GET | `/api/auditoria` | Admin | Lista registros de auditoría. Filtros opcionales: `usuario`, `accion`. Ordenados por fecha descendente |
| GET | `/api/auditoria/history` | Admin | Historial de cambios de todas las entidades (Compras, Productos, Máquinas, Ventas, etc.). Máximo 200 registros |
| POST | `/api/auditoria/rollback/{entityType}/{entityId}/{historyId}` | Admin | Revierte una entidad al estado guardado en un registro histórico específico |

---

## CajaController

Tesorería: movimientos de caja, resúmenes, comprobantes, exportación de planillas.

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| GET | `/api/caja/resumen` | Sí | Resumen de caja para un `month` y `year` determinados |
| GET | `/api/caja/movimientos` | Sí | Movimientos de caja del período |
| GET | `/api/caja/productos-simple` | Sí | Lista simple de productos (id, nombre, stock en bodega) para selects |
| POST | `/api/caja/registrar` | Sí | Registra un nuevo movimiento de caja |
| POST | `/api/caja/upload` | Sí | Sube un comprobante/imagen asociado a un movimiento |
| GET | `/api/caja/exportar` | Sí | Exporta resumen y movimientos a Excel |
| GET | `/api/caja/valorizacion` | Sí | Valorización de stock actual |

---

## ComprasController

Gestión de compras y facturas. Con OCR para extracción automática de datos.

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| GET | `/api/compras` | Sí | Lista todas las compras. Parámetro opcional `limit` |
| GET | `/api/compras/{id}` | Sí | Detalle de una compra con sus líneas de detalle |
| POST | `/api/compras` | Sí | Registra una nueva compra con sus detalles. Retorna `201 Created` |
| POST | `/api/compras/{id}/pagar` | Sí | Marca una compra como pagada |
| PUT | `/api/compras/{id}` | Sí | Actualiza una compra existente |
| DELETE | `/api/compras/{id}` | Sí | Elimina una compra |
| POST | `/api/compras/upload-factura` | Sí | Sube imagen de factura para extraer datos mediante OCR con IA |
| POST | `/api/compras/{id}/factura` | Sí | Sube la imagen de factura de una compra específica |
| GET | `/api/compras/{id}/factura` | Sí | Descarga la imagen de factura de una compra |
| POST | `/api/compras/backfill-facturas` | Admin | Migración one-time: carga imágenes de facturas legacy del disco a la base de datos |
| DELETE | `/api/compras/{id}/transferencia` | Sí | Desvincula una compra de su transferencia |
| POST | `/api/compras/{id}/proveedor` | Sí | Reasigna el proveedor de una compra |
| POST | `/api/compras/reconstruir-costos` | Sí | Reconstruye los costos promedio de productos a partir de compras históricas |

---

## ContabilidadController

Contabilidad: cuadres, períodos contables, transferencias, conciliación global y verificaciones de integridad.

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| POST | `/api/contabilidad/transferencia-con-movimiento` | Sí | Crea una transferencia con su movimiento de caja asociado |
| POST | `/api/contabilidad/cuadre` | Sí | Crea un cuadre completo (período + transferencia 1:1) |
| POST | `/api/contabilidad/compra-vinculada` | Sí | Crea una compra vinculada a una transferencia existente |
| POST | `/api/contabilidad/gasto-vinculado` | Sí | Crea un gasto (movimiento de caja) vinculado a una transferencia |
| GET | `/api/contabilidad/trabajadores-activos` | Sí | Lista de trabajadores con transferencias activas |
| PUT | `/api/contabilidad/transferencia/{id}/monto` | Sí | Actualiza el monto de una transferencia. Puede retornar `409 Conflict` por concurrencia |
| POST | `/api/contabilidad/transferencia/{id}/desvincular` | Sí | Desvincula una transferencia |
| DELETE | `/api/contabilidad/transferencia/{id}` | Sí | Elimina una transferencia del cuadre. Retorna detalle de lo eliminado |
| POST | `/api/contabilidad/transferencia/{transferenciaId}/vincular-compra/{compraId}` | Sí | Vincula una compra existente a una transferencia |
| PUT | `/api/contabilidad/compra/{id}` | Sí | Actualiza una compra del cuadre |
| PUT | `/api/contabilidad/gasto/{id}` | Sí | Actualiza un gasto del cuadre |
| GET | `/api/contabilidad/periodos` | Sí | Lista períodos contables. Filtros opcionales: `desde`, `hasta` |
| GET | `/api/contabilidad/periodos/{id}` | Sí | Detalle completo de un período contable |
| POST | `/api/contabilidad/periodos` | Sí | Crea un nuevo período contable |
| PUT | `/api/contabilidad/periodos/{id}` | Sí | Actualiza un período contable |
| POST | `/api/contabilidad/periodos/{id}/cerrar` | Sí | Cierra un período contable |
| DELETE | `/api/contabilidad/periodos/{id}` | Sí | Elimina un período contable (desvincula transferencias sin borrarlas) |
| POST | `/api/contabilidad/transferencia/{id}/comprobante` | Sí | Sube el comprobante de una transferencia |
| GET | `/api/contabilidad/transferencia/{id}/comprobante` | Sí | Descarga el comprobante de una transferencia |
| POST | `/api/contabilidad/transferencia/{id}/verificar` | Sí | Marca una transferencia como verificada |
| POST | `/api/contabilidad/transferencia/{id}/desverificar` | Sí | Desmarca la verificación de una transferencia |
| POST | `/api/contabilidad/compra/{id}/verificar` | Sí | Marca una compra como verificada |
| POST | `/api/contabilidad/compra/{id}/desverificar` | Sí | Desmarca la verificación de una compra |
| POST | `/api/contabilidad/devolucion` | Sí | Registra una devolución con su movimiento de caja inverso |
| GET | `/api/contabilidad/periodos/conciliacion-global` | Sí | Matriz de conciliación multi-período para un trabajador |
| GET | `/api/contabilidad/integridad` | Sí | Ejecuta todas las verificaciones de integridad de datos y retorna resultados agrupados por severidad |

---

## GastoRecurrenteController

Gestión de gastos recurrentes (servicios, alquileres, etc.) con aplicación automática a caja.

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| GET | `/api/gastorecurrente` | Sí | Lista todos los gastos recurrentes (activos e inactivos) |
| POST | `/api/gastorecurrente` | Admin | Crea un nuevo gasto recurrente |
| PUT | `/api/gastorecurrente/{id}` | Admin | Actualiza un gasto recurrente |
| DELETE | `/api/gastorecurrente/{id}` | Admin | Desactiva un gasto recurrente (soft-delete) |
| GET | `/api/gastorecurrente/pendientes` | Sí | Gastos recurrentes pendientes de aplicar en un mes/año |
| POST | `/api/gastorecurrente/aplicar` | Sí | Aplica un gasto recurrente como movimiento de caja. Permite ajustar el monto real |
| POST | `/api/gastorecurrente/aplicar-todos` | Sí | Aplica todos los gastos pendientes del mes con sus montos estimados o personalizados |

---

## InformesController

Subida, descarga y eliminación de informes/documentos. Sin restricción de autorización.

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| GET | `/api/informes/{id}` | — | Descarga un informe por ID |
| POST | `/api/informes` | — | Sube un nuevo informe. Parámetros: `file`, `folder` (opcional, default "General") |
| DELETE | `/api/informes/{id}` | — | Elimina un informe |

---

## InventarioController

Importación de catálogo y consulta de inventario desde archivo.

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| POST | `/api/inventario` | Sí | Importa catálogo de productos desde archivo Excel/CSV |
| GET | `/api/inventario/lista` | Sí | Lista completa del inventario (productos con stock en bodega) |

---

## LogisticaPredictivaController

Análisis predictivo y generación automática de órdenes de carga por zonas logísticas.

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| GET | `/api/logisticapredictiva/zonas` | Sí | Análisis de zonas logísticas con predicción de reposición. Parámetros: `diasHistorial` (14), `ventanaDias` (3) |
| POST | `/api/logisticapredictiva/generar-orden` | Sí | Genera una orden de carga borrador basada en análisis predictivo |

---

## MaquinasController

CRUD de máquinas expendedoras y configuración de slots. Sin restricción de autorización.

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| GET | `/api/maquinas` | — | Lista todas las máquinas |
| POST | `/api/maquinas` | — | Crea una nueva máquina. Retorna `201 Created` |
| PUT | `/api/maquinas/{id}` | — | Actualiza una máquina existente |
| DELETE | `/api/maquinas/{id}` | — | Elimina una máquina |
| GET | `/api/maquinas/{id}/slots` | — | Obtiene la configuración de slots de una máquina |
| POST | `/api/maquinas/{id}/slots` | — | Actualiza la configuración de un slot específico |
| POST | `/api/maquinas/{id}/batch-actions` | — | Procesa acciones en lote sobre los slots (REFILL, EMPTY, SWAP) |

---

## OrdenCargaController

Órdenes de carga: creación, finalización, sugerencias de reposición, OCR de planillas y exportación a Excel.

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| POST | `/api/ordencarga` | — | Crea una orden de carga |
| POST | `/api/ordencarga/finalizar` | — | Finaliza una orden de carga y retorna el stock a bodega |
| PATCH | `/api/ordencarga/{id}/nombre` | — | Actualiza el nombre de una orden |
| PUT | `/api/ordencarga/{id}` | — | Actualiza una orden de carga completa |
| GET | `/api/ordencarga/historial` | — | Historial de órdenes. Parámetro opcional: `maquinaId` |
| GET | `/api/ordencarga/{id}` | — | Detalle de una orden por ID |
| GET | `/api/ordencarga/sugerencia` | — | Sugerencia de carga para una máquina específica (`maquinaId`) |
| GET | `/api/ordencarga/exportar-sugerencia` | — | Exporta la sugerencia de carga a Excel |
| GET | `/api/ordencarga/exportar-consolidado` | — | Exporta la sugerencia global consolidada a Excel |
| POST | `/api/ordencarga/{id}/confirmar` | — | Confirma una orden de carga |
| POST | `/api/ordencarga/from-photo` | — | Procesa foto de planilla de recarga mediante OCR. Extrae slot+cantidad y hace fuzzy matching contra la máquina |

---

## ProductosController

Catálogo de productos, stock, costos históricos y mapeo de códigos EAN.

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| GET | `/api/productos` | — | Lista todos los productos del catálogo |
| POST | `/api/productos/importar-catalogo` | — | Importa catálogo desde archivo Excel/CSV |
| GET | `/api/productos/exportar-catalogo` | — | Exporta el catálogo a Excel |
| POST | `/api/productos/ajustar-stock` | — | Ajusta el stock en bodega de un producto |
| GET | `/api/productos/{id}` | — | Obtiene un producto por ID |
| GET | `/api/productos/{id}/historial-costos` | — | Historial de costos de un producto |
| POST | `/api/productos` | — | Crea un nuevo producto. Retorna `201 Created` |
| PUT | `/api/productos/{id}` | — | Actualiza un producto. Parámetros opcionales: `recalculateFrom`, `recalculateTo` para recalcular costos |
| DELETE | `/api/productos/{id}` | — | Elimina un producto |
| GET | `/api/productos/ean` | — | Lista todos los mapeos EAN |
| GET | `/api/productos/ean/{id}` | — | Obtiene un mapeo EAN por ID |
| POST | `/api/productos/ean` | — | Crea un nuevo mapeo EAN. Retorna `201 Created`. Puede retornar `409` si el EAN ya existe |
| PUT | `/api/productos/ean/{id}` | — | Actualiza un mapeo EAN existente |
| DELETE | `/api/productos/ean/{id}` | — | Elimina un mapeo EAN |
| GET | `/api/productos/{id}/ean` | — | Lista los EANs asociados a un producto |

---

## ProveedoresController

Catálogo de proveedores con matching automático sobre compras existentes.

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| GET | `/api/proveedores` | Sí | Lista el catálogo de proveedores ordenado por nombre canónico |
| POST | `/api/proveedores` | Sí | Crea un nuevo proveedor. `409` si el nombre ya existe |
| PUT | `/api/proveedores/{id}` | Admin | Renombra un proveedor. `409` si hay duplicado |
| DELETE | `/api/proveedores/{id}` | Admin | Elimina un proveedor del catálogo |
| POST | `/api/proveedores/backfill` | Sí | Matching batch: vincula compras sin `ProveedorCatalogId` usando umbral de confianza 0.85 |

---

## RendicionController

Rendiciones de trabajadores: transferencias, compras y gastos vinculados, cierre y resumen.

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| GET | `/api/rendicion` | Sí | Lista rendiciones. Filtros opcionales: `desde`, `hasta` |
| GET | `/api/rendicion/{id}` | Sí | Detalle de una rendición con transferencias, compras y gastos |
| POST | `/api/rendicion` | Sí | Crea una nueva rendición. Puede vincular una transferencia inicial. Retorna `201 Created` |
| PUT | `/api/rendicion/{id}` | Sí | Actualiza una rendición |
| POST | `/api/rendicion/{id}/cerrar` | Sí | Cierra una rendición |
| POST | `/api/rendicion/{id}/link-compra` | Sí | Vincula una compra a una transferencia dentro de la rendición |
| POST | `/api/rendicion/{id}/unlink-compra/{compraId}` | Sí | Desvincula una compra de su transferencia |
| POST | `/api/rendicion/{id}/link-gasto` | Sí | Vincula un gasto a la rendición |
| POST | `/api/rendicion/{id}/unlink-gasto/{gastoId}` | Sí | Desvincula un gasto de la rendición |
| GET | `/api/rendicion/{id}/resumen` | Sí | Resumen financiero de una rendición |
| GET | `/api/rendicion/{id}/full` | Sí | Detalle completo de una rendición con resumen incluido |
| GET | `/api/rendicion/transferencias-no-vinculadas` | Sí | Transferencias disponibles para vincular a rendiciones |
| GET | `/api/rendicion/compras-no-vinculadas` | Sí | Compras disponibles para vincular. Filtros: `proveedor`, `numeroDocumento`, `desde`, `hasta` |
| GET | `/api/rendicion/gastos-no-vinculados` | Sí | Gastos disponibles para vincular. Filtro: `fechaDesde`, `fechaHasta` |
| DELETE | `/api/rendicion/{id}` | Sí | Elimina una rendición y sus transferencias (desvincula compras, elimina movimientos de caja) |

---

## TemplateRecargaController

Plantillas de recarga para máquinas, con período y slots. Fotos guía y OCR, sincronización de ventas.

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| GET | `/api/templaterecarga` | — | Lista todos los templates con sus períodos |
| GET | `/api/templaterecarga/list` | — | Lista ligera de templates (sin períodos anidados) |
| POST | `/api/templaterecarga/{id}/terminar` | — | Cambia estado a Terminado. Fuente para stock crítico |
| POST | `/api/templaterecarga/{id}/reabrir` | — | Reabre un template terminado a Pendiente |
| POST | `/api/templaterecarga/{templateId}/periodo/{periodoId}/slot-batch` | — | Acciones en lote sobre slots de un período (REFILL, EMPTY, SWAP) |
| GET | `/api/templaterecarga/{id}` | — | Obtiene un template por ID con sus períodos |
| POST | `/api/templaterecarga` | — | Crea un nuevo template. Retorna `201 Created`. `409` si hay conflicto de fechas encadenadas |
| PUT | `/api/templaterecarga/{id}` | — | Actualiza un template existente |
| DELETE | `/api/templaterecarga/{id}` | — | Elimina un template |
| GET | `/api/templaterecarga/{id}/analyze` | — | Analiza stockout usando los períodos del template. Parámetro: `umbralHoras` (24) |
| GET | `/api/templaterecarga/{templateId}/slot-timeline` | — | Timeline de ventas para un slot específico (lazy-loaded para el scrubber del dashboard) |
| GET | `/api/templaterecarga/maquina/{maquinaId}/slots` | — | Configuración actual de slots de una máquina (para crear snapshot) |
| POST | `/api/templaterecarga/{id}/sincronizar-ventas` | — | Sincroniza ventas históricas con la configuración del template |
| POST | `/api/templaterecarga/sincronizar-todas-ventas` | — | Sincroniza todos los templates contra ventas históricas |
| PATCH | `/api/templaterecarga/{templateId}/periodo/{periodoId}/slot/{numeroSlot}/sincronizar-producto` | — | Reasigna el producto de un slot en las ventas históricas |
| PUT | `/api/templaterecarga/{templateId}/periodo/{periodoId}/foto-guia` | — | Sube o reemplaza la foto guía de un período. Límite: 10 MB |
| GET | `/api/templaterecarga/{templateId}/periodo/{periodoId}/foto-guia` | — | Obtiene la foto guía de un período |
| DELETE | `/api/templaterecarga/{templateId}/periodo/{periodoId}/foto-guia` | — | Elimina la foto guía de un período |
| PUT | `/api/templaterecarga/{templateId}/periodo/{periodoId}/foto-ocr` | — | Sube o reemplaza la foto OCR de un período. Límite: 5 MB |
| GET | `/api/templaterecarga/{templateId}/periodo/{periodoId}/foto-ocr` | — | Obtiene la foto OCR de un período |
| DELETE | `/api/templaterecarga/{templateId}/periodo/{periodoId}/foto-ocr` | — | Elimina la foto OCR de un período |

---

## TransferenciaController

CRUD de transferencias (movimientos de fondos entre trabajadores y empresa).

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| GET | `/api/transferencia` | Sí | Lista todas las transferencias |
| GET | `/api/transferencia/pendientes` | Sí | Transferencias en estado pendiente |
| GET | `/api/transferencia/{id}` | Sí | Detalle de una transferencia |
| POST | `/api/transferencia` | Sí | Crea una nueva transferencia. Retorna `201 Created` |
| PUT | `/api/transferencia/{id}` | Sí | Actualiza una transferencia existente |
| DELETE | `/api/transferencia/{id}` | Sí | Elimina una transferencia (solo si no está conciliada) |

---

## UsersController

Gestión de usuarios del sistema. Solo administradores.

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| GET | `/api/users` | Admin | Lista todos los usuarios |
| POST | `/api/users` | Admin | Crea un nuevo usuario con contraseña hasheada con BCrypt. Retorna `201 Created` |
| PUT | `/api/users/{id}` | Admin | Actualiza rol y/o contraseña de un usuario |
| DELETE | `/api/users/{id}` | Admin | Elimina un usuario (no permite auto-eliminación) |

---

## VentasController

Importación, consulta y análisis de ventas. Sincronización con portal web, dashboard, reportes financieros y sugerencias de compra.

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| GET | `/api/ventas/last-sync` | Sí | Estado de la última sincronización, health status y estado del circuit breaker |
| POST | `/api/ventas` | Sí | Importa archivo de ventas de máquina. Guarda copia como informe. Parámetro opcional: `fechaLimite` |
| GET | `/api/ventas/fix-dates` | Sí | Corrección de fechas en ventas |
| GET | `/api/ventas/recalcular-costos` | Sí | Recalcula costos históricos de ventas basados en el producto actual |
| POST | `/api/ventas/subir-transbank` | Sí | Importa archivo de transacciones Transbank |
| GET | `/api/ventas/lista-maquinas` | Sí | Lista de máquinas para selects del frontend |
| GET | `/api/ventas/lista-productos` | Sí | Lista de productos para selects del frontend |
| GET | `/api/ventas/dashboard-stats` | Sí | Estadísticas del dashboard. Parámetro opcional: `maquinaId` |
| GET | `/api/ventas/machine-status` | Sí | Estado en línea de las máquinas vía scraper |
| GET | `/api/ventas/stock-critico` | Sí | Productos con stock crítico por máquina. Parámetro opcional: `maquinaId` |
| GET | `/api/ventas/reporte-rango` | Sí | Reporte de ventas en rango de fechas. Parámetros: `inicio`, `fin`, `maquinaId` (opcional), `includePhantom`, `templateId` |
| GET | `/api/ventas/informe-financiero` | Sí | Informe financiero en rango de fechas |
| GET | `/api/ventas/exportar` | Sí | Exporta reporte de ventas a Excel |
| POST | `/api/ventas/sync-portal` | Sí | Sincronización manual desde el portal web (scraping) |
| POST | `/api/ventas/sync-portal-api` | Sí | Sincronización manual desde el portal web (API directa) |
| GET | `/api/ventas/analisis-productos` | Sí | Análisis de productos por rango de fechas. Parámetros: `inicio`, `fin`, `maquinaId`, `includePendientes` |
| GET | `/api/ventas/stockout-analysis` | Sí | Análisis de stockout por rango de fechas. Parámetros: `inicio`, `fin`, `maquinaId`, `umbralHoras` (24) |
| GET | `/api/ventas/ventas-diarias` | Sí | Ventas diarias de un producto en una máquina en un rango de fechas |
| GET | `/api/ventas/categoria-analisis` | Sí | Análisis de ventas por categoría en rango de fechas |
| GET | `/api/ventas/purchase-suggestion` | Sí | Sugerencia de compras basada en rotación histórica. Parámetros: `days` (30), `maquinaId` |
| GET | `/api/ventas/purchase-suggestion/export` | Sí | Exporta sugerencia de compras a Excel |
| DELETE | `/api/ventas/borrar-rango` | Sí | Elimina ventas en rango de fechas para una máquina y restaura stock |

---

## ZonasLogisticasController

Catálogo de zonas logísticas con costo base de viaje.

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| GET | `/api/zonas-logisticas` | Sí | Lista zonas logísticas ordenadas por nombre |
| POST | `/api/zonas-logisticas` | Sí | Crea una nueva zona. `409` si el nombre ya existe. Retorna `201 Created` |
| PUT | `/api/zonas-logisticas/{id}` | Sí | Actualiza nombre y costo base de una zona. `409` si hay duplicado |
| DELETE | `/api/zonas-logisticas/{id}` | Sí | Elimina una zona (FK en máquinas pasa a null) |

---

## Endpoints de Infraestructura

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/health` | Liveness check: verifica que la aplicación esté en ejecución |
| GET | `/health/db` | Readiness check: verifica conectividad con SQL Server |
| GET | `/scalar/v1` | Documentación OpenAPI interactiva (disponible solo en entorno Development) |
