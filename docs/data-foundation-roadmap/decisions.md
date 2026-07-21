# Registro de Decisiones — Data Foundation Roadmap

Decisiones arquitectónicas, metodológicas y de política que gobiernan el roadmap
de fundación de datos. Cada entrada documenta qué se decidió, por qué, qué
afirmaciones habilita, cuáles prohíbe, y bajo qué condiciones se invalida.

Las referencias Engram son identificadores internos del sistema de memoria
persistente del proyecto `vendingmanager`, no enlaces web.

---

## Índice de decisiones

| # | Decisión | Ref. Engram | Estado |
|---|----------|-------------|--------|
| D01 | Denominador G1-A = filas devueltas por el reporte | #1203 | Vigente |
| D02 | Selección de fuente y alineación de eventos como contratos separados | — | Vigente |
| D03 | Comportamiento horario multi-factorial, scope por máquina | #1196, #1243 | Vigente |
| D04 | G2 es aprobación operacional scoped; causa física desconocida | #1196 | Vigente |
| D05 | `Fecha de movimiento` TB es referencia operacional, no verdad absoluta | #1193 | Vigente |
| D06 | Boundary Policy v1: conceptual, no implementada en código | #1212 | Vigente |
| D07 | Fórmula de inventario por conservación; overflow preservado | #1208 | Vigente |
| D08 | `StockActual` es derivado; fotos no prueban residual cuantitativo | #1208 | Vigente |
| D09 | Siguiente snapshot cargado no es residual físico previo | #1208 | Vigente |
| D10 | Piloto G3-I aprobado en diseño pero diferido; sin ejecución | #1209, #1212 | Diferido |
| D11 | Revalidar scopes solo ante invalidadores concretos | — | Vigente |
| D12 | M24 como piloto temporal con hipótesis condicional de delta cercano a 0 | #1243, #1244 | Vigente |

---

## Detalle de decisiones

### D01 — Denominador G1-A OurVend

- **Decisión:** el denominador de G1-A son exactamente las filas devueltas por
  el reporte solicitado (máquina + fechas + parámetros visibles), vinculadas al
  hash del archivo. No aplicar filtrado interno por `ServerTime`/`MachineTime`.
- **Rationale:** el reporte devuelto es el contrato; filtrar internamente
  introduce sesgo no verificable por el negocio.
- **Ref. Engram:** #1203 — OurVend returned-report membership decision.
- **Afirmaciones habilitadas:** acuerdo monetario contra el reporte recibido.
- **Afirmaciones prohibidas:** afirmaciones sobre datos que el reporte *debería*
  haber incluido; cualquier filtrado no documentado.
- **Invalidador:** cambio en la estructura del reporte de OurVend que altere el
  conjunto devuelto.

---

### D02 — Selección de fuente y alineación de eventos

- **Decisión:** elegir qué fuente usar (TB, OV) es independiente de cómo se
  alinean temporalmente los eventos.
- **Rationale:** mezclar selección y alineación en un solo paso oculta deltas.
- **Afirmaciones habilitadas:** trazabilidad con responsabilidad separable por
  fuente y por alineación.
- **Afirmaciones prohibidas:** afirmaciones que mezclen selección + alineación
  en un solo paso sin descomponer.
- **Invalidador:** —.

---

### D03 — Comportamiento horario multi-factorial, scope por máquina

- **Decisión:** M12 (café+snack Android) con auto-network informado; M23
  (controlador snack simple); POS terminal Android independiente. M24 presenta
  selector de controlador entre `Time Machine` y `Time Server`, y muestra
  dependencia de batería RTC para persistencia horaria. El comportamiento
  puede depender de hardware, selector/configuración, estado de batería RTC,
  firmware y conectividad. Debe delimitarse por máquina; no generalizar.
