# ADR 013: Horas Operativas como Base Temporal para Análisis de Quiebre de Stock

## Estado

Aceptado - 2026-07-13

## Contexto

El análisis de quiebre de stock en `SalesAnalyticsService.GetStockoutAnalysisAsync` usaba horas de reloj (24h/día) como base temporal para todos los cálculos: umbral de silencio, horas activas, velocidad diaria y días sin stock. Sin embargo, las máquinas vending **no venden las 24 horas del día** — su horario operativo real es 08:00–22:00 (14h/día).

Esto producía dos problemas:

1. **Subestimación de la velocidad de venta**: al dividir unidades vendidas por horas de reloj (24h) en vez de horas operativas (14h), la velocidad por hora era artificialmente baja, lo que retrasaba la detección de quiebres inminentes.

2. **Falsos negativos en umbral de silencio**: un producto sin ventas durante la noche (p.ej., 22:00–08:00, 10h de reloj) no debía contar como "horas sin stock" porque la máquina estaba cerrada. El algoritmo necesitaba discriminar horas operativas de horas muertas.

El helper `HorarioOperativoHelper.HorasEnRangoOperativo` ya existía y era usado correctamente por `LogisticaPredictivaService`, pero `SalesAnalyticsService` no lo utilizaba.

## Decisión

Se refactoriza `GetStockoutAnalysisAsync` para que toda la matemática temporal use horas operativas (14h/día, 08:00–22:00) a través de `HorarioOperativoHelper`:

- **horasActivas**, **horasSinStock**, **horasDiferencia**: calculadas con `HorasEnRangoOperativo` en vez de `(fin - inicio).TotalHours`.
- **VelocidadDiaria**: `velocidadPorHora × HorasOperativasPorDia (14)` en vez de `× 24`.
- **DiasSinStock**: `horasSinStock / 14` en vez de `/ 24`.
- **umbralHorasSilencio** default: **14 horas operativas** (≈24h de reloj). Se infiere quiebre cuando un producto no vende durante >14h operativas dentro de la ventana activa de la máquina.

El valor `HorasOperativasPorDia = 14` se expone como constante pública en `HorarioOperativoHelper.cs` para que todos los consumidores compartan el mismo valor.

## Consecuencias

- **Positivas**: la velocidad diaria refleja el ritmo real de venta (mayor que antes), lo que permite detectar quiebres inminentes más temprano. El umbral de silencio operativo ignora correctamente horas muertas (nocturnas), eliminando falsos positivos por falta de ventas fuera del horario comercial.
- **Negativas**: cambia **todas las métricas reportadas** por el StockoutDashboard (dineroPerdido, gananciaPerdida, velocidad, DiasHastaStockout). Esto es intencional, pero los usuarios verán números distintos a los reportados históricamente.
- **Consistencia**: ahora ambas vías de detección de quiebre (A: silencio en `SalesAnalyticsService`, B: snapshot en `TemplateRecargaAnalyticsService`) usan el mismo régimen de horas operativas, eliminando la divergencia documentada entre ×14 y ×24.
- **Mantenibilidad**: agregar o cambiar el horario operativo se hace en un solo lugar (`HorarioOperativoHelper`), que ya es la fuente de verdad para todo el dominio logístico.
