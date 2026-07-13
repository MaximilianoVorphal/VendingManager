# Arquitectura — VendingManager

## Visión General

VendingManager es un sistema **polyglot distribuido** para la gestión integral de máquinas vending. Combina un backend en **.NET 10** con **Blazor WebAssembly** interactivo como frontend y un microservicio en **Python/FastAPI** para scraping y OCR. La aplicación sigue los principios de **Clean Architecture** con un **CQRS implícito** (separación entre comandos de escritura y consultas de lectura dentro del mismo modelo) y emplea **SQL Server** como base de datos relacional con soporte completo de **Entity Framework Core**.

El sistema se despliega vía **Docker Compose** con tres contenedores: la aplicación .NET, SQL Server 2022, y el microservicio scraper.

```
┌─────────────────────────────────────────────────────────────────┐
│                    VendingManager (Polyglot Distributed)          │
│                                                                   │
│  ┌─────────────────────────────┐    ┌──────────────────────────┐  │
│  │   .NET 10 App (Blazor WASM   │    │   Python Scraper         │  │
│  │   + REST API)                │◄──►│   (FastAPI + Playwright) │  │
│  │                              │    │                          │  │
│  │  ┌──────────┐ ┌──────────┐  │    │  ┌────────────────────┐  │  │
│  │  │ Blazor   │ │ REST API │  │    │  │ Report Download    │  │  │
│  │  │ WASM     │ │ 19 Ctrl  │  │    │  │ Machine Status     │  │  │
│  │  │ (inter-  │ │          │  │    │  │ Sales Report (JSON)│  │  │
│  │  │ active)  │ │          │  │    │  │ Gemini OCR         │  │  │
│  │  └──────────┘ └────┬─────┘  │    │  └────────────────────┘  │  │
│  │                     │        │    └──────────────────────────┘  │
│  │  ┌──────────────────▼──────┐ │              │                  │
│  │  │    Business Layer        │ │              │ (HTTP)           │
│  │  │    ~35 Services          │ │              │                  │
│  │  └──────────────────┬──────┘ │              │                  │
│  │                     │        │              │                  │
│  │  ┌──────────────────▼──────┐ │              │                  │
│  │  │    EF Core / Dapper      │ │              │                  │
│  │  └──────────────────┬──────┘ │              │                  │
│  └─────────────────────┼────────┘              │                  │
│                        │                       │                  │
│              ┌─────────▼───────────┐            │                  │
│              │   SQL Server 2022   │            │                  │
│              │   (VendingDB)       │            │                  │
│              └─────────────────────┘            │                  │
│                        ▲                       │                  │
│                        │ (internal network)    │                  │
│              ┌─────────┴───────────┐            │                  │
│              │   Ourvend Portal    │◄───────────┘                  │
│              │   (external API)    │   (scraping)                  │
│              └─────────────────────┘                               │
└─────────────────────────────────────────────────────────────────┘
```

## Diagrama C4 Nivel 1 — Contexto del Sistema

```
Persona                  ┌─────────────────────────────┐
(Operador/Administrador) │       Ourvend Portal        │
       │                 │  (Sistema externo de         │
       │                 │   telemetría vending)        │
       ▼                 └──────────┬──────────────────┘
┌─────────────────┐                 │ scraping
│   VendingManager │◄────────────────┘
│   (Sistema)      │
└──────┬───────────┘
       │ gestión
       ▼
┌─────────────────┐
│ Transbank        │
│ (procesador de   │
│  pagos)          │
└──────────────────┘
```

## Stack Tecnológico

| Componente | Tecnología | Versión |
|-----------|-----------|---------|
| Backend | .NET (ASP.NET Core) | net10.0 |
| Frontend | Blazor WebAssembly (interactivo + Server prerendering) | net10.0 |
| API Documentation | Scalar + OpenAPI | 2.11.5 / 2.7.5 |
| Base de Datos | SQL Server (mcr.microsoft.com/mssql/server) | 2022-latest |
| ORM | Entity Framework Core | 10.0.1 |
| Scraper | Python + FastAPI + Playwright + Playwright-Stealth | mcr.microsoft.com/playwright/python:v1.40.0-jammy |
| OCR | Google Gemini (gemini-3-flash-preview) | — |
| Autenticación | Cookies con BCrypt | BCrypt.Net-Next 4.0.3 |
| Rate Limiting | ASP.NET Core Rate Limiting (Fixed Window) | — |
| Logging | Serilog (JSON estructurado + Console) | 8.0.2 |
| Health Checks | ASP.NET Core + SQL Server probe | 10.0.1 / 8.0.2 |
| Excel | ClosedXML + ExcelDataReader + MiniExcel | 0.105.0 / 3.8.0 / 1.35.0 |
| Testing | xUnit (38 test de servicio + controllers + interceptors + domain) | — |
| Contenerización | Docker Compose (Dockerfile multi-stage) | — |

