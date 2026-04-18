# ADR 012: Reversión de Stock y Recalculo de CPP en Compras

## Estado
Aceptado - 2026-04-18

## Contexto
El sistema permite registrar facturas que impactan el stock y el costo promedio (CPP). Hasta ahora, solo se permitía la creación de estos registros. El usuario requiere poder editar y eliminar facturas completas. 

El desafío técnico es mantener la integridad del inventario y el costo financiero al eliminar registros históricos sin tener que recalcular cada venta individual (lo cual sería ineficiente).

## Decisión
Se implementará una **matemática de reversión basada en el valor total del pool**, tanto para la eliminación como para la edición de facturas.

### Lógica de Eliminación:
Para cada producto afectado por una eliminación de $Q$ unidades a un costo unitario $C$:
1. Recuperar Stock actual ($S$) y CPP actual ($P$).
2. Valor Inventario Actual $V_{actual} = S \times P$.
3. Valor a Revertir $V_{reversion} = Q \times C$.
4. Nuevo Stock $S_{nuevo} = S - Q$.
5. Si $S_{nuevo} > 0$, entonces $P_{nuevo} = (V_{actual} - V_{reversion}) / S_{nuevo}$.
6. Si $S_{nuevo} \le 0$, entonces $S_{nuevo} = 0$ y $P_{nuevo} = 0$.
7. Si el resultado de la resta de valores da negativo por redondeos, se capará el CPP a 0.

### Lógica de Edición:
La edición se tratará como una **Reversión de la Factura Antigua** seguida de una **Aplicación de la Factura Nueva** en una sola transacción (`TransactionScope`).

## Consecuencias
- **Precisión**: El CPP regresará a su estado anterior de forma exacta si no hubo otras compras en medio. Si hubo compras posteriores, el CPP se ajustará proporcionalmente retirando el peso de la factura borrada.
- **Trazabilidad**: Los movimientos de Caja asociados serán eliminados/actualizados automáticamente para que los reportes de egresos coincidan con las facturas vigentes.
- **Riesgo**: Si un usuario borra una factura antigua de productos que ya se agotaron, el stock podría quedar en 0 pero con un CPP inconsistente; sin embargo, para el giro de negocio Vending esto es aceptable frente a no poder borrar errores.
