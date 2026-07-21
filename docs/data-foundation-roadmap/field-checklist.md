# Checklist de Terreno — Data Foundation Roadmap

Guía de investigación y captura de evidencia para la fundación de datos.
Ejecutable solo con autorización explícita. Ninguna acción aquí autorizada
sin orden directa.

> **Privacidad:** no registrar datos sensibles de tarjetahabientes (PAN,
> titular, CVV, etc.). No incluir evidencia local cruda ni rutas personales
> del operador en commits o reportes.

---

## A. Investigación de reloj — máquina snack M24 (piloto temporal)

Ejecutar ANTES de cualquier cambio en la máquina o su configuración.
Requiere técnico autorizado para trabajos con batería y energía.

### Datos base

- [ ] Machine ID: _________________________ (M24)
- [ ] Terminal / POS ID: _________________________
- [ ] Modelo de controlador (visible en etiqueta o pantalla de servicio):
- [ ] Versión de firmware (si accesible):
- [ ] Posición del selector (Time Machine / Time Server / otro): _________
- [ ] Fecha y hora de la investigación (UTC y local):
- [ ] Operador que realiza la investigación:
- [ ] Técnico que realiza el reemplazo (si aplica):

### Estado del reloj — línea base

- [ ] Hora mostrada en la máquina: _________________________
- [ ] Hora en teléfono con sincronización de red (foto conjunta inmediata):
  *Adjuntar foto con ambos visibles simultáneamente.*
- [ ] Desplazamiento calculado (máquina − teléfono): _________
- [ ] Configuración de fecha/hora (menú de servicio):
  - [ ] Automática (NTP/red)
  - [ ] Manual
  - [ ] No visible / no accesible
- [ ] Zona horaria configurada (si visible):

### Selector de controlador

- [ ] Posición actual del selector:
  - [ ] Time Machine
  - [ ] Time Server
  - [ ] Otro: _________________________
- [ ] Foto del selector en su posición actual (adjuntar).
- [ ] Posición a la que se cambiará (si aplica): _________________________

### Batería RTC

- [ ] Batería RTC presente: [ ] Sí  [ ] No  [ ] No verificado
- [ ] Estado visual de la batería (corrosión, fuga, daño): _________
- [ ] Voltaje medido (si accesible con instrumento adecuado): _________
- [ ] ¿Se reemplazó la batería?
  - [ ] Sí
    - Fecha de reemplazo: _________________________
    - Tipo/modelo de batería nueva: _________________________
    - Técnico que realizó el reemplazo: _________________________
  - [ ] No

### Configuración de hora Chile local

- [ ] Hora configurada en máquina tras ajustes: _________________________
- [ ] Zona horaria configurada: Chile (UTC−3 / UTC−4 según DST)
- [ ] Selector configurado en:
  - [ ] Time Machine
  - [ ] Time Server
- [ ] Foto conjunta máquina + teléfono sincronizado tras configuración
  (adjuntar).

### Prueba de persistencia tras corte de energía controlado

> **Advertencia:** el corte de energía y reemplazo de batería deben ser
> realizados por técnico autorizado siguiendo el procedimiento del fabricante.
> No realizar sin supervisión del fabricante o personal calificado.

- [ ] Hora antes del corte: _________________________
- [ ] Hora del corte: _________________________
- [ ] Duración del corte: _________________________
- [ ] Hora al reconectar: _________________________
- [ ] Hora en teléfono sincronizado al reconectar: _________________________
- [ ] Diferencia máquina − teléfono tras reconexión: _________________________
- [ ] Foto conjunta tras reconexión (adjuntar).

### Verificación de persistencia diferida (opcional, recomendada)

Si es posible regresar horas o días después:

- [ ] Hora en máquina: _________________________
- [ ] Hora en teléfono sincronizado: _________________________
- [ ] Diferencia: _________________________
- [ ] Foto conjunta (adjuntar).
- [ ] ¿Selector sigue en la posición configurada?
  - [ ] Sí  [ ] No  (si no, describir: _________________________)

### Verificación de OurVend MachineTime exportado

Requiere un evento de venta o prueba autorizado en OurVend. Sin evento
autorizado, **no es posible verificar el timestamp exportado**.

- [ ] ¿Se realizó un evento de prueba autorizado?
  - [ ] Sí (pasar a Verificación)
  - [ ] No (la retención de reloj por sí sola **no prueba** que OurVend
    exporte el `MachineTime` corregido ni que el delta sea cercano a 0)

#### Verificación (solo si hay evento autorizado)

