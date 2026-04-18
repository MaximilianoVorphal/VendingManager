# ADR 002: Desacoplamiento Híbrido del Módulo de Compras (Gastos Generales)

## Estado
Aceptado - 2026-04-18

## Contexto
El módulo de "Compras" originalmente nació como una vía exclusiva para registrar la entrada física de inventario (Stock de Máquinas o Bodega). Por ende, cada `Compra` generaba `DetalleCompra` que obligatoriamente apuntaba mediante Foreign Key (FK) a `ProductoId`.

Sin embargo, el negocio requiere centralizar todos los egresos y la ingesta de facturas contables del negocio en un solo lugar. Gastos como "Bencina", "Peajes" u "Otras operaciones" generan facturas y boletas reales que debían cargarse al sistema vía OCR o manual, pero el sistema fallaba porque "Bencina" no es un producto físico vendible ni debe sumar stock.

## Decisión
Se decidió **flexibilizar el Módulo de Compras** en vez de crear una tabla nueva paralela (ej. `GastosExternos`). De esta forma, el Dashboard de Finanzas sigue viendo `Compras` como la raíz de todos los egresos contables documentados, pero a nivel esquema de base de datos se alteran las restricciones.

Acciones técnicas:
1. `DetalleCompra.ProductoId` pasa a ser nullable (`int?`).
2. Se introduce la propiedad `DetalleCompra.DescripcionItem` para que gastos sin producto tengan una glosa identificable.
3. Se introduce la propiedad `Compra.TipoFactura` (string) para que el frontend clasifique rápidamente si es `MERCADERIA` o `GASTO_GENERAL`.

## Consecuencias Positivas
- No se rompe el flujo contable subyacente. El OCR de Inteligencia Artificial podrá usarse de igual forma sobre boletas de Homecenter, Estacionamientos o Gasolineras. 
- La tabla de Caja (`MovimientoCaja`) se acopla inmediatamente ya que está diseñada para tolerar strings abstractos en su Categoría.

## Consecuencias Negativas
- EF Core ya no forzará la integridad relacional de todos los elementos; dependerá de la capa Lógica (`CompraService.cs`) discernir cuándo inyectar el Stock al Inventario vs cuándo ignorarlo basado en la nulidad del `ProductoId`.
