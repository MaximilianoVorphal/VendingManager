# Guía Operativa — Flujo del Dinero

Guía práctica paso a paso. Qué hacer, en qué orden, y qué esperás ver en cada módulo.

---

## El dinero en dos velocidades

| Módulo | Responde a | Cuándo lo usás |
|--------|-----------|----------------|
| **Caja** | ¿Cuánta plata tengo? | A diario, para ver saldo y registrar gastos sueltos |
| **Conciliación** | ¿En qué gasté la plata que saqué? | Cuando un trabajador rinde cuentas |

**Regla de oro**: todo lo que pase por Conciliación **aparece automáticamente en Caja**. No tenés que registrar dos veces nada.

---

## Flujo semanal típico

### Lunes a Viernes — Operación diaria

```
1. CARGAR MÁQUINAS
   → /cargas
   → Creás orden de carga, registrás qué llevaste, qué sobró
   → Al finalizar: se descuenta de bodega y aparece en Caja como GASTO MERCADERIA

2. REGISTRAR GASTOS SUELTOS (si pagaste algo directo)
   → /caja → botón "Nuevo Movimiento"
   → Ej: pagaste internet del local, una comisión, un arriendo
   → Elegís categoría y monto
```

### Cuando un trabajador te manda las fotos

```
3. ABRIR CONCILIACIÓN
   → /conciliacion → seleccionás trabajador

   PASO 1 — TRANSFERENCIA
   → Registrás cuánta plata le diste
   → Automáticamente se crea un RETIRO en Caja (sale plata)

   PASO 2 — COMPRA (mercadería)
   → Factura del proveedor (productos para las máquinas)
   → Si la pagó de la transferencia: PagadaCaja = true
   → Sube el stock de bodega y actualiza costo promedio
   → Aparece en Caja como GASTO MERCADERIA (línea informativa)

   PASO 3 — GASTO (boletas)
   → Bencina, peaje, insumos que pagó el trabajador
   → Categorías rápidas: BENCINA, PEAJE, INSUMOS, MANTENCIÓN, OTRO
   → Aparece en Caja como GASTO, dentro de GastosFijos

   PASO 4 — CUADRAR
   → Transferido - Compras - Gastos = Diferencia
   → Si dio positivo: te debe plata
   → Si dio negativo: le debés plata

   PASO 5 — CERRAR
   → Bloquea la rendición, no se puede modificar más
```

---

## ¿Dónde aparece cada cosa en Caja?

| Lo que hiciste | Dónde lo ves en Caja | ¿Descuenta del EBIDTA? |
|---------------|---------------------|----------------------|
| **Transferencia** al trabajador | RETIRO (baja el saldo, no es gasto) | ❌ No |
| **Compra de mercadería** | GASTO MERCADERIA (línea informativa) | ❌ No — es inventario |
| **Boleta de bencina** | GASTO → GastosFijos (GASTOS GENERALES) | ✅ Sí |
| **Boleta de peaje** | GASTO → GastosVariables (PEAJES) | ✅ Sí |
| **Insumos** | GASTO → GastosVariables (INSUMOS) | ✅ Sí |
| **Mantención** | GASTO → GastosVariables (MANTENCION) | ✅ Sí |
| **Sueldo del trabajador** | GASTO → GastosFijos (SUELDOS) | ✅ Sí |
| **Arriendo / Internet** | GASTO → GastosFijos | ✅ Sí |

---

## Fin de mes — Cómo leer los números

### En Caja, los 4 KPIs del mes

```
VENTAS DEL MES       $2.500.000   ← lo que vendieron las máquinas
GASTOS TOTALES       $1.200.000   ← TODO lo que salió (bencina, peajes, arriendo, sueldos, mercadería...)
APORTES              $        0   ← plata que pusiste de tu bolsillo
DISPONIBLE EN CAJA   $1.300.000   ← Ventas + Aportes - Gastos
```

### El Estado de Resultados (el P&L)

```
VENTAS                              $2.500.000
− COSTO DE VENTA                    $1.100.000   ← costo de los productos que SÍ se vendieron
= MARGEN BRUTO                      $1.400.000

− Mermas                            $   50.000   ← productos vencidos, rotos
− Gastos Variables                   $  350.000   ← bencina, peajes, insumos, mantención
− Gastos Fijos                       $  600.000   ← arriendo, internet, SUELDOS, comisiones

= UTILIDAD OPERACIONAL (EBIDTA)     $  400.000   ← lo que realmente ganó el negocio
```

