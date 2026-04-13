# 1. Registro de Decisiones de Arquitectura (ADRs)

Date: 2026-04-05

## Status

Accepted

## Context

Para prevenir la pérdida de contexto sobre "por qué" el código fue escrito de esta manera a futuro, un proyecto empresarial necesita un método para dejar constancia de las decisiones críticas. Modificar cosas importantes (como la separación entre Blazor Server y WASM, o la razón por la que usamos Python en un contenedor aparte) queda olvidado si solo está en la cabeza del desarrollador original.

## Decision

Adoptaremos la metodología de **Architecture Decision Records (ADR)** de acuerdo al estándar de Michael Nygard. Cada vez que hagamos un cambio grande de arquitectura, añadiremos un pequeño archivo Markdown numerado (`0002-nombre-del-cambio.md`) documentando el contexto, la decisión planteada y sus consecuencias.

## Consequences

- Los desarrolladores (y el propio autor a futuro) podrán consultar `/docs/architecture/decisions` para entender por qué ciertas decisiones fueron tomadas.
- Tendremos que escribir un archivo corto de texto de manera responsable antes de implementar una refactorización masiva.
