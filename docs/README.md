# Documentación — VendingManager

> 📖 Este directorio es la fuente canónica de documentación del proyecto.
> Cualquier otra copia es derivada y puede estar desactualizada.
>
> **Política de mantenimiento:**
> - `feat`: si cambia comportamiento visible, el doc de dominio correspondiente se actualiza.
> - `refactor`: solo si cambia nombres de clases/servicios referenciados en docs.
> - `docs`: cambios a docs son primer ciudadano, no afterthought.
> - `api.md` debe mantenerse sincronizado con los atributos `[Authorize]` de cada controller.

Documentación completa del sistema, organizada por dominio de negocio y área técnica.

## 🧭 ¿Por dónde empezar?

| Si querés... | Empezá por |
|--------------|-----------|
| Entender qué hace el sistema y por qué | [README principal](../README.md) y luego los dominios de negocio (01 → 06) |
| Evaluar el diseño técnico | [Arquitectura](./arquitectura.md) → [Modelo de Datos](./entidades.md) |
| Levantar el proyecto y correrlo | Quick Start del [README](../README.md#quick-start) → [Guía de Desarrollo](./desarrollo.md) |
| Explorar la API REST | [API Reference](./api.md) |

---

## 📖 Dominios de Negocio

| # | Documento | De qué trata |
|---|-----------|-------------|
| 01 | [Ingesta de Ventas](./01-ingesta-ventas.md) | Scraping automatizado de OurVend, circuit breaker, importación y deduplicación de ventas |
| 02 | [Finanzas](./02-finanzas.md) | Costo promedio ponderado, márgenes, períodos contables, OCR de facturas |
| 03 | [Logística Predictiva](./03-logistica.md) | Velocidad de vaciado, detección de quiebres, sugerencias de compra, rentabilidad por zona |
| 04 | [Operación en Terreno](./04-operacion-terreno.md) | Órdenes de carga, templates de recarga, OCR fotográfico de planillas |
| 05 | [Caja y Rendiciones](./05-caja-rendiciones.md) | Transferencias, conciliación, cierre de períodos contables |
| 06 | [Auditoría e Integridad](./06-auditoria.md) | Trazabilidad automática, historial por entidad, chequeos de integridad, roles |

---

## 🏗️ Documentación Técnica

| Documento | Contenido |
|-----------|----------|
| [Arquitectura](./arquitectura.md) | Visión general, diagramas C4, capas, patrones de diseño, stack tecnológico |
| [API Reference](./api.md) | Catálogo completo de endpoints REST con 19 controladores |
| [Modelo de Datos](./entidades.md) | Entidades, relaciones, tablas históricas, decisiones de modelado |
| [Guía de Desarrollo](./desarrollo.md) | Setup local, estructura del proyecto, convenciones, testing |
| [Guía de Despliegue](./despliegue.md) | Docker, entornos, health checks, variables de entorno |
