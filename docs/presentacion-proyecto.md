# VendingManager — Presentación del Proyecto

Plataforma centralizada para la gestión integral de un negocio de máquinas expendedoras. Más de 30 tablas de datos modeladas específicamente para vending, 100+ endpoints de API, 24 pantallas de operación, y 300+ tests automatizados. Controlá inventario, finanzas, operaciones, personal y analítica desde un solo lugar, con trazabilidad total y sincronización inteligente.

---

## ¿Qué problema resuelve?

Gestionar un negocio de vending sin sistema es un caos: anotás ventas en papel, calculás costos en Excel, perdés trazabilidad de la plata que entra y sale, no sabés si ganaste o perdiste en el mes, y cuando un trabajador rinde cuentas no tenés forma de conciliar rápido.

**VendingManager automatiza todo eso.** Conecta cada venta de cada máquina con tu caja, tu inventario y tus gastos, y te da _en tiempo real_ los números que importan. Además, **todas las noches a las 23:00 el sistema sincroniza automáticamente todas las máquinas** con el portal OurVend — cero trabajo manual, cero olvidos.

---

## Funcionalidades principales

### 📊 Dashboard

Ventas diarias, semanales y mensuales de un vistazo. Alertas de stock crítico. Análisis de categorías de producto. Todo lo que necesitás para arrancar el día sabiendo cómo viene la operación.

---

### 🏦 Caja — Tesorería

El corazón financiero. Cuatro KPIs siempre actualizados:

| KPI | Qué responde |
|-----|-------------|
| Ventas del Mes | ¿Cuánto entró de las máquinas? |
| Gastos Totales | ¿Cuánto salió para operar? |
| Aportes / Inyecciones | ¿Cuánto capital puse? |
| Disponible en Caja | ¿Cuánta plata tengo? |

Registrá cualquier movimiento con 14 categorías predefinidas: mercadería, bencina, peajes, insumos, mantención, infraestructura, arriendo POS, internet, comisiones, merma, sueldos, aportes, retiros y gastos generales. Cada movimiento puede tener comprobante con foto. Exportación a Excel de todo el mes con un clic. Si el mes está cerrado, el sistema bloquea modificaciones automáticamente.

---

### 📈 Estado de Resultados (P&L)

El cálculo automático de rentabilidad, usando **costos históricos reales** (no promedios genéricos):

```
Ventas − Costo de Venta (congelado al momento de la venta) = Margen Bruto
Margen Bruto − Mermas − Gastos Variables − Gastos Fijos = EBITDA (Utilidad Operacional)
```

**Sabés exactamente cuánto ganó el negocio cada mes**, con costos precisos porque el sistema guarda el costo de cada producto al momento exacto de cada venta. Sin planillas, sin errores.

---

### 🛒 Compras

Registrá cada factura de proveedor. El sistema:

- **Desglosa packs automáticamente**: si comprás un pack de 12 unidades, lo convierte a unidades individuales con su costo real
- **Recalcula costos históricos**: cada producto mantiene una línea de tiempo de costos con fecha efectiva. Cuando vendés, el costo se congela al valor exacto de ese momento — no usa promedios genéricos que distorsionan la rentabilidad
- **Soporta OCR con IA**: sacale una foto a la factura con el celular. En segundos, Google Gemini extrae proveedor, número de documento, fecha, productos, cantidades y precios. El sistema además reconoce automáticamente los códigos EAN y los asocia a tus productos del catálogo
- **Diferencia mercadería de gasto general**: bencina, peajes y otros gastos en la misma pantalla, cada uno con su categoría contable
- Se puede **vincular a transferencias** para conciliación

---

### 📦 Inventario y EAN

- Stock en bodega con costos promedio actualizados automáticamente
- **Sistema EAN/SKU con auto-aprendizaje**: cada vez que ingresás una compra, el sistema aprende la relación entre código de barras y producto. La próxima vez que escanees ese EAN, ya sabe qué producto es
- Importación y exportación masiva desde Excel
- Los ajustes de stock están **bloqueados por diseño**: toda entrada y salida de mercadería debe pasar por Compras u Órdenes de Carga, garantizando trazabilidad total

---

### 🏭 Gestión de Máquinas y Slots

Cada máquina expendedora, con sus slots (posiciones físicas), productos asignados, precios y stock actual. Sabés qué hay en cada espiral de cada máquina sin tener que ir a mirar.

- **Acciones masivas sobre slots**: recargar, vaciar o intercambiar productos entre slots con detección automática de colisiones
- **Swap inteligente**: si movés un producto a un slot ocupado, el sistema intercambia automáticamente

---

### 🔄 Templates de Recarga

Planificación inteligente de ciclos de reposición:

- Creás un **Template** para una máquina, con múltiples **Períodos** (fechas de recarga)
- Cada período captura una **foto del estado de los slots** antes de la recarga (`SnapshotSlot`)
- El sistema analiza automáticamente:
  - **Velocidad de venta por slot** (unidades/día)
  - **Días hasta quiebre de stock** (cuándo se va a vaciar)
  - **Detección de slots muertos** (slots que no vendieron nada en el período)
  - **Porcentaje de llenado** (cuánto stock tenés vs cuándo lo recargaste)
