$container = "vendingmanager-db-1"
$dbUser = "SA"
$dbPass = "YourStrong@Password!2025"
$dbName = "VendingDB"

# List available backups
$backupDir = ".\backups"
if (-not (Test-Path $backupDir)) {
    Write-Host "No se encuentra la carpeta 'backups'."
    exit
}

$backups = Get-ChildItem -Path $backupDir -Filter "*.bak" | Sort-Object LastWriteTime -Descending

if ($backups.Count -eq 0) {
    Write-Host "No se encontraron archivos .bak en $backupDir"
    exit
}

Write-Host "Backups disponibles:"
for ($i = 0; $i -lt $backups.Count; $i++) {
    Write-Host "$($i+1): $($backups[$i].Name) ($($backups[$i].LastWriteTime))"
}

$selection = Read-Host "Seleccione el numero del backup a restaurar (1-$($backups.Count)) [Default: 1]"
if ([string]::IsNullOrWhiteSpace($selection)) {
    $selection = 1
}

$index = [int]$selection - 1
if ($index -lt 0 -or $index -ge $backups.Count) {
    Write-Host "Seleccion invalida."
    exit
}

$selectedBackup = $backups[$index]
Write-Host "Restaurando: $($selectedBackup.Name)..."

$innerPath = "/var/opt/mssql/data/$($selectedBackup.Name)"

# Copy backup to container
Write-Host "Copiando archivo al contenedor..."
docker cp "$($selectedBackup.FullName)" "$container`:$innerPath"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error al copiar el archivo al contenedor."
    exit
}

# Restore database
# We need to close existing connections first
$sqlCommand = @"
USE master;
ALTER DATABASE [$dbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
RESTORE DATABASE [$dbName] FROM DISK = '$innerPath' WITH REPLACE;
ALTER DATABASE [$dbName] SET MULTI_USER;
"@

Write-Host "Ejecutando restauracion..."
# Using -C for TrustServerCertificate which is often needed for newer SQL drivers/images
docker exec -i $container /opt/mssql-tools18/bin/sqlcmd -S localhost -U $dbUser -P $dbPass -C -Q "$sqlCommand"

if ($LASTEXITCODE -eq 0) {
    Write-Host "Base de datos restaurada exitosamente."
    
    # Cleanup
    docker exec $container rm "$innerPath"
} else {
    Write-Host "Error durante la restauracion sqlcmd."
}
