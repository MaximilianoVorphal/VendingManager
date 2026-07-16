# Runbook: Restauración de Backup — VendingDB_Test

Este runbook documenta cómo restaurar un backup `.bak` de **VendingDB_Dev** en la base **VendingDB_Test** (contenedor `db-test`, puerto `1435`) y verificar que los datos son consistentes.

## Quick Path

1. Identificar el `.bak` más reciente
2. Obtener los nombres lógicos del backup con `FILELISTONLY`
3. Ejecutar `RESTORE DATABASE` con `WITH MOVE`
4. Verificar con `SELECT COUNT(*)` en tablas clave

---

## Requisitos

- Stack `docker-compose.test.yml` levantado (db-test en puerto `1435`)
- Backup `.bak` disponible en el bind mount del host: `~/vendingmanager-backups-test/`
- `mssql-tools18` en el host, o acceso a `docker exec` sobre db-test

---

## Paso a Paso

### 0. Copiar backup al directorio del stack de test

El script `backup-db.sh` escribe en `~/vendingmanager-backups/` (bind mount del stack dev). Para restaurar en el stack de test, primero copiar el `.bak` al directorio del stack test:

```bash
cp ~/vendingmanager-backups/VendingDB_Dev_2026-07-16_0330.bak ~/vendingmanager-backups-test/
```

> Ajustar el nombre del archivo al `.bak` más reciente disponible en `~/vendingmanager-backups/`.

### 1. Listar backups disponibles

```bash
ls -lh ~/vendingmanager-backups-test/*.bak
```

Elegir el archivo más reciente, ej. `VendingDB_Dev_2026-07-16_0330.bak`.

### 2. Identificar nombres lógicos del backup

Cada backup contiene nombres lógicos para los archivos de datos y de log. Estos son necesarios para el `WITH MOVE`:

```bash
docker exec vendingmanager-db-test-1 \
    /opt/mssql-tools18/bin/sqlcmd \
    -S localhost \
    -U sa \
    -P "$MSSQL_SA_PASSWORD" \
    -C \
    -Q "RESTORE FILELISTONLY FROM DISK = '/var/opt/mssql/backup/VendingDB_Dev_2026-07-16_0330.bak'"
```

La salida muestra las columnas `LogicalName`, `Type` y `PhysicalName`. Anotar los valores de `LogicalName` para los tipos **D** (data) y **L** (log).

> Por convención los nombres lógicos suelen ser `VendingDB_Dev` (data) y `VendingDB_Dev_log` (log). Verificar siempre con `FILELISTONLY` antes de restaurar.

### 3. Restaurar la base de datos

Ejecutar el restore reubicando los archivos físicos a los paths del contenedor `db-test`:

```bash
docker exec vendingmanager-db-test-1 \
    /opt/mssql-tools18/bin/sqlcmd \
    -S localhost \
    -U sa \
    -P "$MSSQL_SA_PASSWORD" \
    -C \
    -Q "
RESTORE DATABASE [VendingDB_Test]
FROM DISK = '/var/opt/mssql/backup/VendingDB_Dev_2026-07-16_0330.bak'
WITH
    MOVE 'VendingDB_Dev' TO '/var/opt/mssql/data/VendingDB_Test.mdf',
    MOVE 'VendingDB_Dev_log' TO '/var/opt/mssql/data/VendingDB_Test_log.ldf',
    REPLACE
"
```

> **Nota**: Reemplazar `VendingDB_Dev` y `VendingDB_Dev_log` por los `LogicalName` reales obtenidos en el paso 2 si difieren.

La operación retorna mensajes similares a:

```
Processed 1232 pages for database 'VendingDB_Test', file 'VendingDB_Dev' on file 1.
Processed 456 pages for database 'VendingDB_Test', file 'VendingDB_Dev_log' on file 1.
RESTORE DATABASE successfully processed 1688 pages in 3.5 seconds (3.771 MB/s).
```

### 4. Verificar con row counts

Comparar las cantidades de registros entre la base origen y la restaurada para confirmar integridad:

