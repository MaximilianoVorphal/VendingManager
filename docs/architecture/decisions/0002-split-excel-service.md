# ADR 0002: Modularización de ExcelService (ISP & SRP)

## Fecha
2026-04-05

## Estado
**Aceptado**

## Contexto
El archivo `ExcelService.cs` había crecido hasta convertirse en un "God Object" de aproximadamente 950 líneas de código. Era responsable de:
1. Coordinar al Scraper en Python para descarga de ventas.
2. Leer excels chinos de ventas y conciliar duplicados.
3. Leer excels de Transbank y cruzarlos mediante un algoritmo heurístico y combinatorio.
4. Generar reportes en Excel de rutas de reabastecimiento (Lista de Carga).
5. Leer plantillas para importar en masa catálogos de productos.

Tener todas estas operaciones en una única interfaz (`IExcelService`) violaba tanto el Principio de Responsabilidad Única (SRP) como el de Segregación de Interfaces (ISP). Los controladores que solo querían descargar la lista de reposición para el camión, por ejemplo, estaban recibiendo dependencias para comunicarse con Python debido al acoplamiento.

## Decisión
Se desmanteló `IExcelService` y su implementación. La lógica fue dividida de forma quirúrgica en cuatro servicios cohesivos bajo la carpeta `Infrastructure/Services` usando interfaces en `Core/Interfaces`:

1.  **`ISyncOrchestratorService`**: Dedicado exclusivamente a orquestar el llamado HTTP al `ScraperClient` en Python y enrutar su Stream al importador de ventas.
2.  **`ISalesImportService`**: Contiene la lógica profunda de Entity Framework, heurística de duplicados y conciliaciones "Carrito/Bundle" para los abonos de Transbank vs Ourvend.
3.  **`ICatalogExcelService`**: Controla el alta y modificación masiva de entidades `Producto`.
4.  **`IOrdenCargaExcelService`**: Motor de generación (mediante ClosedXML) de la lista de reposición/Stock Crítico.

## Consecuencias
**Positivas:**
- Cada clase es menor a 400 líneas.
- Alta portabilidad: `SyncOrchestratorService` puede cambiar la tecnología del scraper sin afectar el parser de Transbank.
- Entendimiento facilitado: Un nuevo desarrollador leyendo `Cargas.razor` ahora sabe que depende estrictamente de `IOrdenCargaExcelService` y no de un ente que engloba un sinfín de requerimientos de negocio disparatados.
- Menor tiempo de compilación para test unitarios aislados.

**Negativas:**
- Cuatro nuevas clases que sumar al contenedor de DI (`Program.cs`). El constructor de ciertos controladores debió ajustarse para inyectar su respectiva interfaz específica en lugar del objeto global.
