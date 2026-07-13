# Documentación Viva — VendingManager

Documentación por **dominio de negocio**, basada en el estado actual del código. Cada archivo sigue la
misma estructura: Resumen de Negocio → Entidades Clave → Reglas de Negocio (crítico) → Flujo Técnico →
Riesgos y Deuda Técnica.

> Principio: no se inventan reglas. Cada valor hardcodeado, fórmula o divergencia entre implementaciones
> está citado con su `archivo:línea`.

## Índice de dominios

| # | Documento | De qué trata |
| --- | --- | --- |
| 1 | [Pipeline de Ingesta](./1-pipeline-ingesta.md) | Scraping stealth de OurVend, polling cada ~2h, circuit breaker, importación y deduplicación de ventas. |
| 2 | [Inteligencia Financiera](./2-inteligencia-financiera.md) | Costo promedio ponderado, márgenes/utilidad, períodos contables, OCR de facturas. **EBITDA/depreciación eliminado.** |
| 3 | [Logística Predictiva](./3-logistica-predictiva.md) | Velocidad de vaciado ×14h, detección de quiebres, sugerencias de compra, rentabilidad por zona. |
| 4 | [Operación en Terreno: Cargas y Recargas](./4-operacion-terreno-recargas.md) | Órdenes de carga (estado BORRADOR), templates de recarga, OCR fotográfico de planillas. |
| 5 | [Caja y Rendiciones](./5-caja-rendiciones.md) | Transferencias a trabajadores, conciliación, compuerta de cierre de rendición. |
| 6 | [Auditoría e Integridad](./6-auditoria-integridad.md) | Rastro de auditoría EF, historial por entidad, 6 chequeos de integridad, roles. |

## Contexto arquitectónico

Para el diagrama C4 y la visión general de la arquitectura (Blazor Server + WASM + microservicio Python),
ver [`architecture/overview.md`](./architecture/overview.md). Los ADR históricos están en
[`architecture/decisions/`](./architecture/decisions/).

## Hallazgos transversales notables

- **EBITDA/depreciación fue eliminado** (migración `CleanupEbitda`, 13-jul-2026). Solo queda la etiqueta
  cosmética en la UI. Ver doc 2, §6.
- **Varios endpoints sin `[Authorize]`**: `OrdenCargaController`, `TemplateRecargaController`,
  `InformesController`, `MaquinasController`, `ProductosController`, y la página `AnalisisProductos.razor`.
- **Dos definiciones divergentes de "gasto real"** entre `RendicionService` e `IntegrityCheckService`
  (docs 5 y 6).
- **Compensaciones de zona horaria hardcodeadas** (offsets `-11/+1/-14`) por un desfase de 12h entre
  máquina y Chile (doc 1).
