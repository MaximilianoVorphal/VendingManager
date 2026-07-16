#!/bin/bash
set -euo pipefail

# =============================================================================
# backup-db.sh — Backup automatizado de VendingDB_Dev (SQL Server en Docker)
#
# Ejecuta BACKUP DATABASE ... WITH INIT, COMPRESSION via docker exec + sqlcmd
# sobre la base VendingDB_Dev del contenedor db-dev, escribiendo el .bak en
# un bind mount host que sobrevive a `docker compose down -v`.
#
# Post-backup: pruning de .bak con más de 14 días, copia off-host opcional
# vía scp sobre Tailscale, y logging timestamped a backup.log.
#
# Uso:
#   bash scripts/backup-db.sh
#
# Variables de entorno (opcionales con defaults):
#   BACKUP_DIR               Directorio local de backups (def: ~/vendingmanager-backups)
#   BACKUP_CONTAINER_NAME    Nombre del contenedor Docker (def: vendingmanager-db-dev-1)
#   BACKUP_DB_NAME           Nombre de la base de datos (def: VendingDB_Dev)
#   OFFHOST_USER             Usuario SSH para copia off-host (opcional)
#   OFFHOST_HOST             Host SSH para copia off-host (opcional)
#   OFFHOST_PATH             Ruta destino en host remoto (opcional)
#
# Códigos de salida:
#   0  — Backup exitoso
#   1  — Error de configuración (.env faltante o MSSQL_SA_PASSWORD no definida)
#   2  — Error en backup (docker exec o sqlcmd falló)
# =============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# ── Defaults ─────────────────────────────────────────────────────────────────
BACKUP_DIR="${BACKUP_DIR:-$HOME/vendingmanager-backups}"
BACKUP_CONTAINER_NAME="${BACKUP_CONTAINER_NAME:-vendingmanager-db-dev-1}"
BACKUP_DB_NAME="${BACKUP_DB_NAME:-VendingDB_Dev}"
LOG_FILE="$BACKUP_DIR/backup.log"
ERROR_LOG_FILE="$BACKUP_DIR/backup-error.log"
TIMESTAMP="$(date '+%Y-%m-%d_%H%M%S')"
BACKUP_FILENAME="${BACKUP_DB_NAME}_${TIMESTAMP}.bak"
BACKUP_CONTAINER_PATH="/var/opt/mssql/backup/$BACKUP_FILENAME"

# ── Logging helper ──────────────────────────────────────────────────────────
log() {
    local level="$1"
    shift
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] [$level] $*" | tee -a "$LOG_FILE" || true
}

log_error() {
    local level="$1"
    shift
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] [$level] $*" | tee -a "$LOG_FILE" >&2 || true
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] [$level] $*" >> "$ERROR_LOG_FILE" || true
}

# ── Asegurar directorio de backups ──────────────────────────────────────────
mkdir -p "$BACKUP_DIR"
# SQL Server runs as mssql (UID 10001) inside the container; the bind-mounted
# host directory must be writable by that UID or BACKUP DATABASE fails with
# Access Denied. Try chown first (needs root), fall back to chmod.
chown 10001:0 "$BACKUP_DIR" 2>/dev/null || chmod 777 "$BACKUP_DIR"

# ── (1) Source .env ─────────────────────────────────────────────────────────
if [ -f "$REPO_ROOT/.env" ]; then
    set -a
    # shellcheck source=../.env
    . "$REPO_ROOT/.env"
    set +a
    log "INFO" ".env sourced from $REPO_ROOT/.env"
else
    log_error "FATAL" ".env not found at $REPO_ROOT/.env"
    exit 1
fi

if [ -z "${MSSQL_SA_PASSWORD:-}" ]; then
    log_error "FATAL" "MSSQL_SA_PASSWORD is not set in .env"
    exit 1
fi

# ── (2) Backup — docker exec → sqlcmd BACKUP DATABASE ──────────────────────
log "INFO" "Starting backup of [$BACKUP_DB_NAME] via container $BACKUP_CONTAINER_NAME"

BACKUP_ERR=$(mktemp)
if docker exec "$BACKUP_CONTAINER_NAME" \
    /opt/mssql-tools18/bin/sqlcmd \
    -S localhost \
    -U sa \
    -P "$MSSQL_SA_PASSWORD" \
    -C \
    -Q "BACKUP DATABASE [$BACKUP_DB_NAME] TO DISK='$BACKUP_CONTAINER_PATH' WITH INIT, COMPRESSION" 2>"$BACKUP_ERR"; then
    rm -f "$BACKUP_ERR"
    log "INFO" "Backup completed: $BACKUP_FILENAME ($BACKUP_DIR/$BACKUP_FILENAME)"
else
    ERR_OUT=$(cat "$BACKUP_ERR" 2>/dev/null)
    rm -f "$BACKUP_ERR"
    log_error "ERROR" "Backup FAILED: ${ERR_OUT:-docker exec or sqlcmd returned non-zero}"
    exit 2
fi

# ── (3) Retention — pruning de .bak con más de 14 días ─────────────────────
log "INFO" "Running retention: removing .bak files older than 14 days from $BACKUP_DIR"

if find "$BACKUP_DIR" -maxdepth 1 -name '*.bak' -mtime +14 -delete 2>>"$ERROR_LOG_FILE"; then
    log "INFO" "Retention pruning completed"
else
    log "WARN" "Retention pruning encountered issues (see $ERROR_LOG_FILE)"
fi

# ── (4) Off-host copy via scp (opcional) ───────────────────────────────────
if [ -n "${OFFHOST_USER:-}" ] && [ -n "${OFFHOST_HOST:-}" ] && [ -n "${OFFHOST_PATH:-}" ]; then
    LATEST_BAK=$(ls -t "$BACKUP_DIR"/*.bak 2>/dev/null | head -1)
    if [ -n "$LATEST_BAK" ]; then
        log "INFO" "Off-host copy: scp $LATEST_BAK to ${OFFHOST_USER}@${OFFHOST_HOST}:${OFFHOST_PATH}/"
        if scp -o BatchMode=yes "$LATEST_BAK" "${OFFHOST_USER}@${OFFHOST_HOST}:${OFFHOST_PATH}/" 2>>"$ERROR_LOG_FILE"; then
            log "INFO" "Off-host copy completed"
        else
            log "WARN" "Off-host scp failed — local backup preserved"
        fi
    else
        log "WARN" "Off-host copy skipped — no .bak files found in $BACKUP_DIR"
    fi
else
    log "INFO" "Off-host copy not configured (set OFFHOST_USER, OFFHOST_HOST, OFFHOST_PATH to enable)"
fi

# ── Done ────────────────────────────────────────────────────────────────────
log "INFO" "Backup cycle complete: $BACKUP_FILENAME"