- **Rationale:** la inspección de M24 (2026-07-21, Engram #1243) reveló un
  selector entre `Time Machine` y `Time Server` y comportamiento consistente
  con batería RTC agotada o ausente (retención de hora no observable;
  reemplazo/medición pendiente). Esto demuestra que el comportamiento no es
  únicamente propiedad del hardware, sino que puede depender de
  selector/configuración, estado de batería y posiblemente firmware.
  La afirmación previa —"la capacidad de sincronización horaria es propiedad
  del hardware, no una decisión de configuración"— ya no es sostenible.
- **Ref. Engram:** #1196 — Approved M12/M23 MachineTime calibrations; #1243 —
  M24 field inspection: selector and RTC battery.
- **Afirmaciones habilitadas:** afirmaciones operacionales scoped a máquina
  específica (ej. M23 delta −721, M12 delta −1).
- **Afirmaciones prohibidas:** generalizar calibración de una máquina a otra;
  asumir sincronización NTP donde no existe; asumir que el mismo modelo de
  controlador implica el mismo comportamiento horario.
- **Invalidador:** evidencia física de que el tipo de controlador declarado es
  incorrecto; reemplazo de batería RTC; cambio de selector; actualización de
  firmware.

---

### D04 — G2 es aprobación operacional scoped

- **Decisión:** la causa física de la diferencia horaria (timezone, deriva de
  reloj) permanece desconocida. G2 M12/M23 es aprobación operacional scoped
  solamente.
- **Rationale:** para M12 y M23 no hubo acceso a configuración de sistema ni a
  logs de eventos de red durante sus ventanas de calibración originales. La
  evidencia de M24 (selector, batería RTC) no establece retroactivamente la
  causa física de los deltas de M12 o M23 en sus respectivas ventanas
  aprobadas.
- **Ref. Engram:** #1196 — Approved M12/M23 MachineTime calibrations.
- **Afirmaciones habilitadas:** uso del delta calibrado para alinear ventanas
  de tiempo operacionales.
- **Afirmaciones prohibidas:** zona horaria, tiempo absoluto, o corrección
  física del reloj.
- **Invalidador:** cambio de configuración de reloj en la máquina; reemplazo
  de controlador.

---

### D05 — `Fecha de movimiento` como referencia operacional

- **Decisión:** `Fecha de movimiento` (Transbank) es referencia operacional
  para `Venta`/`ABONADA` elegibles, débito/prepago, timestamp no-medianoche,
  mapeo válido. No es verdad física ni propiedad de la fuente.
- **Rationale:** Transbank asigna este timestamp; no es "la hora real de la
  venta" sino un marcador del sistema de pagos.
- **Ref. Engram:** #1193 — Transbank operational reference contract decision.
- **Afirmaciones habilitadas:** alineación temporal de pagos con ventas OV.
- **Afirmaciones prohibidas:** usar `Fecha de movimiento` como tiempo absoluto
  de la transacción o como evidencia de secuencia causal.
- **Invalidador:** documentación de Transbank que cambie la semántica del
  campo.

---

### D06 — Boundary Policy v1

- **Decisión:** periodo activo = `[FechaRecarga_start, próximo FechaRecarga
  estrictamente posterior misma máquina)`; fin = `min(now, start + 90d)` bajo
  reloj/versión declarados. SQL sentinel 2099, DTO +2y y mecanismos runtime
  actualmente en conflicto. La política es conceptual, no un cambio de código.
- **Rationale:** necesaria para definir ventanas de evaluación sin ambigüedad.
  La implementación actual tiene contradicciones no resueltas.
- **Ref. Engram:** #1212 — Owner approval — Boundary Policy v1 + G3-I design.
  Ver también #1207 — Contradictory `FechaFin` authorities discovery.
- **Afirmaciones habilitadas:** definición unívoca del periodo activo de una
  recarga.
- **Afirmaciones prohibidas:** basarse en la implementación runtime actual
  (DTO +2y, sentinel 2099) como si fuera la política.
- **Invalidador:** resolución del conflicto SQL/DTO/runtime; nueva política que
  reemplace v1.

---

### D07 — Fórmula de inventario por conservación

- **Decisión:** `raw_expected = initial_snapshot + documented_adjustments −
  admitted_vend_count`. `overflow = max(0, −raw_expected)`. La presentación
  puede truncar visualmente el overflow; el cálculo interno lo preserva.
- **Rationale:** la fórmula de conservación es el único modelo demostrable.
  Truncar visualmente evita alarmas falsas sin perder información.
- **Ref. Engram:** #1208 — Inventory residual/photo/overflow evidence gaps
  discovery.
- **Afirmaciones habilitadas:** reconstrucción de inventario esperado por
  conservación.
- **Afirmaciones prohibidas:** usar overflow truncado como "stockout
  confirmado"; ignorar overflow en cálculos aguas abajo.
- **Invalidador:** nueva fuente de verdad física que demuestre que la fórmula
  no modela la realidad.

---

### D08 — `StockActual` y fotos no son evidencia independiente

- **Decisión:** `ConfiguracionSlot.StockActual` es derivado, no evidencia
  independiente. `FotoGuia` es referencia visual; `FotoOcr` es OCR de planilla
  de carga; ninguno prueba cantidad residual.
- **Rationale:** el stock actual en BD se calcula; las fotos son documentación
  auxiliar sin valor probatorio cuantitativo.
- **Ref. Engram:** #1208 — Inventory residual/photo/overflow evidence gaps
  discovery.
- **Afirmaciones habilitadas:** referencia visual para operaciones.
- **Afirmaciones prohibidas:** citar `StockActual` como medición independiente;
  usar OCR de foto como conteo exacto de residual.
- **Invalidador:** —.

---

### D09 — Siguiente snapshot no es residual

- **Decisión:** la cantidad cargada en el siguiente snapshot no es el residual
  físico previo.
- **Rationale:** una recarga nueva no es una medición de lo que sobró; pueden
  haber retirado productos, ajustado, etc.
- **Ref. Engram:** #1208 — Inventory residual/photo/overflow evidence gaps
  discovery.
- **Afirmaciones habilitadas:** afirmaciones sobre cantidad cargada.
- **Afirmaciones prohibidas:** usar "cargué N" como "sobraban N".
- **Invalidador:** —.

---

### D10 — Piloto G3-I

- **Decisión:** workflow aprobado en diseño pero diferido. Móvil en máquina;
  antes de movimiento/carga: una foto general, conteo exacto solo para slots
  visibles no vacíos, confirmación explícita de slots omitidos como vacíos,
  congelar observación, luego cargar. Operador fijo, M23, una foto cubre todos
  los slots. Ninguna ejecución autorizada.
- **Rationale:** el diseño está completo; la ejecución requiere coordinación
  y una ventana de oportunidad con el operador.
- **Ref. Engram:** #1209 — Approved fast pre-refill capture workflow
  decisions; #1212 — Owner approval — Boundary Policy v1 + G3-I design.
- **Afirmaciones habilitadas:** marco de captura definido y listo para
  ejecutar.
- **Afirmaciones prohibidas:** afirmaciones de stockout físico basadas en datos
  actuales (sin captura G3-I). **Acción prohibida:** ejecutar la captura sin
  autorización explícita.
- **Invalidador:** ejecución del piloto que produzca datos; nueva política que
  reemplace el diseño aprobado.

---

### D11 — Revalidación solo ante invalidadores

- **Decisión:** revalidar scopes aprobados solo ante invalidadores concretos.
  No re-evaluar periódicamente.
- **Rationale:** sin invalidador, el mismo contrato + versión + ventana produce
  el mismo resultado. Re-evaluar sin causa es costo sin beneficio.
- **Afirmaciones habilitadas:** confiar en scopes aprobados hasta invalidación.
- **Afirmaciones prohibidas:** re-evaluar sin invalidador o por cambio de
  herramienta/versión de herramienta.
- **Invalidador:** invalidador concreto: cambio en reportes, reemplazo de
  hardware, alteración de reloj, nuevo contrato Transbank.

---

### D12 — M24 como piloto temporal con hipótesis condicional de delta cercano a 0

- **Decisión:** se establecen dos pistas paralelas:
  - **Pista A (M24):** usar M24 como piloto temporal de bajo riesgo,
    post-reemplazo de batería RTC. Configurar selector `Time Machine` y hora
    Chile local. Probar persistencia tras corte de energía controlado.
    Verificar que OurVend exporte el `MachineTime` corregido mediante evento
    de prueba autorizado.
  - **Pista B (M23 G3-I):** continuar planificación de captura pre-recarga M23
    sin ejecución autorizada (misma política que D10).
- **Rationale:** la inspección de M24 (2026-07-21, Engram #1243) reveló
  selector entre `Time Machine` y `Time Server`, y comportamiento consistente
  con batería RTC agotada o ausente (retención de hora no observable;
  reemplazo/medición pendiente). Si con
  batería funcional y selector correcto M24 retiene hora Chile, el delta
  exportado por OurVend podría ser cercano a 0, pero esto **no está probado**
  — es una hipótesis pendiente de validación.
- **Ref. Engram:** #1243 — M24 field inspection: selector and RTC battery;
  #1244 — Two parallel tracks: M24 temporal pilot + M23 G3-I planning.
- **Afirmaciones habilitadas:**
  - Afirmar observaciones confirmadas de M24: selector entre `Time Machine` y
    `Time Server`, no-persistencia actual (comportamiento consistente con
    batería RTC agotada o ausente; reemplazo/medición pendiente).
  - Planificar reemplazo de batería RTC y pruebas de persistencia.
  - Planificar verificación de `MachineTime` exportado por OurVend para M24.
- **Afirmaciones prohibidas:**
  - Afirmar delta cercano a 0 para M24 sin validación completa (retención de
    reloj + evento de prueba autorizado + verificación cruzada).
  - Afirmar que `Time Server` corresponde a hora China sin documentación del
    proveedor.
  - Ejecutar captura G3-I en M23 sin autorización explícita (D10 se mantiene).
  - Desplegar calibración de M24 a otras máquinas sin validación individual.
- **Invalidadores:**
  - Reemplazo de batería RTC que no resuelva la persistencia horaria.
  - Configuración de selector que no produzca hora Chile correcta.
  - OurVend que no exporte el `MachineTime` esperado.
  - Delta(s) registrados que, bajo un protocolo predeclarado de captura y
    muestreo, se aparten del valor esperado por la hipótesis. Mientras no
    exista una regla de aceptación aprobada (tolerancia, distribución, N), el
    delta documentado permanece en estado `characterizing` — no aprueba ni
    reprueba la hipótesis.
  - Documentación del proveedor que aclare la semántica de `Time Server`.

---

## Glosario

| Término | Definición |
|---------|-----------|
| **Boundary Policy v1** | Regla conceptual para definir el período activo de una recarga: `[FechaRecarga_start, próximo FechaRecarga misma máquina)` con fin = `min(now, start + 90d)`. |
| **Bundle** | Múltiples ventas agrupadas en un solo pago Transbank. |
| **Compuerta (Gate)** | Condición que debe cumplirse antes de que ciertas afirmaciones sean válidas. |
| **Contrato de membresía returned-report** | El reporte devuelto por OurVend para una máquina/rango es el conjunto exacto de filas que constituye el contrato de membresía. |
| **Delta MachineTime** | Diferencia observada entre el timestamp de OurVend (`MachineTime`) y la referencia operacional de Transbank (`Fecha de movimiento`). |
| **G1-A** | Compuerta de acuerdo monetario multi-fuente. |
| **G1-B** | Compuerta de trazabilidad pago↔venta (*candidate*). |
| **G2** | Compuerta de calibración tiempo/identidad operacional. |
| **G3-C** | Compuerta de evidencia de costos/márgenes. |
| **G3-I** | Compuerta de evidencia de inventario (I.0 = conservación, I.1 = residual observado). |
| **G4** | Compuerta de readiness predictivo. |
| **Invalidador** | Evento concreto que invalida un scope aprobado (cambio de reporte, hardware, reloj, contrato). |
| **OV** | OurVend (portal de gestión de máquinas expendedoras). |
| **Scope aprobado** | Combinación específica de máquina/terminal/ventana/contrato/versión para la cual una compuerta pasó. |
| **Selector de controlador** | Interruptor físico o configuración en el controlador de la máquina que selecciona la fuente de tiempo (`Time Machine`, `Time Server`). |
| **Batería RTC** | Batería de respaldo del reloj de tiempo real (*Real-Time Clock*). Su agotamiento causa pérdida de hora al desconectar la máquina. |
| **Época temporal** | Período calibrado de una máquina delimitado por su configuración de reloj, versión de firmware y estado de hardware vigentes. Cualquier cambio en estos crea una nueva época que requiere recalibración. |
| **Source-only** | Evento (pago o venta) que existe en una sola fuente sin correspondencia en la otra. |
| **TB** | Transbank (procesador de pagos). |

---

## Versión

| Versión | Fecha | Cambio |
|---------|-------|--------|
| v1.0 | 2026-07-18 | Registro inicial: 11 decisiones del roadmap de fundación de datos. |
| v1.1 | 2026-07-18 | Tabla índice compacta + detalle expandido por decisión. Referencias Engram añadidas. |
| v1.2 | 2026-07-21 | D03 corregido: comportamiento multi-factorial. D12 añadido: M24 piloto temporal. Glosario extendido. #1243, #1244. |

---

Volver al [README del roadmap](README.md).
