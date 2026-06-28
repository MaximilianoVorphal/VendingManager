# Design v3 Integration — Docs Index

> Documentation for the `design-v3-integration` change that adopted the Claude Design v3 "Industrial Terminal" system into VendingManager.Web.

## Documents

| Document | Description |
|----------|-------------|
| [`ABANDONED.md`](ABANDONED.md) | Records branches superseded by the design-v3 plan (FluentUI migration, experimental_UwU) |
| [`ARCHIVE.md`](ARCHIVE.md) | Final archive summary — what was built, open issues, deferred work, artifact index |

## Redesigned pages

| Page | Route | File |
|------|-------|------|
| Login | `/login` | `src/VendingManager.Web/Pages/Login.razor` |
| Panel de Control | `/` | `src/VendingManager.Web/Pages/Home.razor` |
| Análisis de Productos | `/analisis-ventas` | `src/VendingManager.Web/Pages/AnalisisVentas.razor` |
| Caja | `/caja` | `src/VendingManager.Web/Pages/Caja.razor` |
| Templates Recarga | `/templates-recarga` | `src/VendingManager.Web/Pages/TemplatesRecarga.razor` |
| Informe de Ventas | `/informe-ventas` | `src/VendingManager.Web/Pages/Reportes.razor` |

## Key files added

| Path | Purpose |
|------|---------|
| `wwwroot/css/vendingmanager.css` | Token + component entry point |
| `wwwroot/css/tokens/colors.css` | Ink/paper/status palette (`--ink-900`, `--paper-100`, `--signal-*`) |
| `wwwroot/css/tokens/typography.css` | Type scale + JetBrains Mono / Space Grotesk families |
| `wwwroot/css/tokens/spacing.css` | Spacing + breakpoints + z-index scale |
| `wwwroot/css/tokens/effects.css` | Borders, shadows, lifts, reduced-motion override |
| `wwwroot/css/tokens/fonts.css` | Google Fonts `@import` |
| `Shared/VmButton.razor` | Primary/secondary/danger/ghost buttons |
| `Shared/VmCard.razor` | Card shell with header variants |
| `Shared/VmBadge.razor` | Status badges |
| `Shared/VmKpiCard.razor` | KPI display cards |
| `Shared/VmInput.razor` | Form inputs with label association |
| `Shared/VmSelect.razor` | Selects with `VmSelectOption` |
| `Shared/VmSlotCard.razor` | Slot card (unconsumed in this change) |
| `Shared/VmNavbar.razor` | Top navbar replacing `NavMenu.razor` |
| `Components/MachineOnlinePanel.razor` | "Máquinas online" side panel |
| `Services/IPlantillaService.cs` | Interface for Plantilla selector |
| `Services/MockPlantillaService.cs` | Mock implementation (3–4 hardcoded) |
| `Services/IMachineOnlineService.cs` | Interface for machine status |
| `Services/MockMachineOnlineService.cs` | Mock implementation (5–6 machines) |

## Build & test

```bash
# Build (0 warnings expected)
dotnet build src/VendingManager.slnx

# Unit tests (485/485 expected)
dotnet test src/VendingManager.Tests/VendingManager.Tests.csproj

# Visual baselines (requires running backend + seeded user)
dotnet test src/VendingManager.Tests.Viewport/VendingManager.Tests.Viewport.csproj
```

## Engram artifacts

All SDD artifacts live in Engram under the `sdd/design-v3-integration/` topic prefix. See `ARCHIVE.md` for the full artifact index with observation IDs (#380–#395).
