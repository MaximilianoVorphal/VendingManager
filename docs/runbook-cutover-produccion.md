# Runbook: Cutover a producción real (F1)

**Objetivo**: Migrar el stack de producción de `docker-compose.dev.yml` (DB `VendingDB_Dev`, puerto 8081) a `docker-compose.yml` (DB `VendingDB`, puerto 8080).

**Precondiciones**:
- F0 (backups) desplegado en el servidor — `~/vendingmanager-backups/` existe con backups recientes
- F5 (comprobantes-a-db) desplegado — no hay archivos en disco que copiar
- Acceso SSH al servidor vía Tailscale

## Paso 1: Backup pre-cutover

```bash
ssh <server>
cd ~/proyecto/VendingManager
bash scripts/backup-db.sh
```

Verificar que el backup se creó:
```bash
ls -lh ~/vendingmanager-backups/
```

## Paso 2: Restaurar backup en la nueva DB

El stack de prod usa una DB diferente (`VendingDB` vs `VendingDB_Dev`). Restaurar el backup de dev en la nueva DB:

```bash
# Detener el stack nuevo si ya existe (primera vez no hay nada)
docker compose -f docker-compose.yml down --remove-orphans 2>/dev/null

# Levantar solo la DB de prod (sin webapp ni scraper todavía)
docker compose -f docker-compose.yml up -d db

# Esperar que SQL Server acepte conexiones (~30s)
# Restaurar backup (ver docs/runbook-restore.md para el comando exacto)
#
# Ejemplo con sqlcmd:
# docker exec -i $(docker compose -f docker-compose.yml ps -q db) \
#   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
#   -Q "RESTORE DATABASE [VendingDB] FROM DISK='/var/opt/mssql/backup/VendingDB_Dev_<fecha>.bak' WITH REPLACE"
```

## Paso 3: Validar el stack nuevo (en paralelo con el viejo)

```bash
# Parar el scraper en el stack viejo para evitar doble tráfico a OurVend
docker compose -f docker-compose.dev.yml stop scraper-dev

# Levantar el stack nuevo completo
docker compose -f docker-compose.yml up -d --build
```

Validar:
- `docker compose -f docker-compose.yml ps` — los 3 servicios `(healthy)` o `Up`
- Login en `http://<server>:8080`
- Navegar a Ventas, ver datos
- Subir un comprobante de transferencia y verlo
- Ver un comprobante viejo (el backfill de F5 ya migró los archivos a DB)

## Paso 4: Cutover de tráfico

Repuntar el acceso de usuarios de 8081 → 8080:
- Si usan Tailscale directo al puerto: cambiar de `:8081` a `:8080`
- Si hay reverse proxy (nginx/Caddy): cambiar upstream port de 8081 a 8080

## Paso 5: Apagar el stack viejo

```bash
# Verificar que nadie está usando el puerto 8081
# (esperar ~5 min después del repunteo)

# Detener el stack dev
docker compose -f docker-compose.dev.yml down

# Opcional: mantenerlo como staging real en otro puerto
# Editar docker-compose.dev.yml y cambiar puerto 8081→8091, luego up -d
```

## Rollback

Si algo falla en el stack nuevo:

```bash
# Volver a levantar el stack viejo
docker compose -f docker-compose.dev.yml up -d --build

# Repuntar tráfico de vuelta a 8081

# Detener el stack nuevo
docker compose -f docker-compose.yml down
```

El stack viejo no se borró (solo `down`, no `down -v`), así que los datos y volúmenes siguen intactos.

## Post-cutover

- Verificar que los backups nocturnos (cron, F0) apuntan a la DB correcta (`VendingDB`)
- Verificar que CI despliega a `docker-compose.yml` (ya actualizado en este cambio)
- Monitorear logs: `docker compose -f docker-compose.yml logs -f --tail=50`
