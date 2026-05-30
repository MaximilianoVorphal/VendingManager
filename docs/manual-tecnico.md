# VendingManager — Manual Técnico

Manual completo de arquitectura, desarrollo y operación. Cubre toda feature implementada en el código real (no solo lo documentado previamente).

---

## 1. Arquitectura General

VendingManager es una plataforma **polyglot distribuida**: backend .NET con Clean Architecture, frontend Blazor WebAssembly, y un microservicio Python para scraping y OCR. Orquestado con Docker Compose en tres entornos (dev, test, prod).

### Diagrama de Contexto (C4 — Nivel 1)

```
┌──────────────────────────────────────────────────────────────┐
│                    VendingManager System                      │
│                                                               │
│  ┌──────────────────┐    ┌──────────────────────────────┐    │
│  │ Blazor WebAssembly │───▶│  ASP.NET Core Web API         │    │
│  │ (Frontend SPA)     │    │  Clean Architecture           │    │
│  └──────────────────┘    │  Controllers → Services → EF    │    │
│                          └───────────┬──────────────────┘    │
│                                      │                        │
│                          ┌───────────▼──────────────────┐    │
│                          │  SQL Server 2022             │    │
│                          │  (VendingDB)                  │    │
│                          └──────────────────────────────┘    │
│                                                               │
│  ┌──────────────────────────────────────────────────────┐    │
│  │  Python Scraper Service (FastAPI + Playwright)        │    │
│  │  Extracción OurVend + OCR facturas + OCR recarga     │    │
│  └──────────────────────────────────────────────────────┘    │
│                                                               │
│  ┌──────────────────────────────────────────────────────┐    │
│  │  AutomatedReportService (Background Worker)           │    │
│  │  Sincronización diaria automática a las 23:00        │    │
│  └──────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────┘
         │                                      │
         ▼                                      ▼
┌─────────────────┐                  ┌─────────────────────┐
│   Usuario        │                  │  Google Gemini API   │
│   (Navegador)    │                  │  (OCR de facturas    │
└─────────────────┘                  │   y fotos de recarga) │
                                     └─────────────────────┘
```

### Patrones de diseño aplicados

| Patrón | Dónde se aplica |
|--------|----------------|
| **Clean Architecture** | Backend .NET: `Core/` (interfaces/entidades), `Infrastructure/` (implementaciones), `Controllers/` + `Components/` (presentación) |
| **Repository Pattern** | `IVentaRepository`, `IMaquinaRepository`, `IProductoEANRepository`, `IAccountingPeriodRepository` |
| **Service Pattern** | ~30 servicios especializados cada uno con una responsabilidad clara |
| **Facade Pattern** | `TemplateRecargaService` delega a `TemplateRecargaLifecycleService` y `TemplateRecargaAnalyticsService` |
| **Unit of Work** | `ApplicationDbContext` como unidad de trabajo con EF Core |
| **Interceptor Pattern** | `AuditSaveChangesInterceptor` — registro automático de auditoría sin tocar los servicios |
| **State Machine** | `TemplateRecarga` (Pendiente → Terminado) y `Transferencia` (Pendiente → En Uso → Conciliado) |
| **Code-Behind** | Componentes Blazor grandes separan `.razor` de `.razor.cs` |

---

## 2. Stack Tecnológico (Detallado)

### Backend — .NET 10

| Componente | Tecnología | Notas |
|-----------|-----------|-------|
| Runtime | .NET 10 | SDK 10.0 |
| Web Framework | ASP.NET Core | Minimal + Controllers |
| Frontend | Blazor WebAssembly | Hospedado en ASP.NET Core |
| ORM | Entity Framework Core | Migraciones automáticas al iniciar |
| Base de Datos | SQL Server 2022 | Vía `mcr.microsoft.com/mssql/server:2022-latest` |
| Autenticación | Cookie Authentication | `PersistingAuthenticationStateProvider` para Blazor |
| Autorización | Roles (Admin, User) | `Roles.Admin` en constantes compartidas |
| Hashing | BCrypt.Net | Contraseñas nunca en texto plano |
| Logging | Serilog | JSON estructurado (`RenderedCompactJsonFormatter`) |
| API Docs | Scalar | OpenAPI en Development (`/scalar/v1`) |
| Rate Limiting | Fixed Window | 5 req/min en login, status 429 |
| Health Checks | ASP.NET Core built-in | Liveness (`/health`) + Readiness (`/health/db`) |
| Excel | ClosedXML | Exportación/importación de reportes |
| HTTP | `IHttpClientFactory` | Clientes tipados para Scraper y OCR |
| CORS | Configurado por política | Orígenes Blazor locales |
| Compresión | Response Compression | Gzip/Brotli, incluye `application/octet-stream` |
| Cultura | `es-CL` (pesos chilenos, dd/MM/yyyy) | Forzada en `Program.cs` con `CultureInfo` clonado |

### Microservicio — Python

| Componente | Tecnología | Notas |
|-----------|-----------|-------|
| Framework | FastAPI | API REST ligera |
| Navegación | Playwright | Chromium headless para login y navegación OurVend |
| OCR | Google Gemini | Visión: extrae texto de imágenes de facturas y fotos de recarga |
| Runtime | Python 3.10+ | Contenerizado con Docker |
| Endpoints | 5 | `/download`, `/api/ocr/invoice`, `/api/ocr/recarga`, `/health`, alt mode |

### Infraestructura

| Componente | Herramienta |
|-----------|-----------|
| Contenerización | Docker + Docker Compose |
| Entornos | `docker-compose.yml` (prod), `docker-compose.dev.yml` (dev), `docker-compose.test.yml` (test) |
| Volúmenes | `mssql_data`, `scraper_data` (más variantes por entorno) |
| Timezone | `America/Santiago` (UTC-3/UTC-4) |
| Uploads | `/var/uploads/vendingmanager` (mapeado a host en dev/test) |

---

## 3. Estructura del Proyecto

```
VendingManager/
├── src/
│   ├── VendingManager/                    # Backend ASP.NET Core
│   │   ├── Core/                          # Capa de dominio
│   │   │   ├── Configuration/             # VendingConfig, AnalyticsThresholds
│   │   │   ├── Entities/                  # Modelos de dominio (19 entidades)
│   │   │   │   └── History/               # Entidades históricas para rollback (8)
│   │   │   ├── Interfaces/                # Contratos (~27 interfaces)
│   │   │   └── Utils/                     # Extensiones y helpers
│   │   ├── Infrastructure/                # Capa de infraestructura
│   │   │   ├── Clients/                   # ScraperClient (HTTP tipado)
│   │   │   ├── Data/                      # DbContext, Migrations, Repositories (4)
│   │   │   ├── Interceptors/              # AuditSaveChangesInterceptor
│   │   │   └── Services/                  # Implementaciones (~26 servicios)
│   │   ├── Controllers/                   # 15 controladores REST
│   │   ├── Components/                    # Razor Components
│   │   │   ├── Contabilidad/              # Componentes de conciliación (5)
│   │   │   ├── Shared/                    # Toolbar, PendingActionsQueue
│   │   │   ├── Layout/                    # NavMenu, MainLayout, LoginLayout
│   │   │   └── Pages/                     # Páginas (27+)
│   │   ├── Web/                           # Configuración Web
│   │   │   ├── Auth/                      # PersistingAuthenticationStateProvider
│   │   │   ├── Middleware/                # GlobalProblemDetailsMiddleware
│   │   │   └── ModelBinders/              # DateTimeModelBinder (dd/MM/yyyy)
│   │   ├── Migrations/                    # EF Core Migrations
│   │   └── Program.cs                     # Entry point + DI + auto-migrate + seed
│   ├── VendingManager.Shared/             # Modelos y constantes compartidos
│   ├── VendingManager.Web/                # Cliente Blazor WebAssembly
│   ├── VendingManager.Tests/              # Tests unitarios e integración
│   └── VendingManager.Tests.Viewport/     # Tests de viewport/responsive
├── services/
│   └── scraper/                            # Microservicio Python
│       ├── scraper_api.py                  # FastAPI — 5 endpoints
│       ├── download_ourvend_report_alt.py  # Scraper alternativo (inglés, todas las máquinas)
│       ├── gemini_ocr.py                   # OCR con Gemini Vision API
│       └── Dockerfile                      # Imagen con Chromium + Playwright
├── docs/
│   ├── architecture/                       # Diagramas C4, API reference, ADRs
│   ├── development/                        # Guía de setup
│   ├── negocio/                            # Lógica de negocio y guía operativa
│   ├── presentacion-proyecto.md            # Presentación para comprador
│   └── manual-tecnico.md                   # Este documento
├── docker-compose.yml                      # Producción
├── docker-compose.dev.yml                  # Desarrollo
├── docker-compose.test.yml                 # Testing aislado
├── .env                                    # Variables de entorno (no commiteado)
└── README.md
```

---

## 4. Catálogo Completo de Entidades

### Entidades principales

