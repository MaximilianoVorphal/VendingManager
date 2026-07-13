# 5 — Caja y Rendiciones

> Documentación viva. Refleja el estado del código a la fecha del último commit revisado.
> Alcance acotado: cubre el ciclo de **rendición/transferencia** (dinero
> entregado a trabajadores y su conciliación). El P&L de `CajaBusinessService`
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

## 3. Reglas de Negocio y Supuestos

### 3.1 Máquinas de estados

**`RendicionEstado`:** `Abierta(0)` → `Cerrada(1)`. `Cerrada` bloquea **toda** mutación.

**`TransferenciaEstado`:** `Pendiente(0)` → `EnUso(1)` → `Conciliado(2)` (terminal). Auto-transiciones:

- Vincular la primera compra: `Pendiente → EnUso`.
- Desvincular la última compra: `EnUso → Pendiente`.
- `Conciliado` bloquea vincular nuevas compras y cualquier edición.

### 3.2 Fórmula de conciliación

```
Transferido   = Σ Transferencia.Monto
TotalCompras  = Σ Compra.MontoTotal
TotalGastos   = Σ |MovimientoCaja.Monto|   (solo Tipo == "GASTO", excluyendo categorías estructurales)
Diferencia    = Transferido − TotalCompras − TotalGastos
SaldoADevolver = Diferencia − Devuelto      (suma de Devoluciones)
```

Las categorías estructurales (como `RETIRO_CAPITAL`, `DEVOLUCION_RENDICION`) se excluyen del total de gastos mediante el catálogo central `CategoriasGasto.Estructurales`, compartido con los chequeos de integridad.

### 3.3 Compuerta de cierre

4 precondiciones secuenciales antes de cerrar a `Cerrada`:

1. Todas las Transferencias vinculadas deben estar `Verificada`.
2. Todas las Compras vinculadas deben estar `Verificada`.
3. `SaldoADevolver == 0` (si no, "Registrá una devolución antes de cerrar").
4. Toda transferencia con `Estado == Conciliado`.

`Verificada` es `false` por defecto.

### 3.4 Subida de comprobante

- Máximo **5 MB**.
- Extensiones permitidas: `.jpg`, `.jpeg`, `.png`, `.pdf`.
- Almacenado en `/uploads/transferencias/{Guid}.ext`; el archivo previo se elimina.

---

## 4. Flujo Técnico

**Servicios:** `RendicionService`, `TransferenciaService`, `CajaService`, `CajaBusinessService` (este último es predominantemente P&L/finanzas — ver doc 2), `ContabilidadService`.

**Páginas Blazor:** `Conciliacion.razor`, `ConciliacionMovil.razor`, `Caja.razor`, `CajaV2.razor`.

**Nota de alcance:** la **conciliación global** (`ConciliacionGlobalDto`, `ContabilidadService.GetConciliacionGlobal` / `ContabilidadController`) es cierre de período contable, distinto del saldo por-rendición; pertenece a `2-inteligencia-financiera.md`.