## Capas de la Arquitectura

```
┌─────────────────────────────────────────────────────────┐
│                    Presentation                           │
│  ┌──────────────────┐  ┌──────────────────────────────┐  │
│  │  Blazor WASM      │  │  REST Controllers (19)       │  │
│  │  (35+ pages)      │  │  + Middleware + ModelBinders │  │
│  └────────┬─────────┘  └──────────────┬───────────────┘  │
└───────────┼───────────────────────────┼──────────────────┘
            │                           │
┌───────────▼───────────────────────────▼──────────────────┐
│                  Application / Business Layer               │
│  ┌────────────────────────────────────────────────────┐   │
│  │  Services (~35) + Repositories (5)                  │   │
│  │  - SyncOrchestratorService                          │   │
│  │  - CajaService / CajaBusinessService                 │   │
│  │  - SalesAnalyticsService / PurchasingService         │   │
│  │  - VentasService / MaquinaService / InventarioService│   │
│  │  - FacturaOcrService / RecargaOcrService             │   │
│  │  - TemplateRecarga*Service (3)                       │   │
│  │  - LogisticaPredictivaService                        │   │
│  │  - CompraService / GastoRecurrenteService            │   │
│  │  - RendicionService / TransferenciaService           │   │
│  │  - ContabilidadService / ExcelExportService          │   │
│  │  - AuditService / IntegrityCheckService              │   │
│  │  - ProductMatchingService / ProveedorMatchingService │   │
│  │  - InformesService / AutomatedReportService (BG)     │   │
│  └────────────────────────────────────────────────────┘   │
└───────────────────────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│                Core (Domain)                                  │
│  ┌────────────┐  ┌──────────────┐  ┌──────────────────┐     │
│  │ Entities    │  │ Interfaces   │  │ Configuration    │     │
│  │ (27 ent.)   │  │ (35 ifaces)  │  │ VendingConfig    │     │
│  │             │  │              │  │ AnalyticsThresholds│    │
│  │ + History   │  │ + IScraper   │  └──────────────────┘     │
│  │   entities  │  │   Client     │  ┌──────────────────┐     │
│  │ (10)        │  │              │  │ Domain Services  │     │
│  └────────────┘  └──────────────┘  │ CalculadoraCostos │     │
│                                     └──────────────────┘     │
└──────────────────────────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│                Infrastructure                                  │
│  ┌────────────────┐  ┌────────────┐  ┌──────────────────┐   │
│  │ EF Core DbContext│ │ Repositories│  │ Clients          │   │
│  │ + Migrations    │  │ (5 repos)  │  │ ScraperClient    │   │
│  │ (118 migrations)│  │            │  │ WafBlockedExcep  │   │
│  └────────────────┘  └────────────┘  └──────────────────┘   │
│  ┌────────────────┐  ┌────────────────────────────────────┐  │
│  │ Interceptors   │  │ Services (implementations)          │  │
│  │ AuditSaveChanges│  │ 35 impl classes from Core.Interfaces│ │
│  └────────────────┘  └────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
```

### Core (Dominio)

El centro de la arquitectura, sin dependencias externas.

- **Entities** (27 clases): `Maquina`, `Producto`, `Venta`, `Compra`, `MovimientoCaja`, `OrdenCarga`, `TemplateRecarga`, `PeriodoRecarga`, `SnapshotSlot`, `ConfiguracionSlot`, `ProductoCosto`, `ProductoEAN`, `ProveedorCatalog`, `ProveedorAlias`, `ZonaLogistica`, `Rendicion`, `Transferencia`, `GastoRecurrente`, `User`, `Auditoria`, `Informe`, `Contabilidad`, `Devolucion`, `SyncMetadata` + entidades history (10 clases para auditoría)
- **Interfaces** (35 contratos): Separación total entre definición e implementación. Todas las operaciones del sistema se definen aquí
- **Configuration** (2 clases): `VendingConfig` (configuración del negocio), `AnalyticsThresholds` (umbrales de análisis)
- **Domain** (1 clase): `CalculadoraCostos` — lógica pura de CPP (Costo Promedio Ponderado) sin efectos secundarios

### Infrastructure

Capa de implementación con acceso a datos, servicios externos y persistencia.

