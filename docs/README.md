# Documentación Viva — VendingManager

Documentación por **dominio de negocio**, basada en el estado actual del código. Cada archivo sigue la
misma estructura: Resumen de Negocio → Entidades Clave → Reglas de Negocio → Flujo Técnico.

## Índice de dominios

| # | Documento | De qué trata |
| --- | --- | --- |
| 1 | [Pipeline de Ingesta](./1-pipeline-ingesta.md) | Scraping automatizado de OurVend, polling cada ~2h, circuit breaker, importación y deduplicación de ventas. |
| 2 | [Inteligencia Financiera](./2-inteligencia-financiera.md) | Costo promedio ponderado, márgenes/utilidad, períodos contables, OCR de facturas. |
| 3 | [Logística Predictiva](./3-logistica-predictiva.md) | Velocidad de vaciado, detección de quiebres, sugerencias de compra, rentabilidad por zona. |
| 4 | [Operación en Terreno: Cargas y Recargas](./4-operacion-terreno-recargas.md) | Órdenes de carga (estado BORRADOR), templates de recarga, OCR fotográfico de planillas. |
| 5 | [Caja y Rendiciones](./5-caja-rendiciones.md) | Transferencias a trabajadores, conciliación, compuerta de cierre de rendición. |
| 6 | [Auditoría e Integridad](./6-auditoria-integridad.md) | Rastro de auditoría EF, historial por entidad, chequeos de integridad, roles. |

## Contexto arquitectónico

Para el diagrama C4 y la visión general de la arquitectura (Blazor Server + WASM + microservicio Python),
ver [`architecture/overview.md`](./architecture/overview.md). Los ADR históricos están en
[`architecture/decisions/`](./architecture/decisions/).
