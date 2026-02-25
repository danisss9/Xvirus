# XvirusUI

Xvirus User Interface 5.1

## Table of Contents

- [XvirusUI](#xvirusui)
  - [Table of Contents](#table-of-contents)
  - [Minimum Requirements](#minimum-requirements)
  - [Get Started](#get-started)
  - [App Modes](#app-modes)
  - [Build Scripts](#build-scripts)
  - [Project Structure](#project-structure)
  - [Views](#views)
  - [API Modules](#api-modules)
  - [Window Behaviour](#window-behaviour)
  - [SSE Integration](#sse-integration)

## Minimum Requirements

To develop or build XvirusUI you need:

- Node.js 20+ - [download](https://nodejs.org/)
- Neutralino CLI - [install](https://neutralino.js.org/docs/getting-started/installation)

To run XvirusUI you need:

- XvirusService running on `http://localhost:5236`
- Windows 10 1607 or later

## Get Started

Install dependencies and build the UI:

```bash
npm install
npm run dev:am   # Antimalware mode — build, package, and run
```

Or to run an already-built package:

```bash
npm start
```

To build the XvirusService in the same step:

```bash
npm run prod:service
```

## App Modes

XvirusUI ships a single codebase that renders in one of two modes, configured via `neutralino.config.json → globalVariables`:

| Variable          | Mode             | Description                                              |
|-------------------|------------------|----------------------------------------------------------|
| `IS_FIREWALL`     | Firewall mode    | Shows network-focused views; hides scan, behavior, and some settings options. |
| `IS_ANTIMALWARE`  | Antimalware mode | Shows the full feature set including scan, history, and all settings.         |

Change the active mode by editing `neutralino.config.json`:

```json
"globalVariables": {
  "IS_FIREWALL": false,
  "IS_ANTIMALWARE": true
}
```

## Build Scripts

| Script            | Description                                                                  |
|-------------------|------------------------------------------------------------------------------|
| `dev:am`          | Builds the frontend with Vite, packages with `neu build`, and runs in antimalware mode. |
| `dev:fw`          | Builds the frontend with Vite, packages with `neu build`, and runs in firewall mode. |
| `prod:am`         | Builds a release binary in antimalware mode.                                 |
| `prod:fw`         | Builds a release binary in firewall mode.                                    |
| `start`           | Runs the already-built Neutralino package without rebuilding.                |
| `prod:service`    | Publishes XvirusService using its Windows publish profile.                   |

## Project Structure

```
XvirusUI/
├── src/
│   ├── index.html          # App entry point
│   ├── main.tsx            # Preact root mount
│   ├── App.tsx             # Root component, initialises Neutralino on mount
│   ├── api/                # Typed fetch wrappers for each XvirusService endpoint
│   ├── components/         # Shared UI components (BottomNav, WindowControls)
│   ├── model/              # TypeScript interfaces mirroring C# DTOs
│   ├── services/
│   │   ├── env.ts          # Mode detection (isFirewall / isAntimalware)
│   │   └── neutralino.ts   # Neutralino API wrappers, SSE subscription, tray setup
│   ├── styles/
│   │   └── app.css         # Global CSS
│   └── views/              # One component per screen
├── browser/                # Vite build output (served by Neutralino)
├── bin/                    # Neutralino runtime binaries (all platforms)
├── neutralino.config.json  # Neutralino window and build configuration
├── vite.config.ts          # Vite configuration (root: src, outDir: browser)
└── package.json
```

## Views

| View                  | Description                                                                 |
|-----------------------|-----------------------------------------------------------------------------|
| `HomeView`            | Protection status dashboard. Shows real-time shield state and quick actions. |
| `HistoryView`         | Scan and threat event history. Supports clearing history and adding rules.  |
| `SettingsView`        | Engine and app settings editor. Filters options based on the active mode.   |
| `ScanningView`        | On-demand file and folder scan UI. _(Antimalware mode only)_                |
| `NetworkMonitorView`  | Live view of active network connections with process details.               |
| `AlertView`           | Modal shown when a threat requires a user decision (quarantine or allow).   |

## API Modules

Each module in `src/api/` wraps the corresponding XvirusService REST endpoint:

| Module             | Endpoints covered                                 |
|--------------------|---------------------------------------------------|
| `settingsApi.ts`   | `GET /settings`, `PUT /settings`                  |
| `historyApi.ts`    | `GET /history`, `DELETE /history`                 |
| `rulesApi.ts`      | `GET /rules`, `POST /rules/allow`, `POST /rules/block`, `DELETE /rules/{id}` |
| `quarantineApi.ts` | `GET /quarantine`, `DELETE /quarantine/{id}`, `POST /quarantine/{id}/restore` |
| `updateApi.ts`     | `GET /update/lastcheck`, `POST /update/check`     |
| `networkApi.ts`    | `GET /network/connections`                        |
| `actionsApi.ts`    | `GET /actions/pending`, `POST /actions/{id}`      |

## Window Behaviour

- **Size:** 500 × 800 px, not resizable.
- **Position:** Placed at the bottom-right corner of the primary monitor on launch (above the taskbar).
- **Borderless:** Custom title bar with draggable header. Minimize and close buttons are rendered by `WindowControls`.
- **Close behaviour:** Clicking the OS close button hides the window rather than quitting. The app stays alive in the system tray.
- **System tray:** Provides _Open_ and _Quit_ menu items. Clicking the tray icon shows and focuses the window.

## SSE Integration

XvirusUI subscribes to `http://localhost:5236/events` at startup and maintains a persistent connection that auto-reconnects every 5 seconds on failure.

Incoming events are dispatched to view components via `onServerEvent()`:

| Event             | UI response                                                                          |
|-------------------|--------------------------------------------------------------------------------------|
| `updating`        | OS notification shown; HomeView update card switches to loading state.               |
| `update-complete` | OS notification shown; HomeView update card refreshes with the new last-check time.  |
| `threat`          | If action is required: AlertView opens and the window is brought to the foreground. If auto-quarantined: OS notification shown. |