**El sueldo ya está descontado.** Cuando ves el EBIDTA de $400.000, el sueldo del trabajador ya fue restado dentro de Gastos Fijos. No tenés que restarlo después.

### Diferencia clave: Saldo de Caja ≠ EBIDTA

| Concepto | Qué mide |
|----------|----------|
| **Saldo de Caja** | Cuánta plata física tenés (incluye mercadería comprada, transferencias) |
| **EBIDTA** | Cuánto ganó realmente el negocio (solo ventas netas de costos y gastos reales) |

Ejemplo: si compraste $800.000 en mercadería este mes pero solo vendiste $500.000 de costo, el saldo de caja bajó $800.000 pero el EBIDTA solo refleja los $500.000 de costo de venta. Los otros $300.000 están en la bodega como inventario.

### Ejemplo concreto: comprar hoy, vender mañana

Comprás $500.000 en papas fritas el 28 de junio. Las máquinas las venden recién en julio.

**Junio — P&L**
```
VENTAS                              $2.000.000
− COSTO DE VENTA                    $  900.000   ← costo de lo vendido en junio
= MARGEN BRUTO                      $1.100.000
− Gastos Variables                   $  350.000
− Gastos Fijos                       $  600.000
= EBIDTA                             $  150.000
─────────────────────────────────────────────────
Gastos Mercadería (informativo)      $  500.000   ← las papas que compraste, no descuenta
```

Las papas de $500.000 **no tocaron el EBIDTA**. Están en la bodega como inventario. Lo único que bajó fue el saldo de caja (saliste de esa plata).

**Julio — P&L** (cuando se venden esas papas)
```
VENTAS                              $2.300.000   ← incluye la venta de las papas
− COSTO DE VENTA                    $1.100.000   ← AHORA entra el costo de esas papas
= MARGEN BRUTO                      $1.200.000
− Gastos Variables                   $  350.000
− Gastos Fijos                       $  600.000
= EBIDTA                             $  250.000
```

Recién en julio, cuando el producto se vendió, su costo impacta en el EBIDTA. Es el principio de **devengado**: el gasto se reconoce cuando el producto se vende, no cuando se compra.

**En criollo**: la plata que ponés en mercadería no es un gasto — es un intercambio (plata → inventario). El gasto real ocurre cuando el producto sale de la máquina y ya no lo tenés más.

---

## Orden correcto de trabajo

### Arranque del mes

1. **Configurar gastos fijos recurrentes** en Caja (arriendo, internet, sueldos…)
   → El sistema te va a alertar si falta registrar alguno

### Durante el mes

2. Cada vez que cargás máquinas → `/cargas`
3. Cada vez que un trabajador manda fotos → `/conciliacion`
4. Gastos directos que pagás vos → `/caja`

### Cierre de mes

5. Revisar que **todos los gastos fijos** estén registrados en Caja
6. Revisar que **todas las conciliaciones** estén cerradas
7. Ver el P&L en Caja → ese es tu resultado del mes
8. Si querés sacar plata (retiro de utilidades) → nuevo movimiento en Caja tipo RETIRO

---

## Preguntas frecuentes

### ¿Una factura de mercadería afecta mi ganancia del mes?

**No directamente.** La compra de mercadería baja el saldo de caja (saliste de plata), pero no es un gasto hasta que el producto se vende. El costo se reconoce en el momento de la venta (Costo de Venta), no en el momento de la compra.

### ¿Las boletas de bencina y peaje aparecen en Caja?

**Sí.** Cuando las registrás en Conciliación (Paso 3 — Gasto), automáticamente se crea un MovimientoCaja. Las ves en la lista de movimientos de Caja y en el desglose del P&L (GastosFijos para bencina, GastosVariables para peajes).

### ¿El sueldo se resta del EBIDTA o después?

**Antes.** El sueldo está dentro de GastosFijos. El EBIDTA que ves en el P&L ya tiene el sueldo descontado. No hay un paso adicional de "restar sueldo".

### ¿Qué pasa si el trabajador gastó menos de lo que le transferí?

La diferencia aparece positiva en el Paso 4 (Cuadrar). Esa plata **ya salió de Caja** (cuando hiciste la transferencia). Si el trabajador te la devuelve, registrás un APORTE en Caja para que vuelva a entrar.

### ¿Qué pasa si gastó más?

La diferencia aparece negativa. Le debés plata al trabajador. Podés hacer otra transferencia para cubrir la diferencia.
