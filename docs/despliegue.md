# Guía de Despliegue — VendingManager

## Requisitos del Servidor

- Docker Engine 24+ y Docker Compose v2
- 4 variables de entorno configuradas (ver sección [Variables de Entorno](#variables-de-entorno))

## Despliegue Rápido

```bash
# 1. Clonar el repositorio
git clone https://github.com/MaximilianoVorphal/VendingManager.git
cd VendingManager

# 2. Configurar variables de entorno
cp .env.example .env
# Editar .env con las credenciales reales de producción

# 3. Levantar los servicios
docker-compose up -d
```

La aplicación se ejecuta en `http://localhost:8080`. Las migraciones de base de datos se aplican automáticamente al iniciar el contenedor de la aplicación web.

## Entornos Disponibles

El proyecto define tres entornos mediante archivos Docker Compose independientes que comparten el mismo archivo `.env`:

| Entorno | Archivo | Puerto Web | Puerto BD | Puerto Scraper |
|---------|---------|-----------|-----------|----------------|
| Producción | `docker-compose.yml` | 8080 | 1433 | 8000 |
| Desarrollo | `docker-compose.dev.yml` | 8081 | 1434 | 8001 |
| Testing | `docker-compose.test.yml` | 8082 | 1435 | 8002 |

Cada entorno utiliza su propia base de datos (`VendingDB`, `VendingDB_Dev`, `VendingDB_Test`) y volúmenes Docker independientes. Los tres entornos pueden ejecutarse simultáneamente sin interferencias.

## Servicios

El stack de producción se compone de tres servicios:

| Servicio | Imagen | Puerto | Volumen | Descripción |
|----------|--------|--------|--------|-------------|
| `webapp` | `vendingmanager` (build local) | 8080 | — | Aplicación principal ASP.NET Core 10 con Blazor |
| `db` | `mcr.microsoft.com/mssql/server:2022-latest` | 1433 | `mssql_data` | Base de datos SQL Server 2022 |
| `scraper` | Build local desde `services/scraper` | 8000 | `scraper_data` | Microservicio de scraping con Python, FastAPI y Playwright |

### Configuración de zona horaria

Todos los servicios operan en zona horaria `America/Santiago` (Chile continental, UTC-4/UTC-3).

## Health Checks

La aplicación expone dos endpoints de verificación de estado:

| Endpoint | Tipo | Propósito |
|----------|------|-----------|
| `GET /health` | Liveness | Verifica que el proceso de la aplicación está ejecutándose |
| `GET /health/db` | Readiness | Verifica que la conexión a SQL Server está operativa |

Ambos endpoints devuelven código HTTP 200 cuando el servicio está saludable. Son compatibles con cualquier herramienta de monitoreo externa como UptimeRobot, Grafana, Datadog o balanceadores de carga que requieran validación de estado.

## Variables de Entorno

El archivo `.env` en la raíz del proyecto alimenta las variables de Docker Compose. Las siguientes variables son requeridas para el entorno de producción:

| Variable | Propósito |
|----------|-----------|
| `MSSQL_SA_PASSWORD` | Contraseña del usuario administrador de SQL Server |
| `OURVEND_USER` | Usuario de autenticación para el portal externo OurVend |
| `OURVEND_PASS` | Contraseña de autenticación para el portal OurVend |
| `GEMINI_API_KEY` | Clave de API de Google Gemini para procesamiento de documentos |

## Persistencia de Datos

Los datos se almacenan en volúmenes Docker nombrados:

- `mssql_data`: base de datos SQL Server (archivos `.mdf` y `.ldf`)
- `scraper_data`: archivos descargados por el microservicio de scraping

Estos volúmenes persisten entre reinicios de contenedores y no se eliminan al ejecutar `docker-compose down` a menos que se use el flag `-v`.

## Consideraciones de Red

Los servicios se comunican internamente a través de la red por defecto de Docker Compose utilizando los nombres de servicio como nombres de host:

- La aplicación web accede a la base de datos mediante `Server=db`
- La aplicación web consume el scraper mediante `http://scraper:8000`

Los puertos expuestos al host (`8080`, `1433`, `8000`) pueden modificarse en `docker-compose.yml` si existe conflicto con otros servicios en el servidor.
