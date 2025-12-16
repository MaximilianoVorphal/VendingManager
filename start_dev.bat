@echo off
echo ===================================================
echo   Iniciando Entorno de Desarrollo VendingManager
echo   Modo: Antigravity / No-Debug (Hot Reload Activado)
echo ===================================================

echo 1. Iniciando API (Backend)...
start "VendingManager API" dotnet watch --project "VendingManager/VendingManager.csproj" --urls "http://localhost:5093"

echo 2. Iniciando Cliente Web (Frontend)...
start "VendingManager Web" dotnet watch --project "VendingManager.Web/VendingManager.Web.csproj" --urls "http://localhost:5095"

echo.
echo ===================================================
echo   TODO LISTO!
echo   - API corriendo en: http://localhost:5093
echo   - Web corriendo en: http://localhost:5095
echo.
echo   Puede cerrar esta ventana, los servicios
echo   seguiran corriendo en sus propias ventanas.
echo ===================================================

