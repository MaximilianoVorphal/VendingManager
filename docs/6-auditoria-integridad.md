# 6 — Auditoría e Integridad de Datos

> Documentación viva. Refleja el estado del código a la fecha del último commit revisado.
> Documento de plataforma transversal. Combina auditoría, historial por entidad y
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

## 3. Reglas de Negocio y Supuestos

### 3.1 Rastro de auditoría automático

- Interceptor de EF Core que captura **cada** entidad Added/Modified/Deleted en la tabla `Auditoria` con snapshots `BeforeJson`/`AfterJson` y el `Usuario` resuelto (username de `HttpContext`, o `"system"` si no hay contexto).
- Un `HistoryTypeMap` de **12 entidades** escribe filas de historial dedicadas: `Compra, Producto, Maquina, Venta, MovimientoCaja, ConfiguracionSlot, GastoRecurrente, OrdenCarga, User, Transferencia, Rendicion, ProveedorCatalog`.
- Las filas de historial replican las propiedades del snapshot de dominio. Entidades `History/` correspondientes bajo `Core/Entities/History/`.

### 3.2 Los 6 chequeos de integridad

Corren sobre la data de conciliación y devuelven **solo los resultados no vacíos**:

| ID | Severidad | Regla |
| --- | --- | --- |
| **3A** | Error | Transferencia sobre-asignada: `Σ Compras.MontoTotal > Transferencia.Monto`. |
| **3B** | Warn | Compras huérfanas (`TransferenciaId == null`). |
| **3C** | Info | Transferencias `Conciliado` sin compras. |
| **7A** | Error | Rendición `Cerrada` con `SaldoADevolver != 0`. |
| **7B** | Warn | Transferencia cruzada (`RendicionId` **Y** `PeriodoId` no-null). |
| **7C** | Error | Rendición `Abierta` con `SaldoADevolver < 0`. |

**Definición de saldo en integridad:**

```
SaldoADevolver = Transferido − Compras − Gastos − Devuelto
```

Los gastos excluyen categorías estructurales (`RETIRO_CAPITAL`, `DEVOLUCION_RENDICION`) vía el catálogo central `CategoriasGasto.Estructurales`, compartido entre el motor de integridad y el de rendiciones.

Severidad: enum `CheckSeverity` (`Error`/`Warn`/`Info`).

### 3.3 Autenticación y roles

Solo **dos roles**: `Admin` y `User`. Autenticación por cookie vía `CookieAuthenticationStateProvider`, `AccountController`, `RedirectToLogin.razor`. Superficie pequeña — se documenta aquí como sección de plataforma.

---

## 4. Flujo Técnico

**Componentes:** `AuditSaveChangesInterceptor` (registrado en el pipeline de EF), `AuditService`, `IntegrityCheckService`.

**Controlador/UI:** los resultados de integridad se exponen vía el controlador correspondiente y se presentan en la UI de conciliación/contabilidad. El rastro de auditoría e historial es transparente (se escribe en cada `SaveChanges`, sin acción del usuario).
