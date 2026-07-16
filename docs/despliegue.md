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
# Crear archivo .env con las variables requeridas (ver .env de desarrollo como referencia)
# Editar .env con las credenciales reales de producción

# 3. Levantar los servicios
docker-compose up -d
```

La aplicación se ejecuta en `http://localhost:8080`. Las migraciones de base de datos se aplican automáticamente al iniciar el contenedor de la aplicación web.

## Flujo de Despliegue (CI/CD)

Los despliegues se disparan por **tags**, no por pushes a `master`:

- **Push a `master`**: el pipeline de CI (`.github/workflows/ci.yml`) solo compila y ejecuta los tests. No despliega.
- **Push de un tag `v*`** (por ejemplo `v1.2.0`): el pipeline compila, ejecuta los tests y, si pasan, ejecuta el job `deploy`, que se conecta al servidor vía Tailscale + SSH, hace checkout del tag, ejecuta un backup pre-deploy (`scripts/backup-db.sh`) y levanta el stack de producción con `docker compose -f docker-compose.yml up -d --build`.
- **`workflow_dispatch`**: permite relanzar el workflow manualmente; el job `deploy` solo se ejecuta si el ref despachado es un tag `v*`.

```bash
# Desplegar una nueva versión
git tag v1.2.0
git push origin v1.2.0
```

## Backups

- Backups nocturnos de la base de datos mediante `scripts/backup-db.sh`, programado vía cron en el servidor.
- Los archivos `.bak` se escriben en un bind mount del host (`~/vendingmanager-backups`), que sobrevive a `docker compose down -v`.
- Retención de **14 días**: los `.bak` más antiguos se eliminan automáticamente en cada ejecución.
- Copia off-host opcional vía `scp` sobre Tailscale (ver variables `OFFHOST_*` más abajo).
- El procedimiento de restauración está documentado en [docs/runbook-restore.md](runbook-restore.md).

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

Variables opcionales:

| Variable | Propósito |
|----------|-----------|
| `SEED_ADMIN_PASSWORD` | Rota la contraseña del usuario `admin` al iniciar la aplicación. Mientras se use la contraseña por defecto, la app emite una advertencia en el arranque |
| `OFFHOST_USER` | Usuario SSH para la copia off-host de backups (opcional) |
| `OFFHOST_HOST` | Host SSH de destino para la copia off-host de backups (opcional) |
| `OFFHOST_PATH` | Ruta de destino en el host remoto para la copia off-host (opcional) |

Las tres variables `OFFHOST_*` deben definirse juntas para habilitar la copia off-host en `scripts/backup-db.sh`; si falta alguna, el paso se omite.

## Persistencia de Datos

Los datos se almacenan en volúmenes Docker nombrados:

- `mssql_data`: base de datos SQL Server (archivos `.mdf` y `.ldf`)
- `scraper_data`: archivos descargados por el microservicio de scraping

Estos volúmenes persisten entre reinicios de contenedores y no se eliminan al ejecutar `docker-compose down` a menos que se use el flag `-v`.

## Consideraciones de Red

Los servicios se comunican internamente a través de la red por defecto de Docker Compose utilizando los nombres de servicio como nombres de host:

- La aplicación web accede a la base de datos mediante `Server=db`
- La aplicación web consume el scraper mediante `http://scraper:8000`

Los puertos expuestos al host (`8080`, `1433`, `8000`) pueden modificarse en `docker-compose.yml` si existe conflicto con otros servicios en el servidor. El puerto `1433` de SQL Server está ligado únicamente a `127.0.0.1`, por lo que la base de datos no queda expuesta fuera del propio servidor.