| Entidad | Propósito |
|---------|----------|
| `Producto` | Producto del catálogo: nombre, categoría, stock en bodega, costo promedio, precio de venta |
| `ProductoEAN` | Mapeo código de barras (EAN) → Producto. Auto-aprendizaje desde compras |
| `ProductoCosto` | Registro histórico de costo por producto con fecha efectiva (`FechaInicio`/`FechaFin`). Clave para P&L preciso |
| `Maquina` | Máquina expendedora: nombre, ubicación |
| `ConfiguracionSlot` | Configuración actual de un slot: producto asignado, stock actual, capacidad máxima, precio |
| `Venta` | Venta individual: máquina, slot, producto, precio, costo congelado, fecha (UTC + local) |
| `Compra` | Factura de proveedor: puede ser mercadería o gasto general. Tiene `DetalleCompra` |
| `DetalleCompra` | Línea de compra: producto, cantidad, costo unitario, `PackSize` (para desglose automático) |
| `MovimientoCaja` | Movimiento de tesorería: tipo (GASTO, RETIRO, APORTE, OPERACIONAL), categoría, monto, comprobante |
| `Transferencia` | Plata que sale de Caja para conciliación. Estados: Pendiente → En Uso → Conciliado |
| `Rendicion` | Modelo viejo de conciliación por trabajador |
| `GastoRecurrente` | Plantilla de gasto fijo mensual (arriendo, sueldos, internet, etc.) |
| `OrdenCarga` | Orden de reposición de máquina. Tiene `DetalleOrdenCarga` |
| `DetalleOrdenCarga` | Línea de carga: slot, producto, cantidad cargada, cantidad sobrante |
| `TemplateRecarga` | Plantilla de ciclo de recarga para una máquina. Estados: Pendiente (0) → Terminado (2) |
| `PeriodoRecarga` | Período dentro de un template: fecha de recarga, máquina asociada |
| `SnapshotSlot` | Foto del estado de un slot en el momento de la recarga: stock, producto |
| `AccountingPeriod` | Período contable para conciliación (modelo nuevo) |
| `Auditoria` | Registro de auditoría: entidad, operación, usuario, timestamp |
| `Informe` | Archivo subido (Excel, imagen) almacenado como binario en BD |
| `User` | Usuario del sistema con username, password hash (BCrypt), y rol |

### Entidades históricas (rollback)

Cada una refleja el esquema de su entidad principal en un momento dado:

`CompraHistory`, `ProductoHistory`, `MaquinaHistory`, `VentaHistory`, `MovimientoCajaHistory`, `ConfiguracionSlotHistory`, `GastoRecurrenteHistory`, `OrdenCargaHistory`, `UserHistory`

El sistema de rollback (`AuditoriaController.Rollback`) restaura el estado de una entidad a partir de su registro histórico.

---

## 5. Catálogo Completo de la API

### AccountController
Autenticación y sesión.

| Método | Ruta | Descripción |
|--------|------|-------------|
| POST | `/api/account/login` | Login con username/password, establece cookie de auth |
| GET | `/logout` | Cierra sesión |
| GET | `/api/account/user` | Obtiene datos del usuario autenticado |

### AuditoriaController
Trazabilidad y recuperación.

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/api/auditoria` | Lista registros de auditoría (filtrable por usuario/acción) |
| GET | `/api/auditoria/history` | Últimos 200 registros históricos de todas las entidades |
| POST | `/api/auditoria/rollback/{entityType}/{entityId}/{historyId}` | Revierte una entidad a su estado histórico |

### CajaController
Tesorería — corazón financiero.

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/api/caja/resumen` | Resumen de caja del mes/año (4 KPIs) |
| GET | `/api/caja/movimientos` | Lista de movimientos del mes/año |
| GET | `/api/caja/productos-simple` | Lista simple de productos para dropdowns |
| POST | `/api/caja/registrar` | Registra un movimiento manual (gasto, ingreso, retiro) |
| POST | `/api/caja/upload` | Sube imagen de comprobante |
| GET | `/api/caja/exportar` | Exporta reporte de caja a Excel |
| GET | `/api/caja/valorizacion` | Valorización del inventario actual |

### ComprasController
Gestión de facturas y proveedores.

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/api/compras` | Lista compras (con límite opcional) |
| GET | `/api/compras/{id}` | Detalle de una compra |
| POST | `/api/compras` | Registra nueva compra |
| PUT | `/api/compras/{id}` | Actualiza compra existente |
| DELETE | `/api/compras/{id}` | Elimina compra |
| POST | `/api/compras/{id}/pagar` | Marca compra como pagada (genera movimiento en Caja) |
| POST | `/api/compras/upload-factura` | Sube factura para extracción OCR vía Gemini |
| POST | `/api/compras/{id}/factura` | Sube imagen de factura para una compra |
| GET | `/api/compras/{id}/factura` | Descarga imagen de factura |
| POST | `/api/compras/reconstruir-costos` | Reconstruye todo el historial de costos desde cero (replay de compras) |

### ContabilidadController
Períodos contables y conciliación (modelo nuevo).

| Método | Ruta | Descripción |
|--------|------|-------------|
| POST | `/api/contabilidad/transferencia-con-movimiento` | Crea transferencia + movimiento de caja asociado |
| POST | `/api/contabilidad/compra-vinculada` | Crea compra vinculada a transferencia |
| POST | `/api/contabilidad/gasto-vinculado` | Crea gasto vinculado a rendición |
| GET | `/api/contabilidad/trabajadores-activos` | Lista trabajadores activos |
| PUT | `/api/contabilidad/transferencia/{id}/monto` | Actualiza monto de transferencia |
| POST | `/api/contabilidad/transferencia/{id}/desvincular` | Desvincula transferencia de rendición |
| PUT | `/api/contabilidad/compra/{id}` | Actualiza compra vinculada |
| PUT | `/api/contabilidad/gasto/{id}` | Actualiza gasto vinculado |
| GET | `/api/contabilidad/periodos` | Lista períodos contables |
| GET | `/api/contabilidad/periodos/{id}` | Detalle completo de un período |
| POST | `/api/contabilidad/periodos` | Crea nuevo período contable |
| PUT | `/api/contabilidad/periodos/{id}` | Actualiza período |
| POST | `/api/contabilidad/periodos/{id}/cerrar` | Cierra período (bloquea modificaciones) |

### GastoRecurrenteController
Gastos fijos mensuales.

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/api/gastorecurrente` | Lista todos los gastos recurrentes |
| POST | `/api/gastorecurrente` | Crea gasto recurrente (Admin) |
| PUT | `/api/gastorecurrente/{id}` | Actualiza gasto recurrente (Admin) |
| DELETE | `/api/gastorecurrente/{id}` | Desactiva gasto recurrente (Admin) |
| GET | `/api/gastorecurrente/pendientes` | Gastos pendientes de registrar en el mes |
| POST | `/api/gastorecurrente/aplicar` | Aplica un gasto como movimiento de caja |
| POST | `/api/gastorecurrente/aplicar-todos` | Aplica todos los pendientes del mes |

### InformesController
Repositorio de archivos.

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/api/informes` | Lista archivos subidos |
| GET | `/api/informes/{id}` | Descarga archivo |
| POST | `/api/informes` | Sube archivo |
| DELETE | `/api/informes/{id}` | Elimina archivo |

### InventarioController
Catálogo masivo.

| Método | Ruta | Descripción |
|--------|------|-------------|
| POST | `/api/inventario/subir-catalogo` | Importa catálogo desde Excel |
| GET | `/api/inventario/lista` | Lista de productos |

### MaquinasController
Gestión de máquinas y slots.

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/api/maquinas` | Lista todas las máquinas |
| POST | `/api/maquinas` | Crea máquina |
| PUT | `/api/maquinas/{id}` | Actualiza máquina |
| DELETE | `/api/maquinas/{id}` | Elimina máquina |
| GET | `/api/maquinas/{id}/slots` | Configuración de slots de una máquina |
| POST | `/api/maquinas/{id}/slots` | Actualiza un slot individual |
| POST | `/api/maquinas/{id}/batch-actions` | Acciones masivas: REFILL, EMPTY, SWAP |

**Lógica de swap**: si actualizás un slot con un número que ya está ocupado, el sistema intercambia automáticamente los productos entre ambos slots con detección de colisiones.

### OrdenCargaController
Logística de reposición.

| Método | Ruta | Descripción |
|--------|------|-------------|
| POST | `/api/ordencarga` | Crea orden de carga |
| POST | `/api/ordencarga/finalizar` | Finaliza orden: descuenta bodega, actualiza slots, registra en Caja |
| PATCH | `/api/ordencarga/{id}/nombre` | Cambia nombre de la orden |
| PUT | `/api/ordencarga/{id}` | Actualiza orden |
| GET | `/api/ordencarga/historial` | Historial de órdenes (filtrable por máquina) |
| GET | `/api/ordencarga/{id}` | Detalle de una orden |
| GET | `/api/ordencarga/sugerencia` | Sugerencia de carga para una máquina |
| GET | `/api/ordencarga/exportar-sugerencia` | Exporta sugerencia a Excel |
| GET | `/api/ordencarga/exportar-consolidado` | Exporta consolidado global a Excel |
| POST | `/api/ordencarga/from-photo` | **OCR de foto de recarga**: extrae datos de carga desde una foto con Gemini |

