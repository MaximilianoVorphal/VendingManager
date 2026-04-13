# Development Setup

Esta guía detalla los pasos para levantar el entorno completo de `VendingManager` de forma local.

## 1. Con Docker Compose (Recomendado)

El repositorio usa `docker-compose.dev.yml` para el desarrollo iterativo.

### Instrucciones
1. Abre una terminal en la raíz del proyecto.
2. Ejecuta:
   ```bash
   docker-compose -f docker-compose.dev.yml up -d --build
   ```
3. Verifica que los tres contenedores estén funcionando:
   - `db-dev`: SQL Server en el puerto `1434` (mapeado de 1433 localmente).
   - `scraper-dev`: FastAPI/Python en el puerto `8001` (para scraping y descargas).
   - `webapp-dev`: Aplicación Blazor Web en el puerto `8081`.

4. Accede a tu entorno de desarrollo en [http://localhost:8081](http://localhost:8081).

> [!WARNING]  
> Ten en cuenta que si haces cambios en archivos C#, es posible que debas reconstruir el contenedor `webapp`. En entornos productivos, usa el `docker-compose.yml` regular.

## 2. Compilación Manual (Sin usar el contenedor Web)

Si deseas utilizar *Hot Reload* o ejecutar en Visual Studio sin que Docker compile tu C#:

1. Levanta solo las dependencias (Database y Scraper) usando los contenedores de Docker:
   ```bash
   docker-compose -f docker-compose.dev.yml up -d db-dev scraper-dev
   ```
2. Modifica el string de conexión en tu `appsettings.Development.json` (dentro de `src/VendingManager`) para que apunte a `localhost,1434` en lugar de `db-dev`.
3. Ejecuta la solución de manera regular desde Visual Studio o ejecutando:
   ```bash
   dotnet run --project src/VendingManager/VendingManager.csproj
   ```
