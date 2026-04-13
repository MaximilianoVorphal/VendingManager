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
        Container(scraper, "Python Scraper Service", "FastAPI + Playwright, Python", "Extrae automáticamente reportes de la web de OurVend.")
    }

    Rel(user, spa, "Visualiza y edita", "HTTPS")
    Rel(spa, api, "Llama métodos API", "JSON/HTTPS")
    Rel(api, db, "Lee y escribe hacia", "EF Core / SQL")
    Rel(api, scraper, "Solicita disparar scraping", "HTTP")
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
3. **Repository Pattern / Service Pattern:** Los controladores no hablan con EF Core de forma directa, lo hacen a través de interfaces (`ISalesImportService`, `ISyncOrchestratorService`, etc).

## Diagrama de Contenedores API (C4)

```mermaid
C4Container
    title Componentes Internos de la API Backend (VendingManager.API)

    Person(spa_user, "Frontend App", "Blazor")

    System_Boundary(api_boundary, "Web API Boundary") {
        Container(controllers, "Web API Controllers", "C# REST", "Reciben peticiones y validan seguridad (ej: VentasController, CargasController).")
        
        Boundary(services_boundary, "Integration Services Layer") {
            Component(sync, "Sync Orchestrator", "Service", "Llama al microservicio Python.")
            Component(sales, "Sales Import", "Service", "Conciliación Transbank vs Ourvend.")
            Component(catalog, "Catalog Excel", "Service", "Importa/Exporta productos masivamente.")
            Component(orden, "Orden Carga Excel", "Service", "Genera el XLS con la ruta del camión.")
        }
        
        ComponentDb(efcore, "Entity Framework Core", "ORM", "Contexto UnitOfWork para acceso SQL.")
    }
    
    System_Ext(scraper, "Scraper Python", "FastAPI")

    Rel(spa_user, controllers, "Llama endopoints", "JSON")
    Rel(controllers, sync, "Solicita descargas", "Interface")
    Rel(controllers, sales, "Envía Excel importado", "Interface")
    Rel(controllers, orden, "Genera reporte", "Interface")
    
    Rel(sync, scraper, "Petición asíncrona de extracción", "HTTP")
    Rel(sync, sales, "Redirige stream para procesar", "In-Process")
    
    Rel(sales, efcore, "Actualiza base de datos", "LINQ")
    Rel(catalog, efcore, "Actualiza productos", "LINQ")

    UpdateElementStyle(controllers, $fontColor="white", $bgColor="#2B78E4", $borderColor="#1C5FB8")
    UpdateElementStyle(efcore, $fontColor="white", $bgColor="#D32F2F", $borderColor="#B71C1C")
    UpdateElementStyle(scraper, $fontColor="black", $bgColor="#FBC02D", $borderColor="#F57F17")
```