### ProductosController
CRUD de productos y gestión EAN.

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/api/productos` | Lista todos los productos |
| GET | `/api/productos/{id}` | Detalle de un producto |
| POST | `/api/productos` | Crea producto |
| PUT | `/api/productos/{id}` | Actualiza producto (con recálculo opcional de costos) |
| DELETE | `/api/productos/{id}` | Elimina producto |
| GET | `/api/productos/{id}/historial-costos` | Historial de costos del producto |
| POST | `/api/productos/importar-catalogo` | Importa catálogo desde Excel |
| GET | `/api/productos/exportar-catalogo` | Exporta catálogo a Excel |
| POST | `/api/productos/ajustar-stock` | **BLOQUEADO**: redirige a usar Compras |
| GET | `/api/productos/ean` | Lista todos los mapeos EAN |
| GET | `/api/productos/ean/{id}` | Detalle de un mapeo EAN |
| POST | `/api/productos/ean` | Crea mapeo EAN |
| PUT | `/api/productos/ean/{id}` | Actualiza mapeo EAN |
| DELETE | `/api/productos/ean/{id}` | Elimina mapeo EAN |
| GET | `/api/productos/{id}/ean` | EANs de un producto específico |

### RendicionController
Conciliación — modelo viejo (por trabajador).

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/api/rendicion` | Lista rendiciones |
| GET | `/api/rendicion/{id}` | Detalle de rendición |
| POST | `/api/rendicion` | Crea rendición |
| PUT | `/api/rendicion/{id}` | Actualiza rendición |
| POST | `/api/rendicion/{id}/cerrar` | Cierra rendición |
| POST | `/api/rendicion/{id}/link-compra` | Vincula compra a transferencia de la rendición |
| POST | `/api/rendicion/{id}/unlink-compra/{compraId}` | Desvincula compra |
| POST | `/api/rendicion/{id}/link-gasto` | Vincula gasto |
| POST | `/api/rendicion/{id}/unlink-gasto/{gastoId}` | Desvincula gasto |
| GET | `/api/rendicion/{id}/resumen` | Resumen de rendición |
| GET | `/api/rendicion/{id}/full` | Rendición completa con resumen |
| GET | `/api/rendicion/transferencias-no-vinculadas` | Transferencias sin vincular |
| GET | `/api/rendicion/compras-no-vinculadas` | Compras sin vincular |
| GET | `/api/rendicion/gastos-no-vinculados` | Gastos sin vincular |
| DELETE | `/api/rendicion/{id}` | Elimina rendición (cascada: borra transferencias) |

### TemplateRecargaController
Planificación de ciclos de recarga con analítica.

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/api/templaterecarga` | Lista templates |
| GET | `/api/templaterecarga/{id}` | Template con sus períodos |
| POST | `/api/templaterecarga` | Crea template |
| PUT | `/api/templaterecarga/{id}` | Actualiza template |
| DELETE | `/api/templaterecarga/{id}` | Elimina template |
| POST | `/api/templaterecarga/{id}/terminar` | Transición: Pendiente → Terminado |
| POST | `/api/templaterecarga/{id}/reabrir` | Transición: Terminado → Pendiente |
| POST | `/api/templaterecarga/{templateId}/periodo/{periodoId}/slot-batch` | Acciones masivas sobre slots del período |
| GET | `/api/templaterecarga/{id}/analyze` | Análisis de stockout del template |
| GET | `/api/templaterecarga/maquina/{maquinaId}/slots` | Slots de máquina para crear template |
| POST | `/api/templaterecarga/{id}/sincronizar-ventas` | Sincroniza template con ventas históricas |
| POST | `/api/templaterecarga/sincronizar-todas-ventas` | Sincroniza TODOS los templates |
| PATCH | `/api/templaterecarga/{templateId}/periodo/{periodoId}/slot/{nroSlot}/sincronizar-producto` | Sincroniza producto de un slot |
| PUT | `/api/templaterecarga/{templateId}/periodo/{periodoId}/foto-guia` | Sube foto de guía de carga |
| GET | `/api/templaterecarga/{templateId}/periodo/{periodoId}/foto-guia` | Descarga foto de guía |
| DELETE | `/api/templaterecarga/{templateId}/periodo/{periodoId}/foto-guia` | Elimina foto de guía |
| PUT | `/api/templaterecarga/{templateId}/periodo/{periodoId}/foto-ocr` | Sube foto para OCR |
| GET | `/api/templaterecarga/{templateId}/periodo/{periodoId}/foto-ocr` | Descarga foto OCR |
| DELETE | `/api/templaterecarga/{templateId}/periodo/{periodoId}/foto-ocr` | Elimina foto OCR |

### TransferenciaController
Gestión de transferencias de dinero.

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/api/transferencia` | Lista transferencias |
| GET | `/api/transferencia/pendientes` | Transferencias pendientes o en uso |
| GET | `/api/transferencia/{id}` | Detalle de transferencia |
| POST | `/api/transferencia` | Crea transferencia |
| PUT | `/api/transferencia/{id}` | Actualiza transferencia |
| DELETE | `/api/transferencia/{id}` | Elimina transferencia |

### UsersController
Administración de usuarios (Admin only).

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/api/users` | Lista usuarios |
| POST | `/api/users` | Crea usuario |
| PUT | `/api/users/{id}` | Actualiza rol/contraseña |
| DELETE | `/api/users/{id}` | Elimina usuario |

### VentasController
Ventas, sincronización y analítica pesada.

| Método | Ruta | Descripción |
|--------|------|-------------|
| POST | `/api/ventas/subir-transbank` | Sube archivo de pagos Transbank |
| GET | `/api/ventas/fix-dates` | Corrige fechas corruptas (mantenimiento) |
| GET | `/api/ventas/recalcular-costos` | Recalcula costos históricos (deprecado) |
| GET | `/api/ventas/lista-maquinas` | Lista máquinas para dropdowns |
| GET | `/api/ventas/lista-productos` | Lista productos para dropdowns |
| GET | `/api/ventas/dashboard-stats` | Estadísticas del dashboard |
| GET | `/api/ventas/stock-critico` | Productos bajo stock mínimo |
| GET | `/api/ventas/reporte-rango` | Reporte de ventas por rango de fechas |
| GET | `/api/ventas/informe-financiero` | Informe financiero (P&L) |
| GET | `/api/ventas/exportar` | Exporta reporte a Excel |
| POST | `/api/ventas/sync-portal` | Dispara sincronización manual con OurVend |
| GET | `/api/ventas/analisis-productos` | Clasificación ABC + Estrella/Vaca/Incógnita/Cacho |
| GET | `/api/ventas/stockout-analysis` | Análisis de quiebres de stock |
| GET | `/api/ventas/ventas-diarias` | Ventas diarias por producto/máquina |
| GET | `/api/ventas/categoria-analisis` | Análisis por categoría de producto |
| GET | `/api/ventas/purchase-suggestion` | Sugerencia de compras a 30 días |
| GET | `/api/ventas/purchase-suggestion/export` | Exporta sugerencia de compras |
| DELETE | `/api/ventas/borrar-rango` | Borra ventas en rango de fechas |

---

## 6. Catálogo Completo de Servicios

### Infrastructure/Services/

| Servicio | Interfaz | Responsabilidad |
|----------|----------|----------------|
| `CajaService` | `ICajaService` | CRUD de movimientos de caja, resumen mensual, upload de comprobantes |
| `CajaBusinessService` | — | Lógica de negocio de caja: cálculo de KPIs y P&L |
| `CompraService` | `ICompraService` | Gestión de compras: CPP, desglose de packs, seguimiento de `ProductoCosto` |
| `VentasService` | `IVentasService` | CRUD de ventas, corrección de fechas, borrado |
| `InventarioService` | `IInventarioService` | Catálogo de productos, import/export Excel |
| `MaquinaService` | `IMaquinaService` | CRUD de máquinas, gestión de slots, batch actions con swap |
| `TransferenciaService` | `ITransferenciaService` | CRUD de transferencias, ciclo de vida |
| `RendicionService` | `IRendicionService` | Ciclo de vida de rendiciones, link/unlink de compras y gastos |
| `ContabilidadService` | `IContabilidadService` | Períodos contables, transferencias con movimiento, compras/gastos vinculados |
| `GastoRecurrenteService` | `IGastoRecurrenteService` | CRUD de gastos recurrentes, detección de pendientes, aplicación individual/masiva |
| `OrdenCargaService` | `IOrdenCargaService` | Creación y finalización de órdenes de carga, retorno de stock |
| `TemplateRecargaService` | `ITemplateRecargaService` | **Fachada**: delega a lifecycle y analytics |
| `TemplateRecargaLifecycleService` | `ITemplateRecargaLifecycleService` | **State machine**: Pendiente → Terminado, reapertura, sync a `ConfiguracionSlots` |
| `TemplateRecargaAnalyticsService` | `ITemplateRecargaAnalyticsService` | Análisis de stockout, sync de ventas, cross-template chain, dead slot detection |
| `SalesAnalyticsService` | `ISalesAnalyticsService` | Dashboard stats, reportes, clasificación ABC, análisis por categoría |
| `PurchasingService` | `IPurchasingService` | Stock crítico, sugerencia de compras con caché |
| `SalesImportService` | `ISalesImportService` | Importación Excel de ventas, conciliación Transbank con **bundle matching** |
| `SyncOrchestratorService` | `ISyncOrchestratorService` | Orquesta llamadas al scraper + importación de ventas |
| `InformesService` | `IInformesService` | Almacenamiento y recuperación de archivos |
| `AuditService` | `IAuditService` | Registro de auditoría |
| `AutomatedReportService` | — | **Background service**: sync diario a las 23:00 de todas las máquinas |
| `FacturaOcrService` | `IFacturaOcrService` | Cliente HTTP tipado para OCR de facturas vía scraper |
| `RecargaOcrService` | `IRecargaOcrService` | Cliente HTTP tipado para OCR de fotos de recarga vía scraper |
| `ProductMatchingService` | `IProductMatchingService` | Mapeo EAN/SKU → Producto con **auto-aprendizaje** desde compras |
| `ExcelExportService` | `IExcelExportService` | Exportación Excel genérica para reportes |
| `OrdenCargaExcelService` | `IOrdenCargaExcelService` | Exportación Excel específica para órdenes de carga |
| `CatalogExcelService` | `ICatalogExcelService` | Import/export de catálogo de productos desde Excel |

### Infrastructure/Clients/

| Cliente | Interfaz | Responsabilidad |
|---------|----------|----------------|
| `ScraperClient` | `IScraperClient` | Cliente HTTP tipado para el microservicio Python |

### Infrastructure/Data/Repositories/

| Repositorio | Interfaz | Responsabilidad |
|-------------|----------|----------------|
| `ProductoEANRepository` | `IProductoEANRepository` | Persistencia de mapeos EAN |
| `VentaRepository` | `IVentaRepository` | Consultas de ventas |
| `MaquinaRepository` | `IMaquinaRepository` | Consultas de máquinas |
| `AccountingPeriodRepository` | `IAccountingPeriodRepository` | Consultas de períodos contables |

---

## 7. Algoritmos y Features Clave

### 7.1 Sistema de Costos Históricos (ProductoCosto)

Cada producto tiene una **línea de tiempo de costos**. Cada registro `ProductoCosto` tiene `FechaInicio` y `FechaFin`. Cuando se registra una venta, el costo se **congela** al costo vigente en ese momento exacto.

Cuando entra una compra nueva:
1. Se cierra el registro `ProductoCosto` actual (`FechaFin = now`)
2. Se recalcula el costo promedio ponderado con la nueva compra
3. Se crea un nuevo `ProductoCosto` con el nuevo costo

**Reconstrucción total**: `POST /api/compras/reconstruir-costos` hace replay de TODAS las compras en orden cronológico para regenerar la línea de tiempo completa desde cero.

### 7.2 Desglose de Packs (Pack Splitting)

Cuando una compra tiene `PackSize > 1` en un `DetalleCompra`:
- `CompraService.DesglosarPacks()` convierte: cantidad × PackSize, costo unitario / PackSize
- Ejemplo: compra de 5 packs de 12 unidades a $6.000 → se registra como 60 unidades a $100 c/u

### 7.3 Auto-Aprendizaje EAN/SKU

`ProductMatchingService`:
- `SaveMappingAsync()`: cuando se ingresa un EAN en una compra, lo asocia al producto
- `SaveSkuMappingAsync()`: ídem para SKUs (códigos internos de proveedor)
- La próxima vez que aparezca ese EAN o SKU, el sistema ya sabe qué producto es

### 7.4 TemplateRecarga — State Machine y Analítica

#### Ciclo de vida
```
Pendiente (0) ──→ Terminado (2)
     ↑                  │
     └── Reabrir ───────┘