```bash
# En VendingDB_Dev (contenedor db-dev, puerto 1434)
docker exec vendingmanager-db-dev-1 \
    /opt/mssql-tools18/bin/sqlcmd \
    -S localhost \
    -U sa \
    -P "$MSSQL_SA_PASSWORD" \
    -C \
    -Q "
SELECT 'VendingDB_Dev' AS [DB], 'dbo.Cargas' AS [Tabla], COUNT(*) AS [Rows] FROM VendingDB_Dev.dbo.Cargas
UNION ALL
SELECT 'VendingDB_Dev', 'dbo.Recargas', COUNT(*) FROM VendingDB_Dev.dbo.Recargas
UNION ALL
SELECT 'VendingDB_Dev', 'dbo.Ventas', COUNT(*) FROM VendingDB_Dev.dbo.Ventas
"
```

```bash
# En VendingDB_Test (contenedor db-test, puerto 1435)
docker exec vendingmanager-db-test-1 \
    /opt/mssql-tools18/bin/sqlcmd \
    -S localhost \
    -U sa \
    -P "$MSSQL_SA_PASSWORD" \
    -C \
    -Q "
SELECT 'VendingDB_Test' AS [DB], 'dbo.Cargas' AS [Tabla], COUNT(*) AS [Rows] FROM VendingDB_Test.dbo.Cargas
UNION ALL
SELECT 'VendingDB_Test', 'dbo.Recargas', COUNT(*) FROM VendingDB_Test.dbo.Recargas
UNION ALL
SELECT 'VendingDB_Test', 'dbo.Ventas', COUNT(*) FROM VendingDB_Test.dbo.Ventas
"
```

Los conteos deben coincidir exactamente entre ambas bases. Si no coinciden, el backup está incompleto o corrupto.

---

## Referencia Rápida

| Concepto | Valor |
|----------|-------|
| Contenedor db-test | `vendingmanager-db-test-1` |
| Puerto db-test | `1435` |
| Base destino | `VendingDB_Test` |
| Ruta backup en contenedor | `/var/opt/mssql/backup/` |
| Ruta backup en host | `~/vendingmanager-backups-test/` |
| Bind mount compose | `~/vendingmanager-backups-test:/var/opt/mssql/backup` |

---

## Instalación de Cron (Servidor)

> **Nota**: La configuración de cron pertenece al ámbito de backups automáticos, no a restauración. Se incluye aquí como referencia rápida.

Para activar los backups automáticos diarios, agregar esta línea al crontab en el servidor:

```bash
# Editar crontab
crontab -e

# Agregar (ejecuta a las 03:30 hora local Santiago)
# El script ya escribe a backup.log vía tee — no redirigir stdout/stderr para evitar duplicados
30 3 * * * ~/proyecto/VendingManager/scripts/backup-db.sh
```

Verificar que el cron esté activo:

```bash
crontab -l | grep backup-db
```

> **Nota**: La ruta `~/proyecto/VendingManager` puede variar según la ubicación del repositorio en cada servidor. Ajustar según corresponda.

---

## Verificación Manual (Post-Implementación)

Checklist de verificación para aplicar luego de implementar los cambios:

- [ ] **4.1 Smoke test**: Ejecutar `bash scripts/backup-db.sh` — debe salir con código `0`. Verificar que aparece un `.bak` en `~/vendingmanager-backups/` y una entrada en `backup.log`.
- [ ] **4.2 Negative test**: Mover temporalmente `.env` (`mv .env .env.bak`), ejecutar el script — debe salir con código `1` y mostrar mensaje claro de error. Restaurar `.env`.
- [ ] **4.3 Retention test**: Crear archivos `.bak` con mtime >14 días (`touch -t 202506010000 ~/vendingmanager-backups/old_test.bak`), ejecutar el script, verificar que `old_test.bak` fue eliminado.
- [ ] **4.4 Bind-mount survival**: Ejecutar `docker compose -f docker-compose.dev.yml down -v`, verificar que `~/vendingmanager-backups/` conserva los `.bak`.
- [ ] **4.5 Off-host (opcional)**: Configurar `OFFHOST_USER`, `OFFHOST_HOST`, `OFFHOST_PATH` en `.env`, ejecutar el script, verificar que el archivo aparece en el destino remoto.
- [ ] **4.6 Restore funcional (REC-002)**: Restaurar el `.bak` en db-test siguiendo los pasos de este runbook y verificar que los row counts de `dbo.Cargas`, `dbo.Recargas`, `dbo.Ventas` coinciden.
