# ADR-004: Modelo de Gastos Recurrentes y Trazabilidad Caja-Compra

**Estado:** Aceptado  
**Fecha:** 2026-04-14  
**Contexto:** Fase 1 de Hoja de Ruta — Cerrar Brechas de Datos

## Contexto

El sistema VendingManager tenía varios flujos de datos desconectados:

1. **Gastos fijos mensuales** (chips de máquina, bencina mensual) no tenían un modelo formal. Se registraban manualmente cada mes en Caja sin mecanismo de alerta ni tracking.
2. **Vinculación Caja ↔ Compra** se hacía buscando por texto en `MovimientoCaja.Descripcion` (frágil y propenso a errores).

## Decisión

### 1. Nueva Entidad: `GastoRecurrente`

Se crea una entidad `GastoRecurrente` con campos: Descripcion, MontoEstimado, Categoria, MaquinaId (opcional), Activo.

El servicio `GastoRecurrenteService` cruza esta tabla contra `MovimientosCaja` del mes para detectar gastos pendientes automáticamente. La UI muestra un banner de alerta en la página de Caja.

### 2. FK explícitos en `MovimientoCaja`

Se agregan dos campos nullable:
- `CompraId` → FK hacia `Compra` (reemplaza búsqueda por texto)
- `GastoRecurrenteId` → FK hacia `GastoRecurrente` (evita duplicados por mes)

### 3. Integración en UI de Caja

Los gastos recurrentes se gestionan dentro de la misma página de Caja (panel colapsable) para mantener el contexto financiero unificado. No se crea una página separada.

## Consecuencias

- **Positivas:** Alertas automáticas de gastos pendientes; trazabilidad bidireccional confiable; eliminación de búsqueda frágil por texto.
- **Negativas:** Migración requerida en base de datos (automática via EF Core).
- **Riesgo:** Los `MovimientoCaja` existentes de compras previas NO tendrán `CompraId` (migración no retroactiva). Las nuevas compras sí lo tendrán.

## Archivos Afectados

| Archivo | Acción |
|---|---|
| `Core/Entities/GastoRecurrente.cs` | NUEVO |
| `Core/Interfaces/IGastoRecurrenteService.cs` | NUEVO |
| `Infrastructure/Services/GastoRecurrenteService.cs` | NUEVO |
| `Controllers/GastoRecurrenteController.cs` | NUEVO |
| `Core/Entities/MovimientoCaja.cs` | MODIFICADO (+ CompraId, GastoRecurrenteId) |
| `Infrastructure/Services/CompraService.cs` | MODIFICADO (usa FK, no texto) |
| `Infrastructure/Data/ApplicationDbContext.cs` | MODIFICADO (+ DbSet) |
| `Program.cs` | MODIFICADO (+ DI) |
| `VendingManager.Web/Pages/Caja.razor` | MODIFICADO (banner + panel + modal) |
| `Shared/DTOs/MovimientoCajaDto.cs` | MODIFICADO (+ campos) |