- **Services** (35 implementaciones): Lógica de negocio concreta. Desde operaciones financieras (`CajaBusinessService`) hasta integración con OCR (`FacturaOcrService`, `RecargaOcrService`)
- **Data**: `ApplicationDbContext` con 49 DbSets, repositorios especializados (`VentaRepository`, `MaquinaRepository`, `AccountingPeriodRepository`, `ProductoEANRepository`, `ProveedorAliasRepository`)
- **Clients**: `ScraperClient` — comunicación HTTP con el microservicio Python, con manejo de WAF, timeout de 3 minutos y cache de 6 horas para machine-status
- **Interceptors**: `AuditSaveChangesInterceptor` — interceptor de EF Core que captura automáticamente cambios de estado y escribe en tablas de auditoría + history

### Presentation

Dos caras de la misma moneda: API REST y Blazor WebAssembly.

- **19 Controladores REST**: `VentasController`, `MaquinasController`, `CajaController`, `ComprasController`, `ProductosController`, `OrdenCargaController`, `TemplateRecargaController`, `LogisticaPredictivaController`, `ContabilidadController`, `RendicionController`, `TransferenciaController`, `GastoRecurrenteController`, `InventarioController`, `InformesController`, `ProveedoresController`, `UsersController`, `AccountController`, `ZonasLogisticasController`, `AuditoriaController`
- **Blazor WASM** (35+ páginas): `Home.razor`, `Caja.razor`, `Cargas.razor`, `Compras.razor`, `RecargaMovil.razor`, `TemplatesRecarga.razor`, `Inventario.razor`, `Conciliacion.razor`, `OptimizadorRutas.razor`, `StockoutDashboard.razor`, etc.
- **Componentes reutilizables**: `DataTable`, `SlotCard`, `FotoGuiaPanel`, `PanelDeControlSidebar`, `StatusBadge`, `EmptyState`, `IndustrialAlert`, `LoadingSpinner`, `ConfirmDialog`, `Mobile*` (DatePickerSheet, MachinePhotoSheet, RecargaSaveSheet)
- **Middleware**: `GlobalProblemDetailsMiddleware` (RFC 7807 ProblemDetails), `DateTimeModelBinderProvider`, `RateLimiter`, `SerilogRequestLogging`
- **Autenticación**: Cookie-based con `PersistingAuthenticationStateProvider` (Server) y `CookieAuthenticationStateProvider` (WASM), política de admin por roles

### Microservicio Python

Servicio independiente para scraping y OCR, implementado en Python con FastAPI.

**Endpoints expuestos:**

| Endpoint | Método | Propósito |
|----------|--------|-----------|
| `/download` | POST | Descarga reporte Excel desde Ourvend (Playwright + stealth) |
| `/api/ocr/invoice` | POST | OCR de facturas/boletas vía Gemini |
| `/api/ocr/recarga` | POST | OCR de listas de recarga manuscritas vía Gemini |
| `/api/machines/status` | GET | Estado en tiempo real de máquinas (API sin Playwright) |
| `/api/sales/report` | GET | Reporte de ventas JSON (browser path con Playwright) |
| `/health` | GET | Health check del servicio |

**Arquitectura interna del scraper:**

- `scraper_api.py` — FastAPI app principal, ruteo y orquestación
- `download_ourvend_report_alt.py` — Scraper de reportes Excel con Playwright stealth
- `report_sales_api.py` — Cliente API Ourvend reverse-engineered (RSA login, paginación, detección WAF)
- `machine_status.py` — Scraping de estado de máquinas vía API REST (sin Playwright)
- `gemini_ocr.py` — Integración con Google Gemini 3 Flash Preview para extracción estructurada de datos

El scraper implementa un **circuit breaker** del lado .NET que maneja degradación gradual: desde detección de WAF hasta parada completa con backoff exponencial.

## Patrones de Diseño

