# 3. Decomposición del VentasService y Migración de lógica C4

Date: 2026-04-13

## Status

Accepted

## Context

A medida que `VendingManager` escaló, el archivo `VentasService.cs` en la capa de Backend Central (`src/VendingManager/Infrastructure/Services/`) superó las 870 líneas de código. Se había convertido en un "God Object" anti-pattern responsable de:
- Operaciones atómicas de base de datos (`GetMaquinasAsync`).
- Importación de archivos (proxy delegado).
- Cálculos analíticos altísimamente demandantes y agrupaciones polinómicas (`GetStockoutAnalysisAsync`, `GetAnalisisProductosAsync`).
- Lógica de la cadena de reabastecimiento orientada puramente a predicción futura (`GetPurchaseSuggestionAsync`).

Esto generaba fuertes colisiones en Inyección de Dependencias, complejidad de testing, y acoples indeseados con los Controladores que no requerían funcionalidades de predicción.

## Decision

Al igual que se procedió en el ADR **0002**, seccionamos el monolito en servicios estrechos, respetando el principio SOLID de Responsabilidad Única y la segregación de interfaces de Clean Architecture:

1. **`IVentasService` / `VentasService` (Residual):**
   Fue drásticamente acortado. Ahora solo gestiona transacciones puramente CRUD a la vista de Ventas y mantenimiento histórico (como `FixDatesAsync` o `DeleteVentasRangoAsync`).
2. **`ISalesAnalyticsService` / `SalesAnalyticsService`:**
   Nuevo subdominio acoplado a la reportería densa. Maneja informes financieros, métricas Dashboard, velocidades de rotación e índices de Quiebre de Stock.
3. **`IPurchasingService` / `PurchasingService`:**
   Nuevo subdominio acoplado a la Supply Chain. Determina cuántos SKUs adquirir para la bodega en márgenes de 30 días con cruces directos al inventario actual (`ConfiguracionSlots`).

## Consequences

**Positivas:**
- Claridad técnica drástica.
- Prevención de fugas de memoria en llamadas no escaladas por culpa del DbContext unificado.
- Mayor mantenibilidad. Para modificar el cálculo Cacho/Estrella de un producto, ya no se navega por lógica HTTP o importaciones.
- Cumplimiento de la regla `<Arquitectura y Documentacion>` estipulada globalmente por el usuario.

**Negativas:**
- Ligero aumento sistemático de los constructores (Inyección de dependencias más grande) en `VentasController.cs` (pasando a requerir `ISalesAnalyticsService`, `IPurchasingService` y `ISalesImportService`).