- [ ] ID del evento de prueba en OurVend: _________________________
- [ ] `MachineTime` exportado por OurVend: _________________________
- [ ] Hora de referencia (teléfono sincronizado) en el momento del evento:
  _________________________
- [ ] Diferencia (MachineTime − referencia): _________________________
- [ ] Número de eventos o muestra capturados: _________________________
- [ ] Ventana temporal cubierta por la muestra: _________________________
- [ ] Delta mínimo observado: _________________________
- [ ] Delta máximo observado: _________________________
- [ ] Delta promedio / mediana: _________________________

> **Prohibido:** clasificar el delta como "aceptable" o "cercano a 0"
> después de ver los resultados. Cualquier regla de aceptación (tolerancia,
> distribución, N mínimo) debe ser predeclarada y aprobada por separado.
> Sin esa regla, el delta documentado permanece en estado `characterizing`.

> **Importante:** la retención de hora correcta en pantalla no garantiza que
> el timestamp exportado por OurVend (`MachineTime`) sea correcto. Solo un
> evento de prueba autorizado con verificación cruzada puede confirmarlo.

---

## B. Captura pre-recarga — M23

Ejecutar ANTES de mover o cargar productos. Operador fijo asignado.

### Apertura

- [ ] Fecha: _________________________
- [ ] Hora de llegada a máquina: _________________________
- [ ] Hora de apertura de puerta: _________________________
- [ ] Operador: _________________________

### Foto general

- [ ] Foto general de la máquina con puerta abierta (todos los slots visibles).
  *Una sola foto que cubra todos los slots.*

### Atestación previa a la carga

Declaración grabada o escrita antes de tocar producto:

> "Máquina M23 `2410280023`. Fecha [FECHA], hora [HORA]. Antes de retirar o
> cargar producto. Los siguientes slots contienen unidades. Todos los slots
> no listados fueron verificados y están vacíos."

### Conteo por slot (solo slots no vacíos)

| Slot | Producto (SKU / nombre) | Unidades residuales exactas | Anomalía (rotura, vencido, extraño) |
|------|------------------------|----------------------------|--------------------------------------|
|      |                        |                            |                                      |
|      |                        |                            |                                      |
|      |                        |                            |                                      |
|      |                        |                            |                                      |

### Confirmación slots omitidos

- [ ] Todos los slots **no** listados en la tabla anterior fueron inspeccionados
  y están **vacíos**.
- [ ] Si algún slot omitido contenía producto, listarlo arriba.

### Congelamiento

- [ ] Conteo finalizado, hora: _________________________
- [ ] Inicio de carga, hora: _________________________

---

## C. Durante y después de la carga

### Carga

| Slot | Producto | Unidades añadidas | Cantidad final en slot | Capacidad del slot | ¿Cambió producto? |
|------|----------|-------------------|------------------------|--------------------|-------------------|
|      |          |                   |                        |                    | Sí / No |
|      |          |                   |                        |                    | Sí / No |
|      |          |                   |                        |                    | Sí / No |

### Ajustes

- [ ] Unidades retiradas (dañadas/vencidas): _________________________
- [ ] Unidades retiradas (sobrantes de carga anterior): _________________________
- [ ] Ajustes excepcionales (describir): _________________________
- [ ] ¿Se cambió la configuración de algún slot (precio, código)?
  *Detallar:*

### Foto final

- [ ] Foto final de máquina cerrada después de carga completa.

---

## D. Plantilla copiable — tabla de texto