| Patrón | Dónde se aplica |
|--------|----------------|
| **Clean Architecture** | Separación en 3 proyectos: Core (dominio) → Infrastructure → Web/Presentation. Las dependencias apuntan hacia adentro (Core no conoce Infrastructure) |
| **Repository Pattern** | `IVentaRepository`, `IMaquinaRepository`, `IAccountingPeriodRepository`, `IProductoEANRepository`, `IProveedorAliasRepository` — abstracción de acceso a datos |
| **CQRS Implícito** | Separación natural: servicios de consulta (`SalesAnalyticsService`, `LogisticaPredictivaService`) vs servicios de comando (`SyncOrchestratorService`, `CajaService`) |
| **Strategy Pattern** | Dos estrategias de scraping: Excel download (ALT) vs API JSON pura, ambas orquestadas por `SyncOrchestratorService` |
| **Circuit Breaker** | `PollingCircuitBreaker` con 5 estados (Closed → Degraded → Open → HalfOpen → Halted) para proteger contra fallos del portal Ourvend |
| **Interceptor Pattern** | `AuditSaveChangesInterceptor` de EF Core captura cambios y escribe automáticamente en tablas de auditoría e history |
| **Background Service** | `AutomatedReportService` como `BackgroundService` .NET para sincronización automática con Ourvend en ventana horaria configurable (8:00-21:00) |
| **Domain Service (Pure Math)** | `CalculadoraCostos` como clase estática sin efectos secundarios ni dependencias — CPP (Costo Promedio Ponderado) puro |
| **Options Pattern** | `IOptions<VendingConfig>` y `IOptions<AnalyticsThresholds>` para configuración tipada |
| **HTTP Client Factory** | `IHttpClientFactory` + `AddHttpClient<T>` para `ScraperClient`, `FacturaOcrService`, `RecargaOcrService` |
| **Memory Cache** | Cache de 6h para machine-status vía `IMemoryCache` + patrón Cache-Aside en `ScraperClient` |
| **Rate Limiting** | Fixed window policy (5 requests/minuto) para login |
| **Auto-Migration** | `context.Database.Migrate()` en startup — las migraciones se aplican automáticamente al iniciar |
| **ProblemDetails (RFC 7807)** | Middleware global que captura excepciones no manejadas y retorna respuestas estandarizadas |

## Diagrama de Componentes (C4 Nivel 2)

```
┌──────────────────────────────────────────────────────────────────────┐
│                        .NET Application                                │
│                                                                        │
│  ┌──────────────┐    ┌────────────────────────────────────────────┐   │
│  │  Blazor WASM  │    │           REST Controllers                  │   │
│  │  (35 pages)    │    │  ┌─────┐ ┌──────┐ ┌──────┐ ┌──────────┐  │   │
│  │               │    │  │Ventas│ │Caja  │ │Compr │ │Maquinas  │  │   │
│  │  Components:  │    │  │Ctrl  │ │Ctrl  │ │asCtrl│ │Ctrl      │  │   │
│  │  DataTable    │    │  └──┬──┘ └──┬───┘ └──┬───┘ └────┬─────┘  │   │
│  │  SlotCard     │    │  ┌──┼───────┼────────┼───────────┼───┐    │   │
│  │  FotoGuiaPanel│    │  │  │       │        │           │   │    │   │
│  │  StatusBadge  │    │  │  ▼       ▼        ▼           ▼   │    │   │
│  │  LoadingSpinner│   │  │      Business Services           │    │   │
│  │  Mobile*      │    │  │  ┌──────────────────────────┐   │    │   │
│  └──────┬───────┘    │  │  │ ~35 Services:             │   │    │   │
│         │            │  │  │ SyncOrchestrator          │   │    │   │
│         │ (HTTP)     │  │  │ Caja / Ventas / Maquinas  │   │    │   │
│         ▼            │  │  │ SalesAnalytics / Purchase │   │    │   │
│  ┌──────────────┐    │  │  │ Compra / Inventario      │   │    │   │
│  │ REST API     │    │  │  │ LogisticaPredictiva      │   │    │   │
│  │ (webapp)     │────┼─►│  │ TemplateRecarga* (3)     │   │    │   │
│  └──────────────┘    │  │  │ FacturaOcr/RecargaOcr   │   │    │   │
│                      │  │  │ Contabilidad/Rendicion   │   │    │   │
│                      │  │  │ Purchasing/Inventario    │   │    │   │
│                      │  │  └──────────┬───────────────┘   │    │   │
│                      │  │             │                    │    │   │
│                      │  │  ┌──────────▼────────────────┐   │    │   │
│                      │  │  │       Repositories         │   │    │   │
│                      │  │  │  VentaRepo / MaquinaRepo   │   │    │   │
│                      │  │  │  AccountingPeriodRepo      │   │    │   │
│                      │  │  │  ProductoEANRepo           │   │    │   │
│                      │  │  │  ProveedorAliasRepo        │   │    │   │
│                      │  │  └──────────┬────────────────┘   │    │   │
│                      │  │             │                    │    │   │
│                      │  │  ┌──────────▼────────────────┐   │    │   │
│                      │  │  │      EF Core DbContext     │   │    │   │
│                      │  │  │  49 DbSets + Migrations   │   │    │   │
│                      │  │  │  AuditSaveChangesInterceptor│   │    │   │
│                      │  │  └──────────┬────────────────┘   │    │   │
│                      │  └─────────────┼────────────────────┘    │   │
│                      └────────────────┼─────────────────────────┘   │
│                                       │                            │
│        ┌──────────────────────────────▼─────────────────────┐      │
│        │                 SQL Server 2022                     │      │
│        │              VendingDB (118 migrations)             │      │
│        └────────────────────────────────────────────────────┘      │
│                                       │                            │
│        ┌──────────────────────────────▼─────────────────────┐      │
│        │           Microservicio Scraper (Python)            │      │
│        │  ┌──────────┐ ┌───────────┐ ┌────────────────┐    │      │
│        │  │ Download  │ │ Machine   │ │ Sales Report   │    │      │
│        │  │ Report    │ │ Status    │ │ (JSON via      │    │      │
│        │  │ (Excel)   │ │ (API)     │ │  browser)      │    │      │
│        │  └──────────┘ └───────────┘ └────────────────┘    │      │
│        │  ┌──────────────────────────────────────────┐     │      │
│        │  │  Gemini OCR (Invoice + Recarga)           │     │      │
│        │  └──────────────────────────────────────────┘     │      │
│        └────────────────────────────────────────────────────┘      │
└──────────────────────────────────────────────────────────────────────┘
```

