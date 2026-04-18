# ADR 010: Sincronización Histórica de Ventas a través de Templates de Recarga

## Estado
Aceptado

## Fecha
17 de Abril de 2026

## Contexto
Dado que las máquinas expendedoras pueden tener temporalmente configurados productos de forma errónea (por ejemplo, slot físico 10 tiene Papas fritas, pero lógicamente se le asignaron Galletas en el registro), la posterior venta se registra bajo el ProductoId equivocado. Esto generaba que la contabilidad y las analíticas de "Stockouts" perdieran de vista qué producto efectivamente se agotó.

El sistema de "Templates de Recarga" cuenta con una herramienta muy robusta de `PeriodoRecarga` (una ventana de inicio y fin de una reposición) y `SnapshotSlot` (la foto de qué tenía la máquina en ese periodo). 

Para corregir los errores históricos causados por la máquina de manera central y con registro, se requiere la capacidad de utilizar el *Snapshot* como fuente de veracidad retroactiva para los modelos de `Venta`.

## Decisión
Se ha introducido la funcionalidad *SyncVentasWithTemplateAsync* en la capa de Servicios y un nuevo endpoint en `TemplateRecargaController` que permite emparejar (matchear) y reescribir los históricos. 

1. El usuario tiene la opción de actualizar solo `ProductoId` o también el `CostoVenta` original registrado, reemplazándolo por el costo del producto actual (`Producto.CostoPromedio`). 
2. Esta acción es atómica, evalúa las relaciones *Many-to-One* entre Ventas y Periodos, reasignando sus foreignkeys sin alterar la `FechaLocal` ni el `MaquinaId` ni la identificación única `IdOrdenMaquina`.
3. Se integrará un UI de mitigación de riesgo (Warning Modal) directo en Blazor antes del borrado o sobreescritura de histórico.

## Consecuencias Positivas
- Ahora es posible enmendar de manera rápida y masiva errores logísticos en Ventas.
- El panel de *Stockout Dashboard* puede ser recalibrado inmediatamente después de hacer la corrección.

## Consecuencias Negativas o Riesgos
- Alterar datos preteritos siempre trae el riesgo implícito de romper registros de caja diarios históricos si se abusa. Sin embargo, al estar delimitado por la ventana de tiempo del Periodo, el "Blast Radius" (radio de impacto) es pequeño. Además, la funcionalidad está intrínsecamente restringida por el atributo `[Authorize(Roles = Roles.Admin)]`.
