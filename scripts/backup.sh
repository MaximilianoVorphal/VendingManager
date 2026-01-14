#!/bin/bash

# Configuration
DB_CONTAINER="vendingmanager-db-1" # Adjust if your container name is different (check 'docker ps')
DB_USER="sa"
DB_NAME="VendingDB"

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
    echo "Error: MSSQL_SA_PASSWORD is not set. Please set it in .env or environment."
    exit 1
fi

TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
BACKUP_FILENAME="VendingDB_Backup_$TIMESTAMP.bak"
CONTAINER_BACKUP_PATH="/var/opt/mssql/$BACKUP_FILENAME"
HOST_BACKUP_DIR="./backups"

mkdir -p "$HOST_BACKUP_DIR"

echo "Starting backup of $DB_NAME..."

# Execute backup inside container
docker exec $DB_CONTAINER /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U "$DB_USER" -P "$MSSQL_SA_PASSWORD" -C \
    -Q "BACKUP DATABASE [$DB_NAME] TO DISK = '$CONTAINER_BACKUP_PATH' WITH FORMAT"

if [ $? -eq 0 ]; then
    echo "Database backup created inside container at $CONTAINER_BACKUP_PATH"
    
    # Copy backup to host
    docker cp "$DB_CONTAINER:$CONTAINER_BACKUP_PATH" "$HOST_BACKUP_DIR/$BACKUP_FILENAME"
    
    if [ $? -eq 0 ]; then
        echo "Backup copied to host at $HOST_BACKUP_DIR/$BACKUP_FILENAME"
        
        # Cleanup inside container
        docker exec $DB_CONTAINER rm "$CONTAINER_BACKUP_PATH"
        echo "Cleanup completed."
    else
        echo "Error copying backup to host."
        exit 1
    fi
else
    echo "Error creating backup."
    exit 1
fi
