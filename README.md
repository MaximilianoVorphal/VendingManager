# VendingManager 🚀

VendingManager es una plataforma centralizada para la gestión de inventario, cajas, y reportes automatizados de máquinas expendedoras. Incluye soporte para extraer automáticamente los registros de venta y carga de *OurVend* mediante un Scraper en Python.

## Estructura del Proyecto

Este proyecto sigue una arquitectura separada (Polyglot) para aprovechar lo mejor de varios mundos:

* **`.NET (Blazor Web App)`**: Ubicado en `/src`. Contiene el API backend (uso de Clean Architecture), interfaces compartidas (`Shared`) y la UI principal en Blazor WebAssembly (`Web`).
* **`Python (Playwright Scraper)`**: Ubicado en `/services/scraper`. Un microservicio encargado de extraer los datos desde portales externos.

## Documentación Técnica (Hub)

Toda la documentación arquitectónica y técnica ha sido estructurada en la carpeta `/docs`. Por favor consulta los siguientes recursos:

* 🏗️ [Overview de Arquitectura (C4 y Diagramas)](docs/architecture/overview.md)
* 💡 [Architecture Decision Records (ADRs)](docs/architecture/decisions/)
* ⚙️ [Guía de Configuración Local (Setup)](docs/development/setup.md)

## Requisitos Previos

* Docker y Docker Compose
* .NET 10 SDK (o el entorno especificado en los proyectos)
* Python 3.10+ (si decides correr el scraper sin Docker)

Para más detalles, ve a la [Guía de Setup](docs/development/setup.md).