- **Fotos por período**: podés subir foto de la guía de carga y foto para OCR
- **Ciclo de vida**: Pendiente → Terminado, con posibilidad de reabrir
- El análisis cruza datos **entre todos los templates de una misma máquina** (cross-template chain)

---

### 🚚 Órdenes de Carga

Logística de reposición:

- Generá órdenes desde templates o sugerencias del sistema
- Registrá qué llevaste y qué sobró
- **OCR desde foto de recarga**: sacale una foto a la lista de carga manuscrita y Gemini la interpreta automáticamente
- Al finalizar: descuenta de bodega, actualiza slots de la máquina y registra el costo en Caja
- Exportación a Excel de sugerencias de carga y consolidados

---

### 📒 Conciliación — Doble modelo

#### Modelo nuevo: Períodos Contables

El sistema más robusto para rendir cuentas:

1. Creás un **Período Contable** (ej: "Junio 2026", "Q2 2026")
2. Registrás **Transferencias** (plata que sale de Caja). Automáticamente se genera un RETIRO en Caja
3. Vinculás **Compras** (mercadería) y **Gastos** (bencina, peajes, insumos) a esa transferencia
4. El panel de conciliación te muestra si **cuadra** o hay diferencia
5. **Cerrás** el período cuando todo está conciliado (bloquea modificaciones)

#### Modelo viejo: Rendiciones por trabajador

Para compatibilidad con el flujo anterior. Creás una rendición, le asignás transferencias, compras y gastos, y la cerrás cuando cuadra. Ambos modelos conviven.

**Regla de oro**: todo lo que registrás en Conciliación aparece automáticamente en Caja. No se registra nada dos veces.

---

### 💰 Gastos Recurrentes

Configurá los gastos fijos una sola vez (arriendo, internet, sueldos, comisiones, alquiler POS):

- El sistema te **alerta si falta registrar** alguno en el mes
- **Aplicación con un clic**: seleccionás un gasto recurrente y automáticamente genera el Movimiento de Caja del mes
- **Aplicación masiva**: un botón para aplicar todos los pendientes del mes de una vez

---

### 📡 Sincronización Automática

- **Scraper OurVend**: cada noche a las 23:00, el sistema ingresa automáticamente al portal OurVend, descarga los reportes de venta de todas las máquinas, y los procesa. Detecta duplicados (por número de orden o por máquina+fecha+slot+precio) para no contar dos veces la misma venta. Si una máquina tiene una configuración de horario especial, ajusta automáticamente la zona horaria
- **Conciliación Transbank**: cuando subís el archivo de pagos de Transbank, el sistema cruza cada pago con su venta. Si una persona compró varios productos en un solo pago (carrito), el sistema lo detecta con un algoritmo de **bundle matching** que busca la combinación exacta de ventas que suman el monto del pago
- **Ventas fantasma**: cuando Transbank reporta un pago sin venta asociada, el sistema no lo pierde — crea un registro `TB-SIN-VENTA` para que sepas que hubo un cobro que necesita revisión. Nada desaparece
- **Reintento automático**: si la sincronización falla una noche, el sistema reintenta automáticamente al día siguiente

---

### 📊 Análisis Avanzado

| Análisis | Qué te dice |
|----------|------------|
| **Ranking ABC** | Clasifica productos en A (80% ventas), B (siguiente 15%), C (resto 5%) |
| **Clasificación Estrella/Vaca/Incógnita/Cacho** | Cruza rotación con margen para identificar qué productos te conviene mantener y cuáles no |
| **Análisis de Stockout** | Cuánta plata perdiste por tener slots vacíos, incluyendo detección de slots muertos |
| **Predicción de stockout** | Días estimados hasta que un slot se queda sin producto, basado en velocidad de venta real |
| **Sugerencia de compras** | Qué necesitás comprar para los próximos 30 días, priorizado por criticidad |
| **Análisis por categoría** | Rendimiento agrupado por tipo de producto (snacks, bebidas, golosinas, etc.) |

---

### 🔐 Seguridad y Control

- **Gestión de usuarios con roles**: Admin (acceso total a 24 pantallas, incluyendo administración de usuarios, auditoría y configuración) y Usuario (operación diaria: caja, compras, ventas, cargas). Contraseñas protegidas con BCrypt — ni siquiera el administrador del sistema puede verlas
- **Protección contra fuerza bruta**: el login está limitado a 5 intentos por minuto. Si alguien intenta adivinar contraseñas, el sistema responde con un delay aleatorio para frustrar ataques automatizados
- **Auditoría total**: cada creación, modificación o eliminación de cualquier entidad — productos, ventas, máquinas, movimientos de caja, usuarios — queda registrada automáticamente con quién lo hizo, cuándo y qué cambió. Son 11 tablas de historia que guardan el estado anterior de cada registro
- **Rollback**: si alguien se equivocó (borró una compra, modificó un precio mal), podés revertir cualquier entidad a su estado anterior con un clic desde el panel de administración. El sistema restaura el registro exactamente como estaba
- **Monitoreo 24/7**: el sistema expone endpoints de health check que te permiten saber en todo momento si la aplicación está viva (`/health`) y si la base de datos responde (`/health/db`). Ideal para integrar con herramientas de monitoreo como UptimeRobot o Grafana
- **300+ tests automatizados**: el proyecto incluye tests unitarios y de integración que validan que los módulos críticos — caja, compras, OCR, matching de productos, middleware de errores, interceptor de auditoría — funcionan correctamente. Cada cambio se puede verificar antes de salir a producción

