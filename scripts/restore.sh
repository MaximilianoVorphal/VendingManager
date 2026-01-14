#!/bin/bash

# Configuration
DB_CONTAINER="vendingmanager-db-1" # Adjust if your container name is different
DB_USER="sa"
DB_NAME="VendingDB"

if [ -z "$1" ]; then
    echo "Usage: ./restore.sh <path_to_backup_file.bak>"
    exit 1
fi

BACKUP_FILE_HOST="$1"
BACKUP_FILENAME=$(basename "$BACKUP_FILE_HOST")
CONTAINER_RESTORE_PATH="/var/opt/mssql/$BACKUP_FILENAME"

# Load environment variables
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
ENV_FILE="$SCRIPT_DIR/../.env"

if [ -f "$ENV_FILE" ]; then
  export $(grep -v '^#' "$ENV_FILE" | xargs)
elif [ -f ".env" ]; then
  export $(grep -v '^#' .env | xargs)
fi

# Check password
if [ -z "$MSSQL_SA_PASSWORD" ]; then
    echo "Error: MSSQL_SA_PASSWORD is not set."
    exit 1
fi

if [ ! -f "$BACKUP_FILE_HOST" ]; then
    echo "Error: Backup file not found at $BACKUP_FILE_HOST"
    exit 1
fi

echo "Restoring database $DB_NAME from $BACKUP_FILE_HOST..."

# Copy backup to container
echo "Copying backup file to container..."
docker cp "$BACKUP_FILE_HOST" "$DB_CONTAINER:$CONTAINER_RESTORE_PATH"

if [ $? -ne 0 ]; then
    echo "Error copying file to container."
    exit 1
fi

# Restore database
# Note: WITH REPLACE overwrites the existing database.
# We also close existing connections by setting to SINGLE_USER momentarily (optional but recommended for restore on active db)
echo "Restoring database..."

docker exec $DB_CONTAINER /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U "$DB_USER" -P "$MSSQL_SA_PASSWORD" -C \
    -Q "ALTER DATABASE [$DB_NAME] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [$DB_NAME] FROM DISK = '$CONTAINER_RESTORE_PATH' WITH REPLACE; ALTER DATABASE [$DB_NAME] SET MULTI_USER;"

if [ $? -eq 0 ]; then
    echo "Database restored successfully."
    
    # Cleanup
    docker exec $DB_CONTAINER rm "$CONTAINER_RESTORE_PATH"
else
    echo "Error restoring database."
    exit 1
fi