```

Al **terminar** un template, `TemplateRecargaLifecycleService` sincroniza los `SnapshotSlots` con las `ConfiguracionSlots` reales de la máquina.

#### Cross-Template Chain
`TemplateRecargaAnalyticsService.BuildCrossTemplateChain()` busca TODOS los templates terminados de una misma máquina y los ordena por `FechaRecarga`, permitiendo análisis que cruzan múltiples ciclos.

#### Stockout Analysis
Por cada slot en el template:
- `VelocidadDiaria` = unidades vendidas / días del período
- `DiasHastaStockout` = stock actual / velocidad diaria → predicción de cuándo se vacía
- `EsDeadSlot` = true si el slot no vendió nada en todo el período
- `FillPct` = stock en SnapshotSlot / stock en ConfiguracionSlot actual → qué tan lleno está

#### Template-Based Stock Critico
Cuando `VendingConfig.UseTemplateInventoryForStockCritico = true`, el sistema usa los `SnapshotSlots` del último template terminado (en vez de `ConfiguracionSlots`) para calcular stock crítico. Esto da una visión más realista del inventario en máquina.

### 7.5 Conciliación Transbank — Bundle Matching

`SalesImportService` concilia pagos Transbank con ventas registradas:
1. Intenta **match 1:1** por monto
2. Si no encuentra, ejecuta **bundle matching** recursivo: busca combinaciones de ventas cuya suma exacta coincida con el pago (compra en carrito)
3. Si no hay match posible, crea una **venta fantasma** `TB-SIN-VENTA` para mantener trazabilidad del pago

### 7.6 Clasificación ABC de Productos

`SalesAnalyticsService` clasifica productos por contribución a ventas:
- **A**: productos que representan el 80% de las ventas
- **B**: siguiente 15%
- **C**: 5% restante

Además, cruza rotación con margen para clasificar como:
- **Estrella**: alta rotación + alto margen → mantener y potenciar
- **Vaca**: alta rotación + bajo margen → volumen, no tocar
- **Incógnita**: baja rotación + alto margen → investigar por qué no vende
- **Cacho**: baja rotación + bajo margen → candidato a eliminar

### 7.7 Rollback de Entidades

`AuditoriaController.Rollback` permite revertir cualquier entidad a un estado anterior:
1. El `AuditSaveChangesInterceptor` guarda automáticamente el estado previo en las tablas `*History`
2. El endpoint de rollback recibe `entityType`, `entityId` e `historyId`
3. Restaura los valores desde el registro histórico a la entidad activa

### 7.8 Fechas y Timezone

Cada `Venta` tiene dos campos de fecha:
- `FechaHora`: timestamp UTC
- `FechaLocal`: fecha en zona horaria Chile (`America/Santiago`, UTC-3 a UTC-4 según horario de verano)

El `DateTimeModelBinder` personalizado parsea fechas en formato `dd/MM/yyyy` del frontend.

### 7.9 Ajuste de Stock Bloqueado

`ProductosController.AjustarStock` lanza una excepción deliberadamente. El sistema fuerza que todo movimiento de stock pase por:
- **Compras** (entrada de mercadería)
- **Órdenes de Carga** (salida a máquinas)
- **Finalización de Carga** (retorno de sobrante a bodega)

Esto garantiza **trazabilidad total** del inventario.

---

## 8. Microservicio Python — Scraper

### Endpoints

| Método | Ruta | Descripción |
|--------|------|-------------|
| POST | `/download` | Descarga reporte OurVend para máquina/rango de fechas. Body: `{ machine_id, start_date, end_date }`. Devuelve Excel. Background task limpia el archivo después |
| POST | `/api/ocr/invoice` | OCR de factura: recibe imagen, devuelve JSON con datos extraídos (proveedor, items, totales) vía Gemini |
| POST | `/api/ocr/recarga` | OCR de foto de recarga: recibe imagen de lista manuscrita, devuelve JSON con pares slot/cantidad |
| GET | `/health` | Health check: `{ status: "ok", service: "Vending Scraper" }` |

### Módulo alternativo

`download_ourvend_report_alt.py`: versión en inglés del scraper con selector de "todas las máquinas".

### Variables de entorno

```
OURVEND_USER      — Usuario del portal OurVend
OURVEND_PASS      — Contraseña del portal
GEMINI_API_KEY    — API key de Google Gemini
```

---

## 9. Background Workers

### AutomatedReportService

- **Tipo**: `IHostedService` (BackgroundService)
- **Schedule**: diario a las 23:00 (hora Chile)
- **Acción**: sincroniza TODAS las máquinas llamando a `SyncOrchestratorService`
- **Logging**: registra resultado por máquina
- **Fallo**: reintenta al día siguiente

---

## 10. Middleware e Infraestructura

| Componente | Archivo | Función |
|-----------|---------|---------|
| **GlobalProblemDetailsMiddleware** | `Web/Middleware/GlobalProblemDetailsMiddleware.cs` | Convierte excepciones a RFC 7807 Problem Details (JSON estructurado con type, title, status, detail) |
| **DateTimeModelBinder** | `Web/ModelBinders/` | Binding personalizado para fechas en formato chileno `dd/MM/yyyy` |
| **AuditSaveChangesInterceptor** | `Infrastructure/Interceptors/AuditSaveChangesInterceptor.cs` | Interceptor de EF Core: guarda automáticamente `Auditoria` y entidades `*History` en cada `SaveChanges` |
| **PersistingAuthenticationStateProvider** | `Web/Auth/` | Proveedor de estado de autenticación para Blazor (cookie-based) |
| **Serilog Request Logging** | `Program.cs` | `UseSerilogRequestLogging()` — registra duración de cada HTTP request en JSON |
| **Rate Limiting** | `Program.cs` | Fixed Window: 5 requests/minuto en login (política `LoginPolicy`) |
| **Response Compression** | `Program.cs` | Gzip/Brotli para responses, incluyendo `application/octet-stream` |
| **Health Checks** | `Program.cs` | `/health` (liveness, solo self-check), `/health/db` (readiness, verifica SQL Server) |
| **Auto-Migrate** | `Program.cs` | `context.Database.Migrate()` al iniciar — aplica migraciones pendientes automáticamente |
| **Admin Seed** | `Program.cs` | Si no hay usuarios, crea admin con password de `SEED_ADMIN_PASSWORD` o `admin` en Development |

---

## 11. Base de Datos

### Motor
SQL Server 2022, accedido vía Entity Framework Core.

### Migraciones
Automáticas al iniciar (`Database.Migrate()` en `Program.cs`). No se requiere acción manual.

### Tablas principales
`Productos`, `ProductosEAN`, `ProductosCostos`, `Maquinas`, `ConfiguracionSlots`, `Ventas`, `Compras`, `DetallesCompra`, `MovimientosCaja`, `Transferencias`, `Rendiciones`, `GastosRecurrentes`, `OrdenesCarga`, `DetallesOrdenCarga`, `TemplatesRecarga`, `PeriodosRecarga`, `SnapshotsSlot`, `AccountingPeriods`, `Auditoria`, `Informes`, `Users`

### Tablas históricas
`ComprasHistory`, `ProductosHistory`, `MaquinasHistory`, `VentasHistory`, `MovimientosCajaHistory`, `ConfiguracionSlotsHistory`, `GastosRecurrentesHistory`, `OrdenesCargaHistory`, `UsersHistory`

---

## 12. Configuración

### `appsettings.json` — Secciones clave

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=db;Database=VendingDB;..."
  },
  "VendingConfig": {
    "CajaStartDate": "2026-01-01",
    "TransbankFee": 80,
    "RotacionStockMinimoDias": 30,
    "RotacionUmbralCritico": 7,
    "FacturaUploadPath": null,
    "UseTemplateInventoryForStockCritico": false
  },
  "AnalyticsThresholds": {
    "RotacionAlta": 1.0,
    "RotacionMedia": 0.2,
    "MargenAlto": 0.50
  }
}
```

