# Guía de Desarrollo — VendingManager

## Requisitos

- .NET 10 SDK
- Docker y Docker Compose
- Python 3.10+ (solo si se desarrolla el scraper sin Docker)

## Opciones de Entorno

### Opción A: Full Docker (recomendada)

Levanta todos los servicios en contenedores aislados:

```bash
docker-compose -f docker-compose.dev.yml up -d --build
```

| Recurso | URL |
|---------|-----|
| Aplicación Blazor | http://localhost:8081 |
| API Scalar | http://localhost:8081/scalar/v1 |
| SQL Server | localhost:1434 |
| Scraper | localhost:8001 |

### Opción B: Hot Reload

Adecuada cuando se necesita recarga automática en Visual Studio o `dotnet watch`:

```bash
# 1. Levantar solo dependencias
docker-compose -f docker-compose.dev.yml up -d db-dev scraper-dev

# 2. Ejecutar la aplicación con hot reload
dotnet run --project src/VendingManager/VendingManager.csproj
```

Cuando se usa esta opción, configurar la cadena de conexión en `appsettings.Development.json` para que apunte a `localhost,1434` en lugar de `db-dev`.

## Estructura del Proyecto

```
src/
├── VendingManager/                # Backend: host ASP.NET Core 10 + API REST
│   ├── Components/                # Shell Blazor del servidor (App.razor, <head>, viewport)
│   ├── Controllers/               # Controladores API (19 controladores)
│   ├── Core/                      # Capa de dominio
│   │   ├── Configuration/         # Configuración tipada (IOptions)
│   │   ├── Domain/                # Enumeraciones y value objects
│   │   ├── Entities/              # Entidades de negocio + históricas (History/)
│   │   ├── Interfaces/            # Contratos (servicios, repositorios)
│   │   └── Utils/                 # Utilidades de dominio
│   ├── Infrastructure/            # Capa de infraestructura
│   │   ├── Clients/               # Clientes HTTP externos
│   │   ├── Data/                  # DbContext y configuraciones EF Core
│   │   ├── Interceptors/          # Interceptores de EF Core (auditoría)
│   │   └── Services/              # Implementaciones de servicios e integraciones
│   ├── Migrations/                # Migraciones de Entity Framework Core
│   └── Web/                       # Vistas del host del servidor
├── VendingManager.Web/            # Frontend: Blazor WebAssembly (SPA)
│   ├── Pages/                     # Las 27 pantallas de la aplicación
│   ├── Layout/                    # Layouts y shell móvil
│   └── Services/                  # Clientes HTTP hacia la API
├── VendingManager.Shared/         # DTOs y contratos compartidos backend ↔ frontend
├── VendingManager.Tests/          # Tests unitarios y de integración (xUnit, 1150+)
├── VendingManager.Tests.Viewport/ # Tests visuales de viewport (Playwright)
├── VendingManager.slnx            # Archivo de solución
└── Dockerfile                     # Imagen de la aplicación principal

services/
└── scraper/                       # Microservicio de scraping (Python + FastAPI + Playwright)
```

### Arquitectura

El proyecto sigue Clean Architecture con tres capas principales:

- **Core**: define entidades, interfaces y lógica de dominio. No depende de ningún proyecto externo.
- **Infrastructure**: implementa las interfaces definidas en Core. Contiene acceso a datos, servicios externos y clientes HTTP.
- **Web (Controllers + Components)**: expone la API REST y la interfaz Blazor. Orquesta las dependencias.

El flujo de dependencias es unidireccional: `Web → Infrastructure → Core`.

## Convenciones de Código

El proyecto utiliza `.editorconfig` para mantener la consistencia:

| Regla | Valor |
|-------|-------|
| Idioma del código | Inglés (nombres de clases, métodos, variables, comentarios) |
| Documentación de negocio | Español |
| Indentación | 4 espacios (sin tabs) |
| Fin de línea | LF |
| Codificación | UTF-8 |
| Interfaces | Prefijo `I` (ej. `IProductoService`) |
| Modificadores de acceso | Explícitos siempre |

### Estilo

- **Commits convencionales**: `feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`
- **Controladores delgados**: la lógica de negocio reside en servicios dentro de `Infrastructure/Services`
- **Interfaces en Core, implementaciones en Infrastructure**: cada servicio define su contrato en `Core/Interfaces` y su implementación concreta en `Infrastructure/Services`
- **Una responsabilidad por servicio**: cada clase de servicio aborda una única preocupación del dominio
- **Nullabilidad explícita**: se prefieren operadores de coalescencia nula (`??`) y propagación nula (`?.`)

## Testing

```bash
dotnet test src/VendingManager.Tests/VendingManager.Tests.csproj
```

### Stack de testing

| Herramienta | Propósito |
|-------------|-----------|
| xUnit | Framework de pruebas |
| Moq | Mocking de dependencias |
| FluentAssertions | Aserciones legibles |

El proyecto cuenta con más de 900 pruebas entre unitarias y de integración.

### Entorno aislado para testing manual

Para probar cambios sin afectar los entornos de desarrollo o producción:

```bash
docker-compose -f docker-compose.test.yml up -d --build
```

La aplicación estará disponible en `http://localhost:8082`.

## Variables de Entorno

Todas las variables de entorno se gestionan mediante un archivo `.env` en la raíz del proyecto, utilizado exclusivamente por Docker Compose. Este archivo no está versionado: hay que crearlo a mano siguiendo el bloque del [Quick Start del README](../README.md#-quick-start). Para desarrollo local con `dotnet run`, se recomienda usar `dotnet user-secrets`.

| Variable | Propósito |
|----------|-----------|
| `MSSQL_SA_PASSWORD` | Contraseña del usuario `sa` de SQL Server |
| `OURVEND_USER` | Usuario del portal externo OurVend |
| `OURVEND_PASS` | Contraseña del portal OurVend |
| `GEMINI_API_KEY` | Clave de API de Google Gemini |

## API de Desarrollo

En entorno de desarrollo, la aplicación expone:

- **OpenAPI**: documento de especificación en `/openapi/v1.json`
- **Scalar**: interfaz interactiva para explorar la API en `/scalar/v1`