```
=== INVESTIGACIÓN DE RELOJ M24 (PILOTO TEMPORAL) ===========================
Machine ID:       __________________   Terminal:  __________________
Controlador:      __________________   Firmware:  __________________
Selector:         [ ] Time Machine  [ ] Time Server  [ ] Otro: _____
Fecha inv.:       __________________   Hora inv.: __________________
Operador:         __________________   Técnico:   __________________
Hora máquina:     __________________
Hora teléfono:    __________________   [ ] Foto adjunta
Delta línea base: __________________
Config.:          [ ] Auto  [ ] Manual  [ ] No accesible
Zona horaria:     __________________

SELECTOR:         Posición actual: ______   [ ] Foto
                  Nueva posición:   ______
BATERÍA RTC:      [ ] Presente  [ ] Reemplazada  Fecha: ______
                  Tipo: ______  Técnico: ______
HORA CHILE:       Configurada: ______  Zona: UTC−3 / UTC−4

PRUEBA CORTE:     Antes: ______  Corte: ______  Reconexión: ______
                  Delta post-corte: ______  [ ] Foto
PERSIST. DIFERIDA: Máquina: ______  Teléfono: ______  Delta: ______
                   [ ] Foto  Selector ok: [ ] Sí  [ ] No

MACHINETIME OV:   [ ] Evento autorizado  ID: ______
                  MachineTime OV: ______  Ref. horaria: ______
                  Delta(s): min ______  max ______  avg ______
                  Muestra: N=______  ventana=______
                  [ ] Regla aceptación predeclarada: ______

> NOTA: No clasificar delta post-hoc. Sin regla predeclarada → characterizing.

=== CAPTURA PRE-RECARGA M23 =============================================
Fecha:            __________________
Hora apertura:    __________________
Operador:         __________________
[ ] Foto general adjunta
[ ] Atestación registrada

Slot | Producto | Residual | Anomalía
-----|----------|----------|----------
     |          |          |
     |          |          |

[ ] Slots no listados verificados vacíos
Hora fin conteo:  __________________
Hora inicio carga: __________________

=== CARGA ===============================================================
Slot | Producto | Añadidos | Final | Capacidad | ¿Cambio?
-----|----------|----------|-------|-----------|---------
     |          |          |       |           | Sí / No
     |          |          |       |           | Sí / No

Retirados (daño/vencido): __________________
Retirados (sobrantes):    __________________
Ajustes excepcionales:    __________________
[ ] Foto final adjunta
```

---

## Referencias de planificación

Este checklist se basa en las siguientes observaciones de Engram (proyecto
`vendingmanager`). Los IDs son identificadores internos del sistema de memoria
persistente, no enlaces web.

| Engram ID | Título | Relación con este checklist |
|-----------|--------|----------------------------|
| #1196 | Approved M12/M23 MachineTime calibrations | Calibraciones G2 que la sección A (investigación de reloj) debe poder reproducir o invalidar. |
| #1211 | `sdd/inventory-evidence-foundation/design` | Diseño de la fundación de evidencia de inventario que define los requisitos de captura. |
| #1212 | Owner approval — Boundary Policy v1 + G3-I design | Aprobación del owner que autoriza el diseño de captura (pero no su ejecución). |
| #1209 | Approved fast pre-refill capture workflow decisions | Decisiones de workflow que este checklist implementa en forma de pasos concretos. |
| #1243 | M24 field inspection: selector and RTC battery | Inspección visual de M24 que motiva la sección A de este checklist. |
| #1244 | Two parallel tracks: M24 temporal pilot + M23 G3-I planning | Decisión de dos pistas que separa la investigación M24 de la captura M23. |

---

## E. Prioridades mínimas

Si el tiempo en terreno es limitado, preservar en este orden:

**Pista A — M24 (piloto temporal)**
1. **Línea base reloj + selector:** foto conjunta máquina + teléfono
   sincronizado, más captura de posición del selector y configuración horaria.
2. **Batería RTC:** verificar presencia, estado y reemplazar si está agotada.
3. **Prueba de persistencia post-reemplazo:** corte controlado y verificación
   de retención.
4. **Verificación MachineTime:** solo si hay evento de prueba autorizado.

**Pista B — M23 (captura pre-recarga)**
1. **Foto pre-carga:** general de la máquina abierta antes de tocar producto.
   Sin esto, G3-I.1 no tiene evidencia visual.
2. **Conteo exacto pre-carga por slot:** solo slots no vacíos. Sin esto,
   G3-I.1 no tiene métrica cuantitativa.

---

## F. Recordatorio de privacidad

- No registrar PAN, titular, CVV ni ningún dato de tarjetahabiente.
- No incluir rutas de archivos personales del operador (ej. `/home/usuario/...`).
- Las fotos deben contener solo máquina, producto y operaciones; evitar
  capturar pantallas con datos personales o financieros no pertinentes.
- La evidencia cruda (fotos, planillas) no debe committears al repositorio.
  Solo referencias a documentos externos.

---

## Versión

| Versión | Fecha | Cambio |
|---------|-------|--------|
| v1.0 | 2026-07-18 | Checklist inicial: investigación de reloj y captura pre-recarga/recarga M23. |
| v1.1 | 2026-07-18 | Sección de referencias de planificación Engram añadida. |
| v1.2 | 2026-07-21 | Sección A actualizada a M24: selector, batería RTC, persistencia, MachineTime. Plantilla y prioridades actualizadas. #1243, #1244. |

---

Volver al [README del roadmap](README.md).