| Config | Efecto |
|--------|--------|
| `CajaStartDate` | Fecha mínima permitida para movimientos de caja |
| `TransbankFee` | Comisión Transbank en pesos (se descuenta del pago) |
| `RotacionStockMinimoDias` | Días usados para calcular rotación |
| `RotacionUmbralCritico` | Umbral para alerta de stock crítico (días) |
| `UseTemplateInventoryForStockCritico` | Si es true, usa SnapshotSlots en vez de ConfiguracionSlots |
| `RotacionAlta` / `RotacionMedia` | Umbrales de rotación para clasificación ABC |
| `MargenAlto` | Umbral de margen para clasificación Estrella/Cacho |

---

## 13. Despliegue

### Entornos

| Entorno | Archivo | Puertos |
|---------|---------|---------|
| Producción | `docker-compose.yml` | 8080 (web), 1433 (db), 8000 (scraper) |
| Desarrollo | `docker-compose.dev.yml` | 8081, 1434, 8001 |
| Testing | `docker-compose.test.yml` | 8082, 1435, 8002 |

### Comandos

```bash
# Producción
docker-compose up -d

# Desarrollo
docker-compose -f docker-compose.dev.yml up -d --build

# Testing aislado
docker-compose -f docker-compose.test.yml up -d
```

### Variables de entorno (`.env`)

```env
MSSQL_SA_PASSWORD=<password>
OURVEND_USER=<usuario>
OURVEND_PASS=<contraseña>
GEMINI_API_KEY=<api-key>
```

---

## 14. Desarrollo Local

### Opción A: Full Docker (recomendado)

```bash
docker-compose -f docker-compose.dev.yml up -d --build
```

### Opción B: Hot Reload (solo dependencias en Docker)

```bash
# Levantar DB + Scraper
docker-compose -f docker-compose.dev.yml up -d db-dev scraper-dev

# Configurar appsettings.Development.json → localhost,1434
dotnet run --project src/VendingManager/VendingManager.csproj
```

### User Secrets
`Program.cs` carga `AddUserSecrets<Program>()` en Development. Las variables de Docker (`.env`) y user-secrets coexisten.

### Documentación API
En Development: `/scalar/v1` (Scalar UI sobre OpenAPI).

---

## 15. Testing

```
src/VendingManager.Tests/
├── Components/        # Tests de componentes Blazor
├── Controllers/       # Tests de controladores
├── Services/          # Tests de servicios
├── Entities/          # Tests de entidades
├── DTOs/              # Tests de DTOs
├── Enums/             # Tests de enumeraciones
├── Interceptors/      # Tests del interceptor de auditoría
├── Interfaces/        # Tests de interfaces
├── Middleware/        # Tests de middleware
├── TestData/          # Datos de prueba
└── TestResults/       # Resultados

src/VendingManager.Tests.Viewport/  # Tests de responsive
```

```bash
dotnet test src/VendingManager.Tests/VendingManager.Tests.csproj
```

---

## 16. Convenciones de Código

