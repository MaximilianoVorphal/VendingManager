# VendingManager — Lógica de Negocio

Guía completa de los conceptos financieros y operativos del sistema, en el contexto de un negocio de máquinas expendedoras.

---

## Los 3 pilares del negocio

### 🏦 CAJA (el corazón financiero)

La **billetera general** del negocio. Cada peso que entra o sale se registra como un **Movimiento de Caja**.

#### Los 4 KPIs del mes

| KPI | Qué mide | 
|-----|----------|
| **Ventas del Mes** | Plata que entró de las máquinas |
| **Gastos Totales** | Plata que salió para operar (bencina, peajes, mantenciones, arriendos, internet, comisiones) |
| **Aportes / Inyecciones** | Capital que metiste de tu bolsillo |
| **Disponible en Caja** | Lo que queda: Ventas + Aportes − Gastos |

Responde a la pregunta: **"¿Cuánta plata tengo?"**

#### Categorías de movimiento

| Categoría | Tipo | Qué es |
|-----------|------|--------|
| MERCADERIA | GASTO | Reposición de productos |
| LOGISTICA | GASTO | Bencina, fletes |
| PEAJES | GASTO | Peajes y estacionamiento |
| INSUMOS | GASTO | Limpieza, bolsas |
| MANTENCION | GASTO | Repuestos, reparaciones |
| INFRA | GASTO | Luz, agua, arriendo local |
| ARRIENDO_POS | GASTO | Alquiler terminal Transbank |
| INTERNET | GASTO | Planes de datos, chips |
| COMISIONES | GASTO | Comisiones bancarias |
| MERMA | GASTO | Productos vencidos, rotos, robados |
| APORTE | INGRESO | Capital que vos ponés |
| RETIRO | EGRESO | Retiro de utilidades, sueldos |

---

### 📊 ESTADO DE RESULTADOS (lo que ganás)

El cálculo completo de rentabilidad del negocio:

```
Ventas del mes
− Costo de Venta (lo que te costaron los productos vendidos)
= MARGEN BRUTO
− Mermas (productos vencidos/rotos)
− Gastos Variables (bencina, peajes, insumos, mantención)
− Gastos Fijos (arriendo, internet, luz, POS)
= UTILIDAD OPERACIONAL (EBITDA)
```

Responde a la pregunta: **"¿Cuánto gané?"**

#### Conceptos clave

- **Mercadería NO es gasto** — es inversión. El costo se reconoce recién cuando vendés el producto.
- **Costo de Venta** se congela al momento de la venta: si compraste papas a $500 y después a $600, la venta de hoy usa el costo del momento.
- **Margen Bruto** = Ventas − Costo de Venta. Es lo que ganás por vender, antes de pagar gastos operativos.
- **EBITDA** = Margen Bruto − Mermas − Gastos Variables − Gastos Fijos. Es lo que realmente ganó el negocio en el mes.
- **Capital Inmovilizado**: cuánta plata tenés parada en mercadería (bodega + máquinas).

---

### 📒 CONCILIACIÓN (justificar en qué usaste la plata)

El modelo nuevo, basado en **períodos contables** (reemplaza el modelo viejo de rendiciones por trabajador).

#### Conceptos

| Concepto | Qué es |
|----------|--------|
| **Período Contable** | Ventana de tiempo (ej: "Junio 2026", "Q2 2026") |
| **Transferencia** | Plata que sacaste de Caja para ese período |
| **Compra vinculada** | Mercadería comprada con esa transferencia |
| **Gasto vinculado** | Gasto pagado con esa transferencia |
| **Diferencia** | Lo transferido − lo gastado = lo que sobra/falta |
| **Cierre** | Bloquea el período cuando todo cuadra |

#### Flujo de trabajo

1. Creás un período (ej: "Junio 2026")
2. Le agregás **transferencias** (plata que sale de Caja)
3. Vinculás **compras** y **gastos** a esas transferencias
4. El panel de conciliación te muestra si **cuadra**
5. Cerrás el período cuando todo está conciliado

Responde a la pregunta: **"¿En qué usé la plata que saqué?"**

---

## 🔗 Cómo se conectan los conceptos

```
Ventas ──→ alimentan Caja (Ingresos)
Compras ──→ Stock Bodega + Costo Promedio + Movimiento Caja
Gastos ──→ Movimiento Caja
Transferencia ──→ Movimiento Caja (RETIRO) + Período Contable
Stock ──→ Máquinas (Slots) ──→ Templates Recarga ──→ Análisis Stockout
```

### Relación Caja ↔ Conciliación

Cuando creás una **Transferencia** en Contabilidad, automáticamente se genera un **Movimiento de Caja** tipo RETIRO:

