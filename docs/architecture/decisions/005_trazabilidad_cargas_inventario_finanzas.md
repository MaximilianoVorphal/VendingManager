# ADR-005: Trazabilidad Cargas ↔ Inventario ↔ Finanzas

**Estado:** Aceptado  
**Fecha:** 2026-04-15  
**Contexto:** Fase 3 de Hoja de Ruta — Trazabilidad Logística-Financiera

## Contexto

El sistema de Órdenes de Carga (`OrdenCarga`) gestionaba la logística de reposición (qué se saca de bodega, qué sobra), pero tenía tres brechas:

1. **No se registraba el costo** de lo entregado — sin snapshot de `CostoPromedio` al momento de la carga
2. **No se actualizaba el inventario de la máquina** (`ConfiguracionSlot.StockActual`) automáticamente al finalizar
3. **No se creaba MovimientoCaja** — la carga no aparecía como gasto en tesorería

Esto dejaba la operación de carga desconectada del módulo financiero y del inventario en máquina.

## Decisión

### 1. Snapshot de costo en `DetalleOrdenCarga.CostoUnitario`
Se guarda el `CostoPromedio` del producto **al momento de crear la orden**. Así, si el costo del producto cambia después, el dato histórico queda preservado.

### 2. Actualización automática de slots al finalizar
Al finalizar la orden, se busca el `ConfiguracionSlot` correspondiente (por `MaquinaId` + `ProductoId`) y se incrementa `StockActual` con la cantidad realmente cargada (`CantidadSolicitada - CantidadRetornada`). Se aplica clamp a `CapacidadMaxima`.

### 3. Registro automático en Caja al finalizar
Se crea un `MovimientoCaja` tipo `GASTO` categoría `MERCADERIA` con:
- Monto negativo = costo total real (post-retornos)
- `OrdenCargaId` = FK para trazabilidad bidireccional
- Descripción = `"Carga #ID — NombreOrden/MáquinaNombre"`

Se eligió registrar al **finalizar** (no al crear) porque en ese momento se conoce la cantidad real cargada.

## Consecuencias

### Positivas
- Cada carga tiene un costo financiero visible en Caja
- El inventario de la máquina se actualiza automáticamente
- El usuario ya no necesita ir a `/reposicion` manualmente después de cada carga
- El Estado de Resultados refleja automáticamente el costo de mercadería entregada

### Negativas / Consideraciones
- Órdenes históricas tendrán `CostoUnitario = 0` (no retroactivo)
- Si el usuario edita manualmente `/reposicion`, puede generar discrepancias vs. lo registrado por la orden
- Se requiere migración EF Core (`AddCostoUnitarioToDetalleOrdenCarga`)

## Archivos Afectados
- `Core/Entities/OrdenCarga.cs` — +`CostoUnitario`
- `Infrastructure/Services/OrdenCargaService.cs` — Lógica de snapshot, slot update, y registro caja
- `Infrastructure/Data/ApplicationDbContext.cs` — Configuración decimal
- `Shared/DTOs/OrdenCargaDtos.cs` — +`CostoTotal`, +`CostoUnitario`
- `Web/Pages/Cargas.razor` + `.cs` — UI muestra costos