- **Idioma**: código, comentarios, nombres de variables, commits → inglés. Documentación de negocio → español
- **Commits**: convencionales (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`)
- **Controladores delgados**: zero lógica de negocio en controladores. Toda la lógica en servicios de Infrastructure
- **Interfaces en Core**: todos los contratos en `Core/Interfaces/`, implementaciones en `Infrastructure/Services/`
- **Una responsabilidad por servicio**: `TemplateRecargaService` es fachada, `TemplateRecargaLifecycleService` maneja el state machine, `TemplateRecargaAnalyticsService` maneja la analítica
- **Auto-migrate**: no se necesita aplicar migraciones manualmente
- **Trazabilidad**: el `AuditSaveChangesInterceptor` registra todo automáticamente

---

## 17. Dónde Tocar Según el Cambio

| Tipo de cambio | Archivos |
|---------------|----------|
| Nuevo endpoint REST | `Controllers/` → nuevo método o controlador, interfaz en `Core/Interfaces/`, implementación en `Infrastructure/Services/` |
| Modificar lógica de negocio | Servicio en `Infrastructure/Services/` |
| Nueva entidad | `Core/Entities/` → modelo, migración, registro en `ApplicationDbContext` |
| Nueva página Blazor | `Components/Pages/` o `VendingManager.Web/` |
| Modificar scraping | `services/scraper/` → nuevo endpoint en `scraper_api.py` y/o nuevo método en `ScraperClient` |
| Cambiar comportamiento de auditoría | `Infrastructure/Interceptors/AuditSaveChangesInterceptor.cs` |
| Agregar health check | `Program.cs` → `AddHealthChecks()` |

---

## 18. Decisiones de Arquitectura (ADRs)

Resumen de `docs/architecture/decisions/`:

| ADR | Decisión |
|-----|---------|
| **0001** | Formato ADR para documentar decisiones técnicas |
| **0002** | Separación de Excel en `CatalogExcelService` y `OrdenCargaExcelService` |
| **0003** | Separación de Ventas: importación vs reporting |
| **003** | OCR con Gemini Vision en lugar de Tesseract tradicional |
| **004** | Trazabilidad de gastos recurrentes con alertas de pendientes |
| **005** | Trazabilidad completa cargas → inventario → finanzas |
| **010** | Sincronización histórica de ventas con manejo de duplicados |
| **011** | Compras soportan gastos generales además de mercadería |
| **012** | Eliminación y reversión de compras con trazabilidad |

---

## 19. Páginas Blazor (catálogo completo)

| Página | Ruta | Módulo |
|--------|------|--------|
| `Home.razor` | `/` | Dashboard principal |
| `Login.razor` | `/login` | Inicio de sesión |
| `Caja.razor` | `/caja` | Tesorería |
| `Compras.razor` | `/compras` | Historial de compras |
| `NuevaCompra.razor` | `/compras/nueva` | Registrar compra (con OCR) |
| `EditarCompra.razor` | `/compras/editar/{id}` | Editar compra |
| `Inventario.razor` | `/inventario` | Stock y productos |
| `GestionEan.razor` | `/ean` | Gestión de códigos EAN |
| `GestionMaquinas.razor` | `/maquinas` | Máquinas y slots |
| `TemplatesRecarga.razor` | `/templates` | Plantillas de recarga |
| `Reposicion.razor` | `/reposicion` | Reposición |
| `Cargas.razor` | `/cargas` | Órdenes de carga |
| `Rendiciones.razor` | `/rendiciones` | Rendiciones (modelo viejo) |
| `ContabilidadPage.razor` | `/contabilidad` | Períodos contables |
| `Finanzas.razor` | `/finanzas` | Finanzas |
| `Ventas.razor` | `/ventas` | Ventas |
| `AnalisisVentas.razor` | `/analisis-ventas` | Análisis ABC |
| `InformeFinanciero.razor` | `/informe-financiero` | P&L |
| `StockoutDashboard.razor` | `/stockout` | Análisis de quiebres |
| `PurchaseReport.razor` | `/sugerencia-compras` | Sugerencia de compras |
| `Reportes.razor` | `/reportes` | Reportes |
| `Historial.razor` | `/historial` | Historial |
| `Admin/Auditoria.razor` | `/admin/auditoria` | Auditoría y rollback (Admin) |
| `Admin/GestionUsuarios.razor` | `/admin/usuarios` | Gestión de usuarios (Admin) |

---

## 20. Troubleshooting

| Problema | Causa probable | Solución |
|----------|---------------|----------|
| Error conexión SQL Server | Contraseña SA no configurada | Verificar `.env`: `MSSQL_SA_PASSWORD` |
| Scraper no responde | Credenciales OurVend inválidas | Verificar `OURVEND_USER` / `OURVEND_PASS` en `.env` |
| OCR no funciona | API key Gemini inválida o sin cuota | Verificar `GEMINI_API_KEY` |
| Error 429 en login | Rate limiting (5 req/min) | Esperar 1 minuto o reiniciar contenedor |
| Puerto en uso | Conflicto con otro servicio | Cambiar puertos en docker-compose o detener el servicio |
| Migraciones no aplican | Error de conexión o esquema corrupto | Revisar logs del contenedor `webapp` |
| Stock no se actualiza | Se intentó ajuste manual | Usar Compras (entrada) u Órdenes de Carga (salida) — el ajuste manual está bloqueado |
| Costos inconsistentes | Línea de tiempo de costos corrupta | Ejecutar `POST /api/compras/reconstruir-costos` |
| Sync automático no corre | `AutomatedReportService` falló | Revisar logs del contenedor webapp; reintenta en 24h |

---

## 21. API Contract Reference — Completo

Cada endpoint documentado con request, response, auth, validaciones y códigos de error.  
**Global Exception Mapping** vía `GlobalProblemDetailsMiddleware` (RFC 7807):
- `ArgumentException` → 400 Bad Request
- `UnauthorizedAccessException` → 401 Unauthorized
- `ForbiddenAccessException` → 403 Forbidden
- `KeyNotFoundException` → 404 Not Found
- `InvalidOperationException` → 409 Conflict
- `DbUpdateException` (FK) → 409 Conflict
- `DbUpdateConcurrencyException` → 409 Conflict
- Unhandled → 500 Internal Server Error

### AccountController

| Endpoint | Auth | Request | Success | Errors |
|----------|------|---------|---------|--------|
| `POST /api/account/login` | None | `LoginDto { Username, Password }` | 200 "Login exitoso" | 401 (creds inválidas, timing-attack delay 100-300ms) |
| `GET /logout` | None | — | 302 → `/` | — |
| `GET /api/account/user` | Any | — | 200 `{ name, role }` | — |

**Rate Limited**: LoginPolicy — 5 req/min (429 on overflow). Password via BCrypt (work factor 11).

### AuditoriaController (Admin only)

| Endpoint | Request | Success |
|----------|---------|---------|
| `GET /api/auditoria?usuario=&accion=` | Query: usuario, accion | 200 `AuditoriaDto[]` |
| `GET /api/auditoria/history` | — | 200 `HistoryListItemDto[]` (max 200, Timestamp desc) |
| `POST /api/auditoria/rollback/{entityType}/{entityId}/{historyId}` | Route params: entityType (producto/compra/maquina/venta/movimientocaja/configuracionslot/gastorecurrente/ordencarga/user), entityId, historyId | 200 `RollbackResponseDto` |

### CajaController (Any auth)

| Endpoint | Request | Success | Errors |
|----------|---------|---------|--------|
| `GET /api/caja/resumen?month=&year=` | Query: month, year (default: current) | 200 `CajaResumenDto` (SaldoAnterior, Ventas, Gastos, EBITDA, etc.) | — |
| `GET /api/caja/movimientos?month=&year=` | Query: month, year | 200 `MovimientoCajaDto[]` | — |
| `GET /api/caja/productos-simple` | — | 200 `[{ id, nombre, stockBodega }]` | — |
| `POST /api/caja/registrar` | Body: `MovimientoCaja` entity | 200 "Movimiento registrado." | 400 (monto cero, mes cerrado, fecha < CajaStartDate) |
| `POST /api/caja/upload` | Form: file, category? | 200 `{ Path }` | 400 "No file uploaded" |
| `GET /api/caja/exportar?month=&year=` | Query: month, year | 200 Excel file | 400 "Error al generar Excel" |
| `GET /api/caja/valorizacion` | — | 200 `{ valorBodega, valorMaquinas, valorTotal, fechaCalculo }` | — |

### ComprasController (Any auth)

| Endpoint | Request | Success | Notes |
|----------|---------|---------|-------|
| `GET /api/compras?limit=` | Query: limit (optional) | 200 `CompraDto[]` | — |
| `GET /api/compras/{id}` | Route: id | 200 `CompraDto` | 404 if not found |
| `POST /api/compras` | Body: `RegistrarCompraRequestDto` (Proveedor, TipoFactura, Detalles min 1, PagadaCaja, SubcategoriaGasto) | 201 `{ id }` | Desglose packs >1, CPP, ProductoCosto cierre/apertura, EAN learning |
| `PUT /api/compras/{id}` | Body: `RegistrarCompraRequestDto` | 204 No Content | Reversa impacto anterior + re-ejecuta |
| `DELETE /api/compras/{id}` | Route: id | 204 No Content | — |
| `POST /api/compras/{id}/pagar` | Route: id | 204 No Content | — |
| `POST /api/compras/upload-factura` | Form: file | 200 `OcrInvoiceResultDto` | IA vía Gemini. Valida EAN (8-13 dígitos) |
| `POST /api/compras/{id}/factura` | Form: file | 200 `{ Path }` | — |
| `GET /api/compras/{id}/factura` | Route: id | 200 image/pdf | 404 si no tiene |
| `POST /api/compras/reconstruir-costos` | — | 200 `{ productosProcesados, comprasReprocesadas, registrosProductoCostoCreados, detallesSinProducto }` | Replay total de compras |

### ContabilidadController (Any auth)

| Endpoint | Request | Success | Validaciones |
|----------|---------|---------|-------------|
| `POST /api/contabilidad/transferencia-con-movimiento` | `TransferenciaConMovimientoRequest { RendicionId, Trabajador, Monto > 0, PeriodoId? }` | 201 `TransferenciaDto` | Crea Rendicion si no existe. Crea MovimientoCaja RETIRO negativo automático |
| `POST /api/contabilidad/compra-vinculada` | `CompraVinculadaRequest : RegistrarCompraRequestDto + TransferenciaId, RendicionId, Trabajador` | 201 `CompraDto` | TransferenciaId > 0. Detalles con Cantidad>0, CostoUnitario>=0 |
| `POST /api/contabilidad/gasto-vinculado` | `GastoVinculadoRequest { RendicionId, Trabajador, Descripcion, Monto > 0, Categoria="GENERAL" }` | 201 `MovimientoCajaDto` | Trabajador y Monto obligatorios |
| `GET /api/contabilidad/trabajadores-activos` | — | 200 `TrabajadorActivoDto[]` | — |
| `PUT /api/contabilidad/transferencia/{id}/monto` | `{ monto: decimal }` (monto > 0) | 204 No Content | Actualiza también MovimientoCaja.Monto |
| `POST /api/contabilidad/transferencia/{id}/desvincular` | Route: id | 200 "Transferencia desvinculada" | Si no hay más compras, estado → Pendiente |
| `PUT /api/contabilidad/compra/{id}` | `UpdateCompraRequest { Proveedor?, Fecha?, Detalles? }` | 200 `CompraDto` | — |
| `PUT /api/contabilidad/gasto/{id}` | `UpdateGastoRequest { Descripcion?, Monto?, Categoria? }` | 200 `MovimientoCajaDto` | — |
| `GET /api/contabilidad/periodos?desde=&hasta=` | Query: desde, hasta (optional) | 200 `AccountingPeriodDto[]` | — |
| `GET /api/contabilidad/periodos/{id}` | Route: id | 200 `AccountingPeriodFullDto` (con Transferencias+Gastos) | 404 |
| `POST /api/contabilidad/periodos` | `CreatePeriodoRequest { Name (req), Trabajador? }` | 201 `AccountingPeriodDto` | Name no vacío |
| `PUT /api/contabilidad/periodos/{id}` | `UpdatePeriodoRequest` | 200 `AccountingPeriodDto` | — |
| `POST /api/contabilidad/periodos/{id}/cerrar` | Route: id | 200 "Período cerrado" | Auto-concilia: cierra transfers con compras/gastos |

### GastoRecurrenteController (Any auth, Admin para POST/PUT/DELETE)

| Endpoint | Auth | Request | Success |
|----------|------|---------|---------|
| `GET /api/gastorecurrente` | Any | — | 200 `GastoRecurrente[]` |
| `POST /api/gastorecurrente` | Admin | `GastoRecurrente` entity | 200 |
| `PUT /api/gastorecurrente/{id}` | Admin | `GastoRecurrente` entity | 200 |
| `DELETE /api/gastorecurrente/{id}` | Admin | Route: id | 200 (soft-delete: Activo=false) |
| `GET /api/gastorecurrente/pendientes?month=&year=` | Any | Query: month, year | 200 `GastoPendienteDto[]` |
| `POST /api/gastorecurrente/aplicar` | Any | `{ GastoRecurrenteId, Month?, Year?, MontoReal? }` | 200. Idempotente: 409 si ya registrado |
| `POST /api/gastorecurrente/aplicar-todos` | Any | `{ Month?, Year?, MontosPersonalizados? }` | 200 "Aplicados: X/Y" |

### TemplateRecargaController (Any auth, verify per endpoint)

| Endpoint | Request | Success | Errors específicos |
|----------|---------|---------|-------------------|
| `GET /api/templaterecarga` | — | 200 `List<TemplateRecargaDto>` | — |
| `GET /api/templaterecarga/{id}` | Route: id | 200 `TemplateRecargaDto` con Periodos+Snapshots | 404 |
| `POST /api/templaterecarga` | `CreateTemplateRecargaDto { Nombre (req), Periodos (min 1) }` | 201 `TemplateRecargaDto` | 400 (nombre vacío, sin períodos), 409 CHAIN_CONFLICT |
| `PUT /api/templaterecarga/{id}` | `UpdateTemplateRecargaDto` | 200 | 409 CHAIN_CONFLICT |
| `DELETE /api/templaterecarga/{id}` | Route: id | 204 | — |
| `POST /api/templaterecarga/{id}/terminar` | Route: id | 200 → Estado=2, sync ConfiguracionSlots | 400 estado inválido |
| `POST /api/templaterecarga/{id}/reabrir` | Route: id | 200 → Estado=0 | 400 estado inválido |
| `POST /api/templaterecarga/{id}/sincronizar-ventas?actualizarCostos=` | Route: id | 200 `{ message, count }` | — |
| `POST /api/templaterecarga/sincronizar-todas-ventas?actualizarCostos=` | — | 200 `SyncAllVentasResultDto` | — |
| `PATCH /api/templaterecarga/{tId}/periodo/{pId}/slot/{nro}/sincronizar-producto` | `{ productoId }` (productoId > 0) | 200 `SyncSlotProductoResultDto` | 400 productoId inválido |
| `POST /api/templaterecarga/{tId}/periodo/{pId}/slot-batch` | `SlotBatchRequest { Actions: List<SlotActionDto> }` (min 1) | 200 `SlotBatchResponse` | 400 EMPTY_ACTIONS, 404 NOT_FOUND, 409 INVALID_ACTION |
| `GET /api/templaterecarga/{id}/analyze?umbralHoras=24` | Query: umbralHoras | 200 `List<StockoutAnalysisDto>` | Cross-template chain analysis |
| `GET /api/templaterecarga/maquina/{maquinaId}/slots` | Route: id | 200 `List<SnapshotSlotDto>` | — |
| `PUT/DELETE /api/templaterecarga/{tId}/periodo/{pId}/foto-guia` | Form: file (max 10MB, jpg/png/gif/webp) | 200/204 | 400 FILE_MISSING, 413 FOTO_TOO_LARGE, 415 FOTO_INVALID_TYPE |
| `PUT/DELETE /api/templaterecarga/{tId}/periodo/{pId}/foto-ocr` | Form: file (max 5MB, jpg/png/gif/webp) | 200/204 | Idem |

### OrdenCargaController (Any auth)

| Endpoint | Request | Success | Notes |
|----------|---------|---------|-------|
| `POST /api/ordencarga` | `{ Nombre?, MaquinaId?, Items[], Fecha?, IgnorarStock }` | 200 `OrdenCargaDto` | Descuenta stock bodega al crear |
| `POST /api/ordencarga/finalizar` | `{ OrdenId, Retornos[] }` | 200. Retorna sobrantes a bodega, suma stock a slots, crea MovimientoCaja | Valida CantidadRetornada <= CantidadSolicitada |
| `GET /api/ordencarga/historial?maquinaId=0` | Query: maquinaId (0=todas) | 200 `OrdenCargaDto[]` | — |
| `GET /api/ordencarga/sugerencia?maquinaId=` | Query: maquinaId | 200 `StockCriticoDto[]` | A Cargar = CapacidadMax - StockActual |
| `GET /api/ordencarga/exportar-sugerencia?maquinaId=` | Query: maquinaId | 200 Excel | — |
| `GET /api/ordencarga/exportar-consolidado` | — | 200 Excel global | — |
| `POST /api/ordencarga/from-photo?maquinaId=` | Form: file (max 10MB, image/*) + query maquinaId | 200 `OcrRecargaResultDto` | Gemini OCR. Matching: offset → fuzzy (Levenshtein ≤ 2) |

### VentasController (Any auth)

| Endpoint | Request | Success |
|----------|---------|---------|
| `POST /api/ventas/subir-ventas-maquina?fechaLimite=` | Form: Excel file | 200 "PROCESADO: X nuevas | Y dupl..." |
| `POST /api/ventas/subir-transbank?fechaLimite=` | Form: TB file | 200. Bundle matching (Pass 2: combo ≤5 ventas suma exacta). Phantom: TB-SIN-VENTA |
| `GET /api/ventas/dashboard-stats?maquinaId=0` | Query: maquinaId | 200 `DashboardStats` (60s cache) |
| `GET /api/ventas/stock-critico?maquinaId=0` | Query: maquinaId | 200 `List<StockCriticoDto>` |
| `GET /api/ventas/reporte-rango?inicio=&fin=&maquinaId=0&templateId=` | Query: inicio, fin, maquinaId, includePhantom, templateId | 200 `ReporteDto` |
| `GET /api/ventas/informe-financiero?inicio=&fin=&maquinaId=0` | Query: inicio, fin, maquinaId | 200 `InformeFinancieroDto` |
| `GET /api/ventas/analisis-productos?inicio=&fin=&maquinaId=0` | Query: inicio, fin, maquinaId, includePendientes | 200 `List<AnalisisProductoDto>` (ABC + Estrella/Vaca/Incógnita/Cacho) |
| `GET /api/ventas/stockout-analysis?inicio=&fin=&maquinaId=0&umbralHoras=24` | Query: inicio, fin, maquinaId, umbralHoras | 200 `List<StockoutAnalysisDto>` (dead slots, FillPct, DiasHastaStockout) |
| `GET /api/ventas/ventas-diarias?productoId=&maquinaId=&inicio=&fin=` | Query: all required | 200 `List<VentaDiariaDto>` |
| `GET /api/ventas/categoria-analisis?inicio=&fin=&maquinaId=0` | Query: inicio, fin, maquinaId | 200 `List<CategoriaAnalisisDto>` |
| `GET /api/ventas/purchase-suggestion?days=30&maquinaId=0` | Query: days, maquinaId | 200 `PurchaseSuggestionDto[]` (5-min cache). Sugerido = VentasPeriodo - (StockMaquinas + StockBodega) |
| `DELETE /api/ventas/borrar-rango?inicio=&fin=&maquinaId=` | Query: inicio, fin, maquinaId | 200 "Ventas eliminadas y stock restaurado" |

### ProductosController + MaquinasController + otros

Consultar el archivo completo de contracts generado por la Sesión 4 para los endpoints de: Productos (CRUD + EAN), Maquinas (CRUD + slots + batch-actions), Transferencia, Rendicion, Informes, Inventario, Users.

### DTOs Clave

| DTO | Campos principales |
|-----|-------------------|
| `LoginDto` | Username (`[Required]`), Password (`[Required]`) |
| `CreateUserDto` | Username (`[Required][MaxLength(50)]`), Password (`[Required][MinLength(4)]`), Role (Admin/User) |
| `RegistrarCompraRequestDto` | Proveedor, TipoFactura (MERCADERIA/GASTO_GENERAL), PagadaCaja, SubcategoriaGasto, Detalles (min 1) |
| `CajaResumenDto` | SaldoAnterior, IngresosVentas, GastosOperativos, SaldoFinal, EBITDA (UtilidadOperacional), IsLocked |
| `CreateTemplateRecargaDto` | Nombre (req), Periodos (min 1, con MaquinaId+FechaRecarga) |
| `StockoutAnalysisDto` | NumeroSlot, ProductoId, DineroPerdido, HorasSinStock, VelocidadDiaria, DiasHastaStockout, EsDeadSlot |

---

## 22. Data Flows — End-to-End

### Flow 1: Venta (Scraper → Dashboard)

```
AutomatedReportService (BackgroundService, 23:00)
  → Calcula nextRun = today 23:00
  → Para cada máquina:
    SyncOrchestratorService.SincronizarDesdePortal(maquinaId)
      → ScraperClient → HTTP POST scraper:8000/download
        → Playwright login OurVend → descarga Excel
      → SalesImportService.ImportarVentasMaquina(stream)
        → Parsea Excel (Machine ID, Slot, Price, Order Number, Server Time)
        → Date fix: si fecha.Year < 2024 → usa ServerTime column
        → Timezone: machine 2410280012 → +1h, resto → -11h UTC
        → OrderNumber fix: "E+" → format F0
        → Duplicate check (2 strategies):
          A) OrderNumber ±24h window
          B) (Maquina, FechaLocal, Slot, Precio) exact match
        → Stock decrement: ConfiguracionSlot.StockActual--
        → Crea entidad Venta (FechaHora UTC + FechaLocal Chile)
        → Batch SaveChanges

  Home.razu (Dashboard)
    → GET /api/ventas/dashboard-stats?maquinaId=X
      → SalesAnalyticsService.GetDashboardStatsAsync (60s cache)
        → 3 queries: Hoy, Semana (lunes→hoy), Mes
        → Stock crítico: ConfiguracionSlots WHERE StockActual <= StockMinimo
        → Excluye TB-EXTRA y TB-SIN-VENTA
      → Render: 3 KPI cards + chart + stock crítico badge
