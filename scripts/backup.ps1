param(
    [Parameter(Mandatory=$false)]
    [string]$Database = "VendingDB",

    [Parameter(Mandatory=$false)]
    [string]$Container = ""
)

# Configuración base
$DbUser = "sa"
$DbName = $Database
$BackupDir = Join-Path $PSScriptRoot "..\backups"

# Crear directorio de backups si no existe
if (!(Test-Path -Path $BackupDir)) {
    New-Item -ItemType Directory -Path $BackupDir | Out-Null
    Write-Host "Directorio de backups creado: $BackupDir"
}

# Cargar variables de entorno desde .env
$EnvFile = Join-Path $PSScriptRoot "..\.env"
if (Test-Path $EnvFile) {
    Get-Content $EnvFile | ForEach-Object {
        if ($_ -match "^\s*([^#=]+)=(.*)$") {
            $name = $matches[1].Trim()
            $value = $matches[2].Trim()
            if ($name -eq "MSSQL_SA_PASSWORD") {
                $Global:MssqlSaPassword = $value
            }
            if ($name -eq "DB_CONTAINER" -and [string]::IsNullOrEmpty($Container)) {
                $Global:DbContainer = $value
            }
        }
    }
}

if ([string]::IsNullOrEmpty($Global:MssqlSaPassword)) {
    Write-Error "Error: MSSQL_SA_PASSWORD no encontrado en .env."
    exit 1
}

# Identificar el contenedor de base de datos
$ContainerName = $Container
if ([string]::IsNullOrEmpty($ContainerName)) {
    $ContainerName = $Global:DbContainer
    if ([string]::IsNullOrEmpty($ContainerName)) {
        # Por defecto apuntamos a vendingmanager-db-1
        $ContainerName = "vendingmanager-db-1"
    }
}


Write-Host "Contenedor objetivo: $ContainerName"

# Verificar que el contenedor exista y esté en ejecución
$ContainerExists = docker ps --format "{{.Names}}" --filter "name=$ContainerName" | Select-Object -First 1
if ([string]::IsNullOrEmpty($ContainerExists)) {
    Write-Error "No se pudo encontrar el contenedor de SQL Server en ejecución llamado '$ContainerName'."
    exit 1
}

Write-Host "Contenedor encontrado y en ejecución: $ContainerName"

$Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$BackupFilename = "VendingDB_Backup_$Timestamp.bak"
$ContainerBackupPath = "/var/opt/mssql/$BackupFilename"
$LocalBackupPath = Join-Path $BackupDir $BackupFilename

Write-Host "Iniciando backup de la base de datos $DbName..."

# Ejecutar backup dentro del contenedor
# Nota: Usamos /opt/mssql-tools18/bin/sqlcmd para SQL Server 2022. Si falla, probar con /opt/mssql-tools/bin/sqlcmd
# Ejecutar backup dentro del contenedor
# Nota: Usamos /opt/mssql-tools18/bin/sqlcmd para SQL Server 2022.
# -b: Asegura que sqlcmd devuelva error si el backup falla
$BackupCommand = "BACKUP DATABASE [$DbName] TO DISK = '$ContainerBackupPath' WITH FORMAT, COPY_ONLY"

# Ejecutamos sqlcmd directamente (sin /bin/bash -c) para evitar problemas de escaping de comillas
docker exec $ContainerName /opt/mssql-tools18/bin/sqlcmd -S localhost -U $DbUser -P $Global:MssqlSaPassword -C -b -Q $BackupCommand

if ($LASTEXITCODE -eq 0) {
    Write-Host "Comando SQL ejecutado exitosamente."
    
    # Verificar que el archivo realmente se creó
    docker exec $ContainerName test -f $ContainerBackupPath
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Archivo de backup verificado en el contenedor: $ContainerBackupPath"
        
        # Copiar al host
        Write-Host "Copiando backup al host..."
        docker cp "$($ContainerName):$ContainerBackupPath" "$LocalBackupPath"
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Backup guardado exitosamente en: $LocalBackupPath"
            
            # Limpiar dentro del contenedor
            docker exec $ContainerName rm "$ContainerBackupPath"
            Write-Host "Archivo temporal eliminado del contenedor."
        }
        else {
            Write-Error "Error al copiar el archivo desde el contenedor."
        }
    }
    else {
        Write-Error "El comando finalizó bien, pero no se encuentra el archivo de backup en el contenedor (test -f falló)."
    }
}
else {
    Write-Error "Error al ejecutar el backup en SQL Server. Verifique el mensaje de error anterior." 
}
