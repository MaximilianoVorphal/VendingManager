# Script para clonar la base de datos de PROD a DEV

# 1. Definir variables
$prodContainer = "vendingmanager-db-1"
$devContainer = "vendingmanager-db-dev-1"
$backupFile = "VendingDB_Backup.bak"
$saPassword = "YourStrong@Password!2025" # Hardcoded based on .env

Write-Host "Iniciando clonación de base de datos..." -ForegroundColor Cyan

# 2. Backup en PROD
Write-Host "1. Creando backup en PROD..."
docker exec $prodContainer /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P $saPassword -Q "BACKUP DATABASE [VendingDB] TO DISK = '/var/opt/mssql/$backupFile' WITH FORMAT"

# 3. Copiar backup al host
Write-Host "2. Copiando backup al host..."
docker cp "${prodContainer}:/var/opt/mssql/$backupFile" ./$backupFile

# 4. Copiar backup al contenedor DEV
Write-Host "3. Copiando backup al contenedor DEV..."
docker cp ./$backupFile "${devContainer}:/var/opt/mssql/$backupFile"

# 5. Restaurar en DEV
Write-Host "4. Restaurando en DEV como VendingDB_Dev..."
# Set Single User Mode to kill connection from WebApp
# We use a simple IF EXISTS check to avoid errors if DB doesn't exist yet
$restoreScript = "
IF DB_ID('VendingDB_Dev') IS NOT NULL
BEGIN
    ALTER DATABASE [VendingDB_Dev] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
END
RESTORE DATABASE [VendingDB_Dev] FROM DISK = '/var/opt/mssql/$backupFile' WITH MOVE 'VendingDB' TO '/var/opt/mssql/data/VendingDB_Dev.mdf', MOVE 'VendingDB_log' TO '/var/opt/mssql/data/VendingDB_Dev_log.ldf', REPLACE;
ALTER DATABASE [VendingDB_Dev] SET MULTI_USER;
"

# Escape double quotes for shell if necessary, but here likely okay as we pass it as a string to sqlcmd inside docker
# However, strictly passing multi-line string to -Q might be tricky in PowerShell -> Docker -> Bash -> SQLCMD.
# Better to write to a temp file inside container or use a single line with correct escaping.
# Let's try single line for robustness.
$restoreQuery = "IF DB_ID('VendingDB_Dev') IS NOT NULL ALTER DATABASE [VendingDB_Dev] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [VendingDB_Dev] FROM DISK = '/var/opt/mssql/$backupFile' WITH MOVE 'VendingDB' TO '/var/opt/mssql/data/VendingDB_Dev.mdf', MOVE 'VendingDB_log' TO '/var/opt/mssql/data/VendingDB_Dev_log.ldf', REPLACE; ALTER DATABASE [VendingDB_Dev] SET MULTI_USER;"

docker exec $devContainer /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P $saPassword -Q "$restoreQuery"

# 6. Limpieza
Write-Host "5. Limpiando archivos temporales..."
Remove-Item ./$backupFile
docker exec $prodContainer rm /var/opt/mssql/$backupFile
docker exec $devContainer rm /var/opt/mssql/$backupFile

Write-Host "¡Base de datos clonada exitosamente!" -ForegroundColor Green
Write-Host "Ahora DEV tiene los datos de PROD."
