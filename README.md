# VendingManager 🏭

![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)
![Blazor](https://img.shields.io/badge/Blazor-WebAssembly-512BD4?logo=blazor)
![SQL Server](https://img.shields.io/badge/SQL_Server-2022-CC2927?logo=microsoft-sql-server)
![Python](https://img.shields.io/badge/Python-3.10-3776AB?logo=python)
![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?logo=docker)
![License](https://img.shields.io/badge/License-MIT-green)

> Plataforma integral para la gestión de negocios de máquinas expendedoras. Controlá inventario, finanzas, operaciones y analítica desde un solo lugar, con trazabilidad total y sincronización inteligente.

---

## 🎯 ¿Qué problema resuelve?

Gestionar un negocio de vending sin sistema es un caos: las ventas se anotan en papel, los costos se calculan en Excel, y la trazabilidad del dinero que entra y sale se pierde entre planillas. Cuando un trabajador rinde cuentas, no hay forma de conciliar rápido, y al cierre del mes nunca tenés certeza de si ganaste o perdiste.

**VendingManager automatiza todo ese proceso.** Conecta cada venta de cada máquina con tu caja, tu inventario y tus gastos, y te da _en tiempo real_ los números que importan. Además, **todas las noches el sistema sincroniza automáticamente las máquinas** con el portal OurVend — cero trabajo manual, cero olvidos.

El resultado: sabés exactamente cuánto ganó el negocio cada mes, con costos precisos porque el sistema guarda el valor de cada producto al momento exacto de cada venta. Sin planillas, sin errores, sin depender de la memoria de nadie.

---

## 🧱 Stack Tecnológico

| Capa | Tecnología |
|------|-----------|
| Backend | .NET 10, ASP.NET Core |
| Frontend | Blazor WebAssembly |
| Base de Datos | SQL Server 2022 |
| Scraping & OCR | Python, FastAPI, Playwright, Google Gemini |
| Infraestructura | Docker, Docker Compose |
| Arquitectura | Clean Architecture, Repository Pattern |
| Testing | xUnit, Moq, FluentAssertions |

---

## ⚡ Funcionalidades Principales

- 📡 **Sincronización Automática** — El sistema ingresa diariamente al portal OurVend, descarga los reportes de venta de todas las máquinas y los procesa. Detecta duplicados automáticamente y reintenta si hay fallos.

- 🤖 **OCR con Inteligencia Artificial** — Sacale una foto a la factura del proveedor o a la lista de carga manuscrita. Google Gemini extrae proveedor, productos, cantidades y precios en segundos. El sistema además reconoce códigos EAN y los asocia a tu catálogo automáticamente.

- 📈 **Rentabilidad en Tiempo Real** — El Estado de Resultados (P&L) se calcula automáticamente usando costos históricos reales. Cuando vendés un producto, el costo se congela al valor exacto de ese momento: ventas menos costo de venta, menos gastos variables y fijos. Sin promedios genéricos que distorsionan los números.

- 🔍 **Conciliación Transbank** — Subí el archivo de pagos y el sistema cruza cada pago con su venta. Si alguien compró varios productos en un solo pago, un algoritmo de _bundle matching_ encuentra la combinación exacta de ventas. Los pagos sin venta asociada quedan registrados para revisión: nada desaparece.

- 📊 **Analítica Predictiva** — Ranking ABC de productos, clasificación por rotación y margen, predicción de quiebres de stock basada en velocidad de venta real, y sugerencia de compras a 30 días priorizada por criticidad. Sabés qué productos te conviene mantener y cuáles no.

- 🔐 **Auditoría y Trazabilidad** — Cada creación, modificación o eliminación de cualquier entidad queda registrada automáticamente: quién lo hizo, cuándo, y qué cambió. Si alguien se equivoca, podés revertir cualquier registro a su estado anterior con un clic desde el panel de administración.

- 🚚 **Logística de Reposición** — Generá órdenes de carga desde plantillas inteligentes. Registrá qué llevaste y qué sobró. Al finalizar, el sistema descuenta de bodega, actualiza los slots de la máquina, y registra el costo en Caja automáticamente.

- 📋 **Plantillas de Recarga Inteligente** — Planificá ciclos de reposición con múltiples períodos. El sistema analiza velocidad de venta por slot, predice cuándo se va a vaciar cada producto y detecta slots que no vendieron nada. Cruzás datos entre todos los ciclos de una misma máquina.

---

## 🏗️ Arquitectura

```
┌──────────────────────────────────────────────────────────────┐
│                    VendingManager System                      │
│                                                               │
│  ┌──────────────────┐    ┌──────────────────────────────┐    │
│  │ Blazor WebAssembly │───▶│  ASP.NET Core Web API         │    │
│  │ (Frontend SPA)     │    │  Clean Architecture           │    │
│  └──────────────────┘    │  Controllers → Services → EF    │    │
│                          └───────────┬──────────────────┘    │
│                                      │                        │
│                          ┌───────────▼──────────────────┐    │
│                          │  SQL Server 2022             │    │
│                          │  (VendingDB)                  │    │
│                          └──────────────────────────────┘    │
│                                                               │
│  ┌──────────────────────────────────────────────────────┐    │
│  │  Python Scraper Service (FastAPI + Playwright)        │    │
│  │  Extracción OurVend + OCR facturas + OCR recarga     │    │
│  └──────────────────────────────────────────────────────┘    │
│                                                               │
│  ┌──────────────────────────────────────────────────────┐    │
│  │  AutomatedReportService (Background Worker)           │    │
│  │  Sincronización diaria automática                     │    │
│  └──────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────┘
         │                                      │
         ▼                                      ▼
┌─────────────────┐                  ┌─────────────────────┐
│   Usuario        │                  │  Google Gemini API   │
│   (Navegador)    │                  │  (OCR de facturas    │
└─────────────────┘                  │   y fotos de recarga) │
                                     └─────────────────────┘
```

### Patrones de Diseño

| Patrón | Aplicación |
|--------|-----------|
| Clean Architecture | Separación Core / Infrastructure / Presentation |
| Repository Pattern | Abstracción de acceso a datos |
| State Machine | Ciclos de vida de templates y transferencias |
| Interceptor Pattern | Auditoría automática transparente |
| Facade Pattern | Servicios compuestos con delegación |

---

## 📊 El Proyecto en Números

| Métrica | Valor |
|---------|-------|
| Pantallas | 24 |
| Endpoints API | 100+ |
| Entidades | 21 |
| Tests automatizados | 300+ |
| Tablas de auditoría | 11 |
| Entornos Docker | 3 (dev, test, prod) |
| Controladores | 15 |

---

## 🚀 Quick Start

```bash
# Requisitos: Docker
git clone https://github.com/MaximilianoVorphal/VendingManager.git
cd VendingManager
docker-compose up -d
```

La app corre en `http://localhost:8080`. Las migraciones de base de datos se aplican automáticamente.

---

## 📚 Documentación

- 📖 [Dominios de Negocio](docs/README.md) — 6 documentos que cubren cada área del sistema
- 🏗️ [Arquitectura](docs/arquitectura.md) — Diagramas C4, capas, patrones de diseño
- 🔌 [API Reference](docs/api.md) — Catálogo completo de endpoints REST
- 🗄️ [Modelo de Datos](docs/entidades.md) — Entidades, relaciones y decisiones de modelado
- ⚙️ [Guía de Desarrollo](docs/desarrollo.md) — Setup, convenciones y testing
- 🚀 [Guía de Despliegue](docs/despliegue.md) — Docker, entornos y health checks

---

## 📄 Licencia

MIT

---

> **Nota**: Este proyecto fue desarrollado como un sistema de gestión integral real para un negocio de vending, cubriendo desde la ingesta automática de datos hasta la auditoría financiera.