---

## Stack tecnológico (resumen)

| Componente | Tecnología |
|-----------|-----------|
| Backend API | .NET 10 (ASP.NET Core) |
| Frontend | Blazor WebAssembly |
| Base de Datos | SQL Server 2022 |
| Scraper / OCR | Python (FastAPI + Playwright + Google Gemini) |
| Infraestructura | Docker + Docker Compose (3 entornos: dev, test, prod) |
| Arquitectura | Clean Architecture + Repository Pattern |
| Logging | Serilog (JSON estructurado) |
| Documentación API | Scalar (OpenAPI) |

El sistema corre completamente contenerizado. Se levanta con un solo comando.

---

## Lo que incluye este proyecto

| Componente | Estado | Detalle |
|-----------|--------|---------|
| Aplicación web completa | ✅ Funcional | 24 pantallas de operación con estados de carga, error y vacío |
| API REST | ✅ Funcional | 15 controladores, +100 endpoints documentados |
| Scraper OurVend | ✅ Funcional | Extracción automática con Playwright, reintento ante fallos |
| OCR con IA | ✅ Funcional | Facturas + fotos de recarga vía Google Gemini |
| Conciliación | ✅ Funcional | Doble modelo: períodos contables + rendiciones por trabajador |
| Templates de recarga | ✅ Funcional | State machine Pendiente→Terminado, cross-template chain analysis |
| Dashboard + KPIs | ✅ Funcional | Cache de 60 segundos, gráficos Chart.js, KPI cards |
| Gestión EAN/SKU | ✅ Funcional | Auto-aprendizaje desde compras, búsqueda por código de barras |
| Costos históricos | ✅ Funcional | Línea de tiempo por producto, costo congelado al vender |
| Auditoría + Rollback | ✅ Funcional | 11 tablas de historia, restauración de entidades con un clic |
| Sync automático diario | ✅ Funcional | 23:00 todas las máquinas, reintento automático |
| Usuarios con roles | ✅ Funcional | Admin + User, BCrypt, rate limiting en login |
| Exportación Excel | ✅ Funcional | Caja, ventas, compras sugeridas, cargas, catálogo |
| Docker (3 entornos) | ✅ Funcional | Producción (puerto 8080), desarrollo (8081), testing (8082) |
| Tests automatizados | ✅ Incluidos | +300 tests unitarios y de integración |
| Documentación | ✅ Completa | Manual técnico 1200+ líneas, guías de negocio y operación |

---

## ¿Qué necesitás para operarlo?

**Requisitos mínimos**:
- Un servidor Linux o Windows con Docker instalado
- 4 variables de entorno en un archivo `.env`:
  - `MSSQL_SA_PASSWORD` — contraseña de la base de datos
  - `OURVEND_USER` y `OURVEND_PASS` — credenciales del portal OurVend
  - `GEMINI_API_KEY` — clave de Google Gemini para el OCR

**Despliegue** — un solo comando:
```bash
docker-compose up -d
```

Esto levanta tres contenedores: la aplicación web (puerto 8080), la base de datos SQL Server (1433), y el microservicio de scraping (8000). Las migraciones de base de datos se aplican solas al iniciar — no necesitás ejecutar scripts manualmente.

**Entornos incluidos**:
| Entorno | Puerto web | Base de datos | Para qué |
|---------|-----------|---------------|----------|
| Producción | 8080 | VendingDB | Operación real |
| Desarrollo | 8081 | VendingDB_Dev | Probar cambios sin afectar producción |
| Testing | 8082 | VendingDB_Test | Pruebas aisladas |

**Monitoreo**: el sistema expone endpoints de salud en `/health` (¿está viva la app?) y `/health/db` (¿responde la base de datos?). Compatible con cualquier herramienta de monitoreo externa.

---

## Resumen

**VendingManager** es un sistema completo, probado y documentado para gestionar un negocio de máquinas expendedoras de punta a punta. Desde que entra la mercadería hasta que sale la utilidad del mes — pasando por cada recarga, cada conciliación, cada gasto recurrente y cada sincronización automática — todo trazado, todo automático, todo en un solo lugar.

No es un prototipo: tiene costos históricos reales, OCR con IA, análisis predictivo de stockout, rollback transaccional, y sincronización nocturna automática. Si tu negocio de vending creció más allá del Excel, esta es la plataforma que necesitás.

---

> **Documentación técnica detallada**: consultá [`manual-tecnico.md`](manual-tecnico.md) para arquitectura, stack completo, API reference, despliegue y desarrollo.
