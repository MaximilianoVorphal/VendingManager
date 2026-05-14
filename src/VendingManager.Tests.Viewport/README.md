# Viewport Responsive Tests

Playwright-based viewport smoke tests for VendingManager responsive CSS.

## Prerequisites

### 1. Install Playwright Browsers

```powershell
# From the VendingManager.Tests.Viewport directory
pwsh -ExecutionPolicy Bypass -Command ".\node_modules\.bin\playwright.ps1 install chromium"
```

Or, if using the .NET Playwright CLI:

```bash
dotnet tool install --global Microsoft.Playwright
playwright install chromium
```

### 2. Start the App

The app must be running for viewport tests to execute. Tests are marked `[Explicit]`
and will be skipped if the app is unavailable.

```bash
# In one terminal, start the app
cd src/VendingManager.Web
dotnet run
```

### 3. Set Base URL (optional)

If running against a non-default URL:

```bash
export VIEWPORT_TEST_BASE_URL=https://localhost:7001
```

## Running Tests

```bash
cd src/VendingManager.Tests.Viewport
dotnet test --filter "Viewport"
```

Or to run a specific test:

```bash
dotnet test --filter "NoHorizontalOverflow"
```

## Device Profiles

| Profile | Viewport |
|---------|----------|
| Desktop 1080p | 1920×1080 |
| Laptop 14" | 1200×800 |
| iPad Pro 11" Portrait | 834×1194 |
| iPad Pro 11" Landscape | 1194×834 |
| iPhone Portrait | 390×844 |
| iPhone Landscape | 844×390 |

## How It Works

- Tests are marked `[Explicit]` — they only run when explicitly targeted
- Each test sets the browser viewport to the target dimensions, then asserts
  `document.documentElement.scrollWidth <= window.innerWidth` (no horizontal overflow)
- Mobile form factor is set via `isMobile: true` for touch event support
- If the app is not reachable, tests call `Assert.Ignore()` and skip gracefully

## CI/CD

In CI, set `VIEWPORT_TEST_BASE_URL` to the deployed environment URL. Tests will
run headlessly against the deployed app and report any responsive regressions.