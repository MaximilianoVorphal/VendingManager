# Tasks: Recarga Movil Save Flow — Correction Implementation

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~920 |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 → PR 2 → PR 3 |
| Delivery strategy | ask-on-risk |
| Chain strategy | stacked-to-main |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Lines | Notes |
|------|------|-----------|-------|-------|
| 1 | MobileRecargaSaveSheet + CSS | PR 1 | ~350 | New component, independent. Base: main. |
| 2 | MobileDatePickerSheet + data model + rewire + visual CSS | PR 2 | ~360 | Depends on save sheet component. Base: main. |
| 3 | bUnit + integration + Playwright tests | PR 3 | ~220 | Covers all SC-0X scenarios. Base: main. |

## Phase 1: Foundation — Data Model Alignment

- [x] 1.1 Modify `Pages/RecargaMovil.razor.cs` — `AllMachinesLoaded()` returns `_machines.All(m => m.TieneFotoGuia && m.FechaRecarga != default)`
- [x] 1.2 Modify `Pages/RecargaMovil.razor.cs` — `GetCargadaTag()` returns `"Cargada HH:mm"` / `"Sin cargar"` based on `TieneFotoGuia` + `FechaRecarga`
- [x] 1.3 Modify `Pages/RecargaMovil.razor.cs` — `OnProductSelected()` sets `CantidadInicial = CapacidadSlot` instead of `Math.Min(1, ...)`

## Phase 2: New Components

- [x] 2.1 Create `Components/MobileRecargaSaveSheet.razor` — photo section with "Obligatoria" badge, slot summary, two action buttons, error display
- [x] 2.2 Create `Components/MobileRecargaSaveSheet.razor.cs` — presentation-only: file validation, blob URL preview, callbacks with captured file
- [x] 2.3 Create `Components/MobileDatePickerSheet.razor` — date picker with confirm/cancel, calendar icon trigger
- [x] 2.4 Create `Components/MobileDatePickerSheet.razor.cs` — date picker parameters, HandleSave invokes OnDateConfirmed, reset on open
- [x] 2.5 Add CSS: `.rm-save-sheet__*` (save sheet only; `.rm-figure__*`, `.rm-slot-fill`, `.rm-slot-empty-warn`, `.rm-scan-cursor`, `.rm-show-price` in PR 2)

## Phase 3: Integration & Visual Fixes

- [x] 3.1 Modify `Pages/RecargaMovil.razor` — add save sheet render + state wiring, calendar icon in Resumen header
- [x] 3.2 Modify `Pages/RecargaMovil.razor.cs` — wire save-sheet state + date-picker state, rewrite save to slot-batch POST + foto-guia PUT (no terminar in save)
- [x] 3.3 Modify `Pages/RecargaMovil.razor` — Guardar button now calls SaveSlotsAsync() which opens save sheet
- [x] 3.4 Modify `Pages/RecargaMovil.razor` — calendar icon + MobileDatePickerSheet wired
- [x] 3.5 Modify `Pages/RecargaMovil.razor` — figure wrapper in EditSlots with "RETIRE AQUÍ" bar, 3×3 grid footer, topbar with units badge
- [x] 3.6 Add CSS: `.rm-figure`, `.rm-slot-fill`, `.rm-slot-empty-warn`, `.rm-scan-cursor`, `.rm-show-price`, `prefers-reduced-motion`

## Phase 4: Tests

- [x] 4.1 bUnit: `MobileRecargaSaveSheet` happy path (SC-01.1), disabled guardar (SC-01.2), API error+retry (SC-01.3) — DONE in PR 1 via Strict TDD
- [x] 4.2 bUnit: "Guardar y cargar otra máquina" callback (SC-02.1, SC-02.2) — DONE in PR 1 via Strict TDD
- [x] 4.3 bUnit: `MobileDatePickerSheet` open + confirm date (SC-03.1) — DONE in PR 2
- [x] 4.4 bUnit: `AllMachinesLoaded` mixed states (SC-06.1, SC-06.2, SC-06.3) — DONE in PR 2
- [x] 4.5 bUnit: `GetCargadaTag` hora+foto variants (SC-07.1, SC-07.2) + `OnProductSelected` full capacity (REQ-08) — DONE in PR 2
- [x] 4.6 Integration: save sequence calls slot-batch then foto-guia, no terminar call
- [x] 4.7 bUnit: SC-04.1 figure wrapper tests (RETIRE AQUÍ bar, 3×3 grid, topbar stats) — DONE in PR 3
- [x] 4.8 Playwright: save sheet opens on Guardar, figure wrapper visible, slot fill bars, ⚠ icon — DONE in PR 3
