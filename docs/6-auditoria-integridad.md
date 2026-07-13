# 6 — Auditoría e Integridad de Datos

> Documentación viva. Refleja el estado del código a la fecha del último commit revisado.
> Documento adicional (plataforma transversal). Combina auditoría, historial por entidad y
> chequeos de integridad, porque juntos forman la red de seguridad de los datos financieros.

---

## 1. Resumen de Negocio

Este módulo es la **red de seguridad**: no genera valor de negocio directo, pero garantiza que los números en los que confía el gerente sean confiables y auditables.

Hace tres cosas:

1. **Rastro de auditoría:** cada cambio en la base de datos (quién, qué, antes/después) queda registrado automáticamente. Si un número cambió, se sabe cuándo y quién lo tocó.
2. **Historial por entidad:** para 12 entidades clave se guarda además una copia versionada de cada modificación.
3. **Chequeos de integridad:** 6 validaciones automáticas sobre la data de conciliación que detectan descuadres (transferencias sobre-asignadas, compras huérfanas, rendiciones cerradas con saldo distinto de cero).

El valor: si algo no cuadra, el sistema lo señala **antes** de que el contador cierre el mes con un error.

---

## 2. Entidades y Componentes Clave

| Componente | Rol |
| --- | --- |
| **`AuditSaveChangesInterceptor`** | Interceptor EF Core que captura cada Added/Modified/Deleted. |
| **`Auditoria`** | Tabla de rastro: `BeforeJson`/`AfterJson` + usuario resuelto. |
| **Entidades `History/`** | Copias versionadas por entidad (12 tipos mapeados). |
| **`IntegrityCheckService`** | Motor de los 6 chequeos de integridad. |
| **`AuditService`** | Registro explícito de acciones (`RegistrarAccionAsync`). |
| **`Roles`** | Constantes de rol (`Admin`, `User`). |

---

## 3. Reglas de Negocio y Supuestos (CRÍTICO)

### 3.1 Rastro de auditoría automático (`AuditSaveChangesInterceptor.cs`)

- Es un `SaveChangesInterceptor` de EF Core: captura **cada** entidad Added/Modified/Deleted en la tabla `Auditoria` con snapshots escalares `BeforeJson`/`AfterJson` y el `Usuario` resuelto (username de `HttpContext`, si no `"system"`) (`:96-147,275-285`).
- Aparte, un **`HistoryTypeMap` de exactamente 12 entidades** escribe filas de historial dedicadas vía reflexión (`:36-50`): `Compra, Producto, Maquina, Venta, MovimientoCaja, ConfiguracionSlot, GastoRecurrente, OrdenCarga, User, Transferencia, Rendicion, ProveedorCatalog`.
- Las filas de historial se construyen **reflexivamente**, copiando las props escalares del snapshot de dominio (`:187-229`). Entidades `History/` correspondientes bajo `Core/Entities/History/` más `TransferenciaHistory`/`RendicionHistory` en el nivel superior. Migración `20260505193727_CreateHistoryTables`.

### 3.2 Los 6 chequeos de integridad (`IntegrityCheckService.cs:25-38`)

Corren sobre la data de conciliación y devuelven **solo los resultados no vacíos**:

| ID | Severidad | Regla |
| --- | --- | --- |
| **3A** | Error | Transferencia sobre-asignada: `Σ Compras.MontoTotal > Transferencia.Monto` (`:44-76`). |
| **3B** | Warn | Compras huérfanas (`TransferenciaId == null`) (`:82-107`). |
| **3C** | Info | Transferencias `Conciliado` sin compras (`:113-139`). |
| **7A** | Error | Rendición `Cerrada` con `SaldoADevolver != 0` (`:145-197`). |
| **7B** | Warn | Transferencia cruzada (`RendicionId` **Y** `PeriodoId` no-null) (`:203-228`). |
| **7C** | Error | Rendición `Abierta` con `SaldoADevolver < 0` (`:234-285`). |

**Definición de saldo en integridad:**

```
SaldoADevolver = Transferido − Compras − Gastos − Devuelto
```

donde **los gastos excluyen categorías estructurales** `RETIRO_CAPITAL` y `DEVOLUCION_RENDICION` (`EsGastoReal`, `:14-18,287-288`).

> **⚠️ Inconsistencia real:** esta definición de "gasto" **difiere** de `RendicionService.GetResumenAsync`, que usa `Tipo == "GASTO"`. Dos criterios distintos de "gasto real" sobre los mismos datos — puede producir saldos que no coinciden entre la vista de rendición y el chequeo de integridad. Documentado también en `5-caja-rendiciones.md`.

Severidad: enum `CheckSeverity` (`Error`/`Warn`/`Info`).

### 3.3 Autenticación y roles

Solo **dos roles**: `Roles.Admin = "Admin"`, `Roles.User = "User"` (`Roles.cs`). Autenticación por cookie vía `CookieAuthenticationStateProvider`, `AccountController`, `RedirectToLogin.razor`. Superficie pequeña — no amerita documento propio, se documenta aquí como sección de plataforma.

---

## 4. Flujo Técnico

**Componentes:** `AuditSaveChangesInterceptor` (registrado en el pipeline de EF), `AuditService`, `IntegrityCheckService`.

**Controlador/UI:** los resultados de integridad se exponen vía el controlador correspondiente y se presentan en la UI de conciliación/contabilidad. El rastro de auditoría e historial es transparente (se escribe en cada `SaveChanges`, sin acción del usuario).

---

## 5. Riesgos y Deuda Técnica Conocida

- **Escritura de historial por reflexión es frágil:** depende de `Type.GetType` por nombre (`AuditSaveChangesInterceptor.cs:83,183`). Renombrar una entidad o mover su namespace rompe el mapeo silenciosamente.
- **Dos definiciones divergentes de "gasto real"** (integridad vs rendición) — inconsistencia analítica real (§3.2).
- **`IsMonthLockedStatic`** en `CajaBusinessService.cs:191-197` está cableado a `return false` ("Actualmente deshabilitado") — lógica de candado muerta. El único candado real de inmutabilidad son los períodos `Cerrado`.
- **Rastro de auditoría solo captura props escalares** (no navegaciones/colecciones) — cambios en relaciones pueden no quedar reflejados en `BeforeJson`/`AfterJson`.
