# Architecture Overview

Esta aplicación es un híbrido entre un Backend ASP.NET Core que provee Web APIs y renderizado inicial (Blazor Server), y un Frontend dinámico mediante Blazor WebAssembly. Adicionalmente interactúa con un microservicio Scraper escrito en Python (FastAPI + Playwright).

## Diagrama de Contexto (C4)

El siguiente diagrama muestra el esquema general de cómo interactúan los componentes dentro del entorno Docker:

```mermaid
C4Context
    title Diagrama de Contexto del Sistema - VendingManager

    Person(user, "Administrador", "Gestiona inventario, ventas y cierres de caja.")
    
    System_Boundary(c1, "VendingManager System") {
        Container(spa, "Blazor WebAssembly UI", "Blazor WASM, C#", "Provee todas las pantallas dinámicas para el usuario (Dashboard, Cargas, Cajas).")
        Container(api, "VendingManager API", "ASP.NET Core Web API, C#", "Provee la lógica de negocio, reportes y acceso a datos a través de EF Core.")
        ContainerDb(db, "SQL Server Database", "Microsoft SQL Server", "Persiste toda la información de ventas, inventario y cajas.")
        Container(scraper, "Python Scraper & AI Service", "FastAPI + Playwright/Gemini", "Extracción de reportes web y procesamiento OCR con IA.")
    }

    System_Ext(gemini, "Google Gemini API", "LLM Vision Service")

    Rel(user, spa, "Visualiza y edita", "HTTPS")
    Rel(spa, api, "Llama métodos API", "JSON/HTTPS")
    Rel(api, db, "Lee y escribe hacia", "EF Core / SQL")
    Rel(api, scraper, "Petición HTTP (Scraping/OCR)", "HTTP")
    Rel(scraper, gemini, "Analiza facturas", "HTTPS")
    Rel(scraper, api, "Notifica éxito y provee datos")

    UpdateElementStyle(user, $fontColor="white", $bgColor="#08427b", $borderColor="#073b6f")
    UpdateElementStyle(spa, $fontColor="white", $bgColor="#438dd5", $borderColor="#3c7ebf")
    UpdateElementStyle(api, $fontColor="white", $bgColor="#438dd5", $borderColor="#3c7ebf")
    UpdateElementStyle(db, $fontColor="white", $bgColor="#e63946", $borderColor="#d62828")
    UpdateElementStyle(scraper, $fontColor="black", $bgColor="#f4a261", $borderColor="#e76f51")
```

## Patrones de Diseño Usados

1. **Clean Architecture:** El backend `.NET` divide su lógica en `Core` (Interfaces/Entidades), `Infrastructure` (Servicios, Data) y la Capa de Presentación (Controladores).
2. **Code-Behind:** En el cliente Blazor, las vistas grandes separan el archivo `.razor` del `.razor.cs`.
3. **Repository Pattern / Service Pattern:** Los controladores no hablan con EF Core de forma directa, lo hacen a través de interfaces especializadas.

> [!NOTE]
> **Para un análisis completo de los Endpoints y Módulos**, consulta nuestra **[Referencia de API y Funcionalidades](api_reference.md)**.
> **Para revisar el historial de desiciones de microservicios**, visita los archivos ADR dentro de `/decisions/`.

## Diagrama de Contenedores API (C4)

```mermaid
C4Container
    title Componentes Internos de la API Backend (VendingManager.API)

    Person(spa_user, "Frontend App", "Blazor")

    System_Boundary(api_boundary, "Web API Boundary") {
        Container(controllers, "Web API Controllers", "C# REST", "Reciben peticiones y validan seguridad (ej: VentasController, CargasController).")
        
        Boundary(services_boundary, "Integration Services Layer") {
            Component(sync, "Sync Orchestrator", "Service", "Llama al microservicio Python para reportes.")
            Component(ocr, "Factura OCR", "Service", "Llama microservicio Python para extraer texto de imágenes.")
            Component(sales, "Sales Import", "Service", "Conciliación Transbank vs Ourvend.")
            Component(catalog, "Catalog Excel", "Service", "Importa/Exporta productos masivamente.")
            Component(orden, "Orden Carga Excel", "Service", "Genera XLS de ruta de camión.")
            Component(analytics, "Sales Analytics", "Service", "Métricas pesadas y Quiebre de stock.")
            Component(purchasing, "Purchasing", "Service", "Abastecimiento de 30 días.")
            Component(compras, "Compra Service", "Service", "Facturas híbridas (Mercadería/Gastos) y control de stock.")
            Component(gastos, "Gastos Recurrentes", "Service", "Gestión de gastos fijos mensuales y alertas de pendientes.")
            Component(ordencarga, "Orden Carga", "Service", "Logística de reposición: descuenta bodega, actualiza slots, registra costo en Caja.")
        }
        
        ComponentDb(efcore, "Entity Framework Core", "ORM", "Contexto UnitOfWork para acceso SQL.")
    }
    
    System_Ext(scraper, "Scraper & AI Python", "FastAPI")

    Rel(spa_user, controllers, "Llama endopoints", "JSON")
    Rel(controllers, sync, "Solicita descargas", "Interface")
    Rel(controllers, ocr, "Solicita lectura factura", "Interface")
    Rel(controllers, sales, "Envía Excel importado", "Interface")
    Rel(controllers, analytics, "Lee Dashboard Stats", "Interface")
    Rel(controllers, purchasing, "Trae pronóstico compras", "Interface")
    Rel(controllers, gastos, "CRUD + Pendientes mes", "Interface")
    Rel(controllers, ordencarga, "CRUD Órdenes + Finalizar", "Interface")
    
    Rel(sync, scraper, "Petición asíncrona de extracción", "HTTP")
    Rel(ocr, scraper, "Petición multipart imagen OCR", "HTTP")
    Rel(sync, sales, "Redirige stream para procesar", "In-Process")
    
    Rel(sales, efcore, "Actualiza base de datos", "LINQ")
    Rel(catalog, efcore, "Actualiza productos", "LINQ")
    Rel(analytics, efcore, "Consultas polimórficas", "LINQ")
    Rel(gastos, efcore, "Cruza GastosRecurrentes vs MovimientosCaja", "LINQ")
    Rel(ordencarga, efcore, "Bodega + Slots + MovimientosCaja", "LINQ")

    UpdateElementStyle(controllers, $fontColor="white", $bgColor="#2B78E4", $borderColor="#1C5FB8")
    UpdateElementStyle(efcore, $fontColor="white", $bgColor="#D32F2F", $borderColor="#B71C1C")
    UpdateElementStyle(scraper, $fontColor="black", $bgColor="#FBC02D", $borderColor="#F57F17")
```