```
Conciliación                        Caja
─────────────                         ────
Transferencia $100.000  ──→  RETIRO -$100.000
Compra $80.000 (productos)
Gasto $15.000 (bencina)
Diferencia $5.000 (sobró)
```

---

## Conceptos del negocio

### 1. Ventas

Cada vez que una máquina vende un producto, se registra: fecha, máquina, slot, producto, precio de venta, costo de venta (congelado), y estado de pago (Transbank).

Las ventas alimentan el **KPI de Ingresos** en Caja y son el punto de partida del **Margen Bruto**.

### 2. Compras / Mercadería

Cada factura de proveedor. Puede ser:
- **MERCADERÍA**: productos para vender → aumenta stock de bodega, recalcula costo promedio
- **GASTO GENERAL**: bencina, peajes, etc → genera movimiento en Caja

Se puede escanear con IA (OCR) para lectura automática. Se puede vincular a transferencias y períodos contables.

### 3. Stock / Inventario

Dos lugares:
- **Bodega**: inventario central
- **Máquinas (Slots)**: lo que ya está cargado en cada máquina

Cada producto tiene **costo promedio** (recalculado en cada compra) y **stock en bodega**. Cada slot tiene stock actual, capacidad máxima y stock mínimo para alertas.

### 4. Gastos Operativos (variables)

Dependen de cuánto operás: **bencina, peajes, insumos, repuestos, mantención**. Si no salís a recargar, no gastás.

### 5. Gastos Fijos (estructurales)

Están siempre, operes o no: **arriendo, luz, internet, chips, alquiler POS, comisiones**. Se configuran como **Gastos Recurrentes** y el sistema alerta si faltan registrar en el mes.

### 6. Transferencias

Plata que sale de Caja para un propósito. Ciclo de vida:
- **Pendiente** → recién creada
- **En Uso** → se están haciendo compras/gastos
- **Conciliado** → todo rendido y cuadra

### 7. Máquinas y Slots

Cada máquina tiene slots (posiciones físicas). Cada slot tiene asignado un producto, stock actual, precio de venta y capacidad.

### 8. Templates de Recarga

Planificación de ciclos de reposición. Capturan el estado inicial de cada slot para análisis de **stockout** (detección de quiebres de stock y costo de oportunidad).

---

## 📄 Páginas del sistema

| Página | Para qué |
|--------|----------|
| **Home** | Dashboard: ventas diarias/semanales/mensuales, stock crítico |
| **Caja** | Tesorería: KPIs, movimientos del mes, estado de resultados, gastos fijos |
| **Compras** | Historial y gestión de facturas |
| **Nueva Compra** | Registrar factura con escaneo IA |
| **Conciliación** | Períodos contables: transferencias, compras, gastos, cierre |
| **Rendiciones** | Modelo viejo de conciliación (por trabajador) |
| **Inventario** | Stock en bodega, costos promedio |
| **Gestión de Máquinas** | Máquinas y sus slots |
| **Templates Recarga** | Planificación de reposición |
| **Análisis de Ventas** | Ranking ABC, tendencias |

---

## Flujo general

```
                    ┌──────────────────────────────────────┐
                    │       VENTAS (OurVend + Transbank)   │
                    │  Cada venta: máquina, slot,          │
                    │  producto, precio, costo congelado   │
                    └──────────────┬───────────────────────┘
                                   │ alimenta
                    ┌──────────────▼───────────────────────┐
                    │          CAJA (Tesorería)            │
                    │  4 KPIs │ Movimientos │ Resultados   │
                    │  Ventas+Aportes−Gastos = Saldo      │
                    └──────┬──────────────┬────────────────┘
                           │              │
              ┌────────────▼──┐    ┌──────▼──────────────┐
              │   COMPRAS     │    │  GASTOS OPERATIVOS  │
              │  Mercadería   │    │  Bencina, peajes,   │
              │  + Gasto Gral │    │  mantenciones, etc. │
              └──────┬────────┘    └─────────────────────┘
                     │
         ┌───────────▼───────────┐
         │   STOCK / INVENTARIO  │
         │  Bodega ← → Máquinas  │
         │  (Slots configurados) │
         └───────────┬───────────┘
                     │
         ┌───────────▼───────────┐
         │  TEMPLATES DE RECARGA │
         │  Planificación +      │
         │  Análisis de stockout │
         └───────────────────────┘

   CONCILIACIÓN (modelo nuevo por período):
   Período Contable → Transferencias → Compras + Gastos → Cuadre

   CONCILIACIÓN (modelo viejo por trabajador):
   Rendición → Transferencias → Compras + Gastos → Cuadre
```
