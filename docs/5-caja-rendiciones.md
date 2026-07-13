# 5 — Caja y Rendiciones

> Documentación viva. Refleja el estado del código a la fecha del último commit revisado.
> Documento adicional. Alcance acotado: cubre el ciclo de **rendición/transferencia** (dinero
> entregado a trabajadores y su conciliación). El P&L / "EBITDA" de `CajaBusinessService`
> pertenece a `2-inteligencia-financiera.md`, no a este documento.

---

## 1. Resumen de Negocio

Cuando a un trabajador se le entrega plata para operar (comprar mercadería, pagar bencina y peajes), hay que poder demostrar **en qué se gastó cada peso y cuánto sobra**. Este módulo es esa rendición de cuentas.

El flujo: una **Transferencia** (plata entregada al trabajador) se gasta en **Compras** (facturas de proveedor) y **Gastos** (bencina/peajes, vía movimientos de caja). Una **Rendición** agrupa esas transferencias y gastos para probar el destino del dinero y saldar el balance (`SaldoADevolver`). Una rendición no se puede cerrar hasta que todo esté verificado y el saldo cuadre en cero.

El valor: reemplaza el "cuaderno del chofer" por un control con estados, verificación y candados que impiden cerrar cuentas descuadradas.

---

## 2. Entidades Clave (Data Model)

| Entidad | Rol | Relaciones |
| --- | --- | --- |
| **Rendicion** | Agrupa el gasto de un trabajador en un período. | `→ Transferencias` (1-N) + `→ Gastos` (`MovimientoCaja`, 1-N). `Trabajador`, `FechaInicio/Fin`, `Estado`, `RowVersion`. |
| **Transferencia** | Plata entregada a un trabajador. | `→ Compras` (1-N). FKs opcionales a `Rendicion`, `AccountingPeriod` (`PeriodoId`), `MovimientoCaja`. `Monto` `decimal(18,2)`, `Verificada` (default false). |
| **MovimientoCaja** | Asiento del libro de caja (compartido con finanzas). | `Tipo`, `Categoria`, `Monto` (signo), `OrdenCargaId`, `RendicionId`. |
| **Devolucion** | Devolución del saldo sobrante. | `Monto > 0`; postea `MovimientoCaja` inverso. |

**Relación central:** `Rendicion → Transferencia → Compra`. La transferencia es la bisagra entre el dinero entregado y lo gastado.

---

## 3. Reglas de Negocio y Supuestos (CRÍTICO)

### 3.1 Máquinas de estados

**`RendicionEstado`:** `Abierta(0)` → `Cerrada(1)`. `Cerrada` bloquea **toda** mutación (`RendicionService.cs:61-62,213-214`).

**`TransferenciaEstado`:** `Pendiente(0)` → `EnUso(1)` → `Conciliado(2)` (terminal). Auto-transiciones:

- Vincular la primera compra: `Pendiente → EnUso` (`RendicionService.cs:160-164`).
- Desvincular la última compra: `EnUso → Pendiente` (`:188-197`).
- `Conciliado` bloquea vincular nuevas compras (`:152-153`) y cualquier edición (`TransferenciaService.cs:51-52`).

### 3.2 Fórmula de conciliación (`RendicionService.cs:245-267`)

```
Transferido   = Σ Transferencia.Monto
TotalCompras  = Σ Compra.MontoTotal
TotalGastos   = Σ |MovimientoCaja.Monto|   (solo Tipo == "GASTO", excluyendo categorías estructurales vía CategoriasGasto.Estructurales.Contains)
Diferencia    = Transferido − TotalCompras − TotalGastos
SaldoADevolver = Diferencia − Devuelto      (suma de Devoluciones)
```

> **Nota:** las categorías estructurales (`RETIRO_CAPITAL`, `DEVOLUCION_RENDICION`) se excluyen del total de gastos mediante el set compartido `CategoriasGasto.Estructurales` en `Shared/`. Esto alinea los totales de rendición con los chequeos de integridad (ver `6-auditoria-integridad.md`). ✅ Ajustado en consolidacion-financiera (jul 2026).

### 3.3 Compuerta de cierre (`CerrarAsync`, `RendicionService.cs:73-140`)

4 precondiciones secuenciales, todas deben pasar antes de flipear a `Cerrada`:

1. Todas las Transferencias vinculadas `Verificada` (si no, error de conteo).
2. Todas las Compras vinculadas `Verificada`.
3. `SaldoADevolver == 0` (si no, "Registrá una devolución antes de cerrar").
4. Toda transferencia con `Estado == Conciliado`.

`Verificada` es `false` por defecto; las filas históricas quedaron explícitamente sin verificar post-migración (`Transferencia.cs:65`).

> Esta compuerta es descrita en el código como "espejo de `ContabilidadService.ClosePeriodoAsync`" (`:87`) — **lógica duplicada entre dos servicios** (riesgo de consistencia).

### 3.4 Subida de comprobante (`TransferenciaService.SaveComprobanteImagenAsync`, `:105-141`)

- Máximo **5 MB** (`5 * 1024 * 1024`).
- Extensiones permitidas: `.jpg`, `.jpeg`, `.png`, `.pdf`.
- Almacenado en `/uploads/transferencias/{Guid}.ext`; el archivo previo se elimina.

---

## 4. Flujo Técnico

**Servicios:** `RendicionService`, `TransferenciaService`, `CajaService`, `CajaBusinessService` (este último es predominantemente P&L/finanzas — ver doc 2), `ContabilidadService`.

**Páginas Blazor:** `Conciliacion.razor`, `ConciliacionMovil.razor`, `Caja.razor`, `CajaV2.razor`.

**Nota de alcance:** la **conciliación global** (`ConciliacionGlobalDto`, `ContabilidadService.GetConciliacionGlobal` / `ContabilidadController`) es cierre de período contable, distinto del saldo por-rendición; pertenece a `2-inteligencia-financiera.md`.

---

## 5. Riesgos y Deuda Técnica Conocida

- **Lógica de cierre duplicada** entre `RendicionService.CerrarAsync` y `ContabilidadService.ClosePeriodoAsync` (`:87`) — deben mantenerse en sync manualmente.
- ✅ **Resuelto — Divergencia de "gasto real" corregida:** desde consolidacion-financiera (jul 2026), `RendicionService.GetResumenAsync` también excluye categorías estructurales vía `CategoriasGasto.Estructurales`, alineado con `IntegrityCheckService`.
- **`TASK-10`** comentado sobre el cableado de `Devuelto` (`RendicionService.cs:254`).
- La convención de signo de `MovimientoCaja` (positivo entra, negativo sale) se asume consistente en todas las rutas; no hay validación central.