## Entidades Principales y Relaciones

El dominio gira en torno a estos conceptos clave:

- **Máquinas** (`Maquina`): representan las máquinas vending físicas, con ubicación, zona logística y configuración de slots
- **Productos** (`Producto`): catálogo con SKU, código de barras, precios y costos promediados (CPP)
- **Ventas** (`Venta`): transacciones registradas desde Ourvend, con precio, costo histórico y trazabilidad por máquina
- **Compras** (`Compra`): registro de adquisiciones con factura, proveedor, detalles e imágenes (almacenadas en DB como bytes)
- **Movimientos de Caja** (`MovimientoCaja`): toda la contabilidad de ingresos y egresos
- **Templates de Recarga** (`TemplateRecarga` / `PeriodoRecarga`): ciclo de reposición con slots, fotos guía y OCR
- **Ordenes de Carga** (`OrdenCarga`): órdenes de reposición con detalle de productos y costos
- **Rendiciones/Transferencias**: módulo de conciliación financiera entre cuentas
- **Zonas Logísticas** (`ZonaLogistica`): agrupación geográfica para optimización de rutas
- **Auditoría** (`Auditoria` + tablas History): trazabilidad completa de cambios mediante interceptor EF

## Convenciones y Estilo Arquitectónico

- **Idioma**: Código fuente en inglés, documentación y comentarios en español. UI en español con locale Chile ($, dd/MM/yyyy, HH:mm)
- **Controladores delgados**: Los controladores delegan toda la lógica a servicios; solo manejan routing, validación superficial y respuestas HTTP
- **Auto-migrate**: `context.Database.Migrate()` en startup; las migraciones se aplican automáticamente sin intervención manual
- **Auditoría automática**: `AuditSaveChangesInterceptor` captura cambios en entidades del core y escribe en tablas de auditoría + tablas history con before/after JSON
- **Seed automático**: Si no existen usuarios, el sistema crea un admin por defecto en Development o vía variable de entorno `SEED_ADMIN_PASSWORD`
- **Cultura fija**: Se fuerza cultura chilena (CLP, formato fecha) basada en InvariantCulture para consistencia cross-platform
- **Concurrencia optimista**: `RowVersion` (timestamp) en entidades como `TemplateRecarga` para prevenir conflictos de concurrencia
- **Tratamiento de errores**: `TreatWarningsAsErrors` habilitado en todos los proyectos .NET
- **Testing**: 38+ test files de servicio con xUnit, tests de dominio puro (CalculadoraCostos), interceptors y componentes Blazor

## Configuración y Despliegue

Tres contenedores Docker:

1. **webapp** (`vendingmanager:latest`): .NET 10, puerto 8080, build multi-stage con cache de NuGet
2. **db** (`mssql/server:2022-latest`): SQL Server 2022, volúmenes persistentes para datos
3. **scraper** (`playwright/python:v1.40.0`): Python + Chromium, pip cache en layer Docker

Variables de entorno clave:
- `ConnectionStrings__DefaultConnection` — conexión a SQL Server
- `ScraperServiceUrl` — URL del scraper (`http://scraper:8000`)
- `OURVEND_USER` / `OURVEND_PASS` — credenciales del portal Ourvend
- `GEMINI_API_KEY` — API key de Google Gemini para OCR
- `SEED_ADMIN_PASSWORD` — password para seed automático de admin