```

### Flow 2: Compra con OCR (Foto → Stock Update)

```
NuevaCompra.razu → upload foto factura
  → POST /api/compras/upload-factura (multipart)
    → FacturaOcrService.ExtractInvoiceDataAsync
      → HTTP POST scraper:8000/api/ocr/invoice
        → Gemini API → JSON { items, proveedor, numeroDocumento, fecha }
      → Valida EAN (8-13 dígitos, solo números)
      → ProductMatchingService.MatchAsync per item:
        → Stage 0: EAN lookup → ProductoEAN exact match (confidence 1.0)
        → Stage 0b: SKU + Proveedor lookup
        → Stage 1: Barcode exact match on Producto.CodigoBarras
        → Stage 2: Tokenized fuzzy (Levenshtein ≤2/3, score ≥0.6)
      → Pack division: si ProductoEAN.PackSize > 1 → cantidad*=PackSize, costoUnitario/=PackSize
      → Return OcrInvoiceResultDto → UI muestra resultados con Confirmar

  → POST /api/compras (RegistrarCompraRequestDto)
    → CompraService.RegistrarCompraAsync
      → DesglosarPacks: cantidad*=PackSize, costoUnitario = subtotal/cantidad
      → CPP: nuevoPromedio = (stockActual*costo + nuevaCant*costoUnit) / (stockActual+nuevaCant)
        Ej: 100u@$500 + 20u@$600 → (50000+12000)/120 = $516.67
      → Cierra ProductoCosto abiertos (FechaHasta = ahora)
      → Abre nuevo ProductoCosto (FechaDesde = ahora, Costo = costoUnitario)
      → MovimientoCaja GASTO (-MontoTotal) si PagadaCaja
      → EAN learning: SaveMappingAsync(ean, productoId)
      → SKU learning: SaveSkuMappingAsync(sku, proveedor, productoId)
