# Design v3 Integration — Ramas abandonadas

> Cambio: `design-v3-integration`
> Origen del sistema de diseño: `~/Downloads/VendingManager Design System (3)/`

## Resumen

Este documento registra las ramas de trabajo previas que quedan **abandonadas** con el inicio del plan `design-v3-integration`. El código de estas ramas no se integrará a `master`; el nuevo diseño industrial terminal las reemplaza por completo.

## Ramas abandonadas

| Rama | Ubicación | Motivo del abandono |
|------|-----------|---------------------|
| `experimental_UwU` | local + origin | Trabajo experimental de restyling visual anterior; queda supeditado por el plan design-v3. |
| `feat/fluentui-foundation` | origin | Primer PR de la migración de 17 PRs a FluentUI; queda supeditado por design-v3. |
| `feat/fluentui-composites-card` | origin | PR intermedio de la migración de 17 PRs a FluentUI; queda supeditado por design-v3. |
| `feat/fluentui-composites-datagrid` | origin | PR intermedio de la migración de 17 PRs a FluentUI; queda supeditado por design-v3. |

## Estrategia de cadena

Se adopta **`feature-branch-chain`** para la integración:

- Rama tracker (acumuladora, borrador, sin merge directo): `feature/design-v3-integration`
- Cada PR hijo se bifurca desde el PR hijo inmediatamente anterior.
- El PR0 es el primero de la cadena y apunta a `master`.

## Artefactos del plan design-v3

Los siguientes artefactos de Engram contienen la definición completa del cambio:

- Exploración: observación `#380`
- Propuesta: observación `#381`
- Especificación: observación `#382`
- Diseño: observación `#383`
- Tareas: observación `#384`

## Ramas fuera de alcance (permanecen activas)

Las siguientes ramas **no** se cierran ni se marcan como abandonadas; continúan su ciclo de vida normal:

- `feat/conciliacion-redesign-slice1`
- `feat/conciliacion-redesign-slice2`
- `feat/conciliacion-redesign-slice3`
- `feat/grafo-conciliacion`

Estas ramas son independientes del cambio `design-v3-integration` y permanecen activas.
