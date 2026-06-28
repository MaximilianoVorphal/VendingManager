# Design v3 Integration — Archive

> **Change**: `design-v3-integration`
> **Branch**: `feature/design-v3-integration`
> **Last SHA**: `5d35fac14f21caa364e4f34347231bcc3618111d`
> **Archived**: 2026-06-28

## 1. Summary

We adopted the Claude Design v3 "Industrial Terminal" system into VendingManager.Web. Six pages were restyled with a coherent token system (colors, typography, spacing, effects, fonts) and 8 reusable Vm* Blazor components. The FluentUI migration plan was abandoned in favor of preserving the industrial-terminal identity.

### What changed

- **CSS token system**: 5 token files (`colors.css`, `typography.css`, `spacing.css`, `effects.css`, `fonts.css`) + entry point `vendingmanager.css` — all additive, no conflict with legacy `--industrial-*` tokens
- **8 Vm* components**: VmButton, VmCard, VmBadge, VmKpiCard, VmInput, VmSelect, VmSlotCard (unconsumed), VmNavbar — in `Shared/`
- **MachineOnlinePanel**: Right sidebar with green/red machine status dots
- **Mock services**: `IPlantillaService`/`MockPlantillaService`, `IMachineOnlineService`/`MockMachineOnlineService` behind interfaces for easy swap
- **NavMenu.razor deleted**, replaced by VmNavbar with mobile hamburger

### Six redesigned pages

| Page | Route | Key changes |
|------|-------|-------------|
| Login | `/login` | VENDING wordmark, VmInput, VmButton, VmCard danger error |
| Panel de Control | `/` | 3 VmKpiCard, VmCard danger alarm with 🚨, Chart.js industrial palette |
| Análisis de Productos | `/analisis-ventas` | VmCard filter panel, Plantilla selector cascades to machine filter, VmBadge ABC/Margen |
| Caja | `/caja` | 4 VmKpiCard always visible, internal scroll, tab bar → modal triggers, 4 modals |
| Templates Recarga | `/templates-recarga` | VmCard grid list, VmBadge pending, modals restyled, SlotCard preserved in editor |
| Informe de Ventas | `/informe-ventas` | VmCard control panel, 4 VmKpiCard, industrial table, 5 modals restyled |

### In numbers

- **15 PRs** (PR0–PR11), **65 commits**
- **~6,200 lines changed** across 15 PR branches
- **485/485 unit tests passing**, 0 build warnings
- **1/6 visual baselines captured** (login.png); 5 pending backend
- **6 warnings, 3 suggestions** at archive time — no BLOCKING issues

## 2. What's deferred to Phase 2

- **StockoutDashboard** — user explicitly deferred ("lo dejo para una segunda fase"). 705-line page, design is significantly simpler.
- **PurchaseReport** (`/informe-compras`) — out of scope per proposal.
- **Operaciones pages**: Máquinas, Compras, Inventario, Cargas.
- **Administración pages**: Auditoría, Usuarios, Historial.
- **Real backend services**: Replace `MockPlantillaService` and `MockMachineOnlineService`.

## 3. Open issues at archive

### Warnings (follow-up PRs)
| # | Issue | Action |
|---|-------|--------|
| W1 | `AnalisisVentas`: pending-stock rows use `--tint-danger` instead of `--tint-pending` (REQ-2.8) | Fix tint variable |
| W2 | `Caja`: "REGISTRAR TODOS" in Aplicar Pendientes is `<button>` not `VmButton` (REQ-3.8) | Swap to VmButton |
| W3 | VmNavbar: "Informe Ventas" and "Análisis Productos" are disabled placeholders | Fix ActiveNav/highlight |
| W4 | `IMachineOnlineService` contract deviation (method/return type differ from spec) | Align spec or impl |
| W5 | `IPlantillaService` contract deviation (no `MaquinaIds` as specified) | Align spec or impl |
| W6 | Reportes sub-tables and Home chart cards still use Bootstrap classes | Minor visual gap |

### Suggestions
| # | Issue | Action |
|---|-------|--------|
| S1 | VmNavbar "Cerrar Sesión" has no OnClick | Wire logout |
| S2 | AnalisisVentas "Análisis por Categoría" uses Bootstrap | Deferred |
| S3 | 5/6 visual baselines uncaptured | Capture when backend available |

## 4. Engram artifact index

| Artifact | Topic key | ID |
|----------|-----------|----|
| Explore | `sdd/design-v3-integration/explore` | #380 |
| Proposal | `sdd/design-v3-integration/proposal` | #381 |
| Spec | `sdd/design-v3-integration/spec` | #382 |
| Design | `sdd/design-v3-integration/design` | #383 |
| Tasks | `sdd/design-v3-integration/tasks` | #384 |
| Apply-Progress | `sdd/design-v3-integration/apply-progress` | #385 |
| Verify-Report | `sdd/design-v3-integration/verify-report` | #394 |
| **Archive-Report** | **`sdd/design-v3-integration/archive-report`** | **#395** |

## 5. Follow-up before merge

1. Push `feature/design-v3-integration` to origin
2. Manually open 15 PRs on GitHub (`gh` not authenticated)
3. Fix warnings W1–W6
4. Capture 5 visual baselines with backend + seeded user
5. Plan Phase 2: StockoutDashboard, PurchaseReport, Operaciones, Administración