```

### Flow 3: TemplateRecarga Lifecycle (Creación → Stockout)

```
TemplatesRecarga.razu → Crear template
  → POST /api/templaterecarga { Nombre, Periodos[] }
    → ValidateFechaRecargaChain: no duplicados por (MaquinaId, FechaRecarga)
    → Crea Template (Estado=Pendiente) + Periodos + SnapshotSlots

→ Agregar slots vía editor visual (slot-batch)
→ Foto OCR de recarga: POST /api/ordencarga/from-photo
  → RecargaOcrService.ExtractRecargaDataAsync
    → HTTP POST scraper:8000/api/ocr/recarga → Gemini
    → Matching: offset (extension machines: 101→1) → fuzzy (Levenshtein ≤2)
    → Quantity cap: max 100 (OCR error prevention)

→ "Terminar" template
  → TemplateRecargaLifecycleService.TerminarAsync
    → Estado = Terminado (2)
    → SyncSlotsToConfiguracion: upsert ConfiguracionSlots desde SnapshotSlots

→ "Analizar Stockout"
  → GET /api/templaterecarga/{id}/analyze
    → BuildCrossTemplateChain: busca TODOS los templates de la máquina
      → FechaFin = FechaRecarga del siguiente período (o now+90d si es el último)
    → AnalizarMaquinaEnPeriodo:
      → Ventas entre FechaRecarga y FechaFin
      → Quiebre: cantidadVendida >= cantidadInicial → fechaAgotamiento
      → Velocidad = cantidadVendida / horasActivas
      → DineroPerdido = velocidad * horasSinStock * precioPromedio
      → Dead slot: slot configurado SIN ventas en el período
      → FillPct = StockActual / StockInicial * 100
      → DiasHastaStockout = StockActual / VelocidadDiaria
```

---

## 23. Testing Landscape

### Framework
- **Framework**: xUnit (primario), NUnit (viewport tests)
- **Mocking**: Moq
- **Assertions**: FluentAssertions
- **Runner**: `dotnet test`
- **Coverage**: coverlet.collector v10.0.0
- **DB**: InMemory (no SQL Server real en tests)

### Estructura
```
src/VendingManager.Tests/         (29 archivos, ~300+ test methods)
├── Controllers/    2  (TemplateRecargaController — Lifecycle, Foto)
├── Services/      15  (Caja, Compra, FacturaOcr, ProductMatching, RecargaOcr,
│                       SalesAnalytics (×2), TemplateRecarga (×3),
│                       TemplateRecargaAnalytics, TemplateRecargaLifecycle,
│                       Purchasing (×2), StringSimilarity)
├── Components/     1  (SlotHelpers)
├── Entities/       1  (TemplateRecargaEntity)
├── Enums/          1  (EstadoTemplate)
├── Middleware/     1  (GlobalProblemDetailsMiddleware — 400/401/403/404/409/500)
├── Interceptors/   1  (AuditSaveChangesInterceptor — Added/Modified/Deleted)
├── Interfaces/     2  (ITemplateRecargaLifecycle, ITemplateRecargaAnalytics)
├── DTOs/           1  (OcrInvoiceResultDto)
└── TestData/       1  (TestDataHelpers — factories: Venta, Producto, Maquina, Slot, InMemoryContext)

src/VendingManager.Tests.Viewport/ (3 archivos, todos [Explicit])
└── Pages/          3  (Home, TemplatesRecarga, ViewportTestBase — requieren app corriendo + Playwright)
```

### Módulos con tests (✅) vs sin tests (❌)

| Módulo | Estado | Notas |
|--------|--------|-------|
| Controllers | ❌ 13/15 sin tests | Solo TemplateRecargaController tiene tests |
| Services | ✅ 15 con tests, ❌ 16 sin tests | ProductMatching, FacturaOcr, TemplateRecarga bien cubiertos. SalesAnalytics ABC/stockout sin test. VentasService, MaquinaService, OrdenCargaService sin test |
| Middleware | ✅ | GlobalProblemDetailsMiddleware test completo |
| Interceptors | ✅ | AuditSaveChangesInterceptor test completo |

### Gaps críticos

| Gap | Riesgo |
|-----|--------|
| **SalesAnalyticsService** — ABC classification, stockout analysis, category analysis | HIGH — lógica core de negocio sin validación |
| **SalesImportService** — Bundle matching, phantom TB-SIN-VENTA | HIGH — algoritmo recursivo complejo sin tests |
| **AutomatedReportService** — Background worker schedule | MEDIUM — sin tests de ejecución o reintento |
| **RecargaOcrService** — Fuzzy slot matching real | MEDIUM — solo StringSimilarity utility testado |
| **CajaService.GetResumenAsync** — 3/4 tests skipped | MEDIUM — bug de tracking conflicts preexistente |
| **CompraService** — CPP y ProductoCosto lifecycle | MEDIUM — solo 1 test (reproducción de error) |

### Patrones
- **DB**: `UseInMemoryDatabase()` con nombre GUID único por test → sin leaks de estado
- **HTTP**: `MockHttpMessageHandler` para simular scraper service
- **Background workers**: sin tests
- **Blazor**: lógica extraída a helpers estáticos (`SlotHelpers`); viewport via Playwright (`[Explicit]`)

---

## 24. Security Audit — Gaps Detectados

### Controladores sin `[Authorize]` (acceso público)

| Controlador | Riesgo |
|------------|--------|
| **ProductosController** | CRUD completo de productos público |
| **MaquinasController** | Gestión de máquinas pública |
| **TemplateRecargaController** | Templates públicos |
| **OrdenCargaController** | Órdenes de carga públicas |
| **InformesController** | Subida/descarga de archivos pública |

### Otras vulnerabilidades

| Gap | Detalle |
|-----|---------|
| **Rate limiting solo en login** | Creación de usuarios, productos, etc. sin protección anti fuerza bruta |
| **Password sin mínimo** | UsuariosController permite passwords sin longitud mínima (solo LoginDto tiene `[Required]`) |
| **BCrypt work factor** | Default (11), no configurable. Sin opción de hardening futuro |
| **CORS AllowAnyMethod + AllowAnyHeader + AllowCredentials** | 6 orígenes confiables, pero combinación permisiva |
| **Sin CSRF específico por endpoint** | `UseAntiforgery()` global pero no verificado endpoint por endpoint |
| **Auth state persistido en cliente** | `PersistingAuthenticationStateProvider` serializa claims a JSON en WASM — accesible por JS |

### Cookies

| Propiedad | Valor |
|-----------|-------|
| Name | `auth_token` |
| SameSite | Strict |
| Secure | Always (prod), SameAsRequest (dev) |
| HttpOnly | true (default) |
| Expiration | Sesión (sin expiración explícita) |
| IsPersistent | true |

### Lo que SÍ está bien

- BCrypt para passwords (no texto plano)
- Timing-attack mitigation en login (100-300ms delay aleatorio)
- GlobalProblemDetailsMiddleware suprime detalles en producción
- Health checks separados (liveness vs readiness)
- Roles Admin/User con políticas de autorización
- Auditoría automática vía interceptor (trazabilidad total)
