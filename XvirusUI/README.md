# XvirusUI

Xvirus User Interface 5.1.2

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

- Node.js 26+ (current LTS) - [download](https://nodejs.org/)
- Rust (stable, MSVC toolchain) - [install](https://www.rust-lang.org/tools/install)
- Tauri prerequisites for Windows (Microsoft C++ Build Tools, WebView2) - [guide](https://v2.tauri.app/start/prerequisites/)

The Tauri CLI is installed locally by `npm install` (`@tauri-apps/cli`).

To run XvirusUI you need:

- XvirusService running on `http://localhost:5236` (Anti-Malware) or `http://localhost:5237` (Firewall)
- Windows 10 1607 or later with the Microsoft Edge WebView2 Runtime (preinstalled on Windows 11)

## Get Started

Install dependencies and build the UI:

```bash
npm install
npm run dev:am   # Antimalware mode — Vite dev server + Tauri window with hot reload
```

To build a release binary (`src-tauri/target/release/xvirus-ui.exe`):

```bash
npm run prod:am
```

To publish the XvirusService for a mode:

```bash
npm run prod:service:am
```

## App Modes

XvirusUI ships a single codebase that renders in one of two modes, chosen at build time:

| Mode | Vite mode   | Tauri config                                          | Description |
|------|-------------|-------------------------------------------------------|-------------|
| `am` | `--mode am` | `src-tauri/tauri.conf.json` (default)                 | Antimalware: full feature set including scan, history, and all settings. Talks to port 5236. |
| `fw` | `--mode fw` | `src-tauri/tauri.conf.json` + `tauri.fw.conf.json`    | Firewall: network-focused views; hides scan, behavior, and some settings options. Talks to port 5237. |

The Vite mode is injected as `__XVIRUS_MODE__` (see `vite.config.ts`) and read by `src/services/env.ts`.
The Tauri config sets the product name (window title, tray tooltip), version, and identifier.

## Build Scripts

| Script            | Description                                                                  |
|-------------------|------------------------------------------------------------------------------|
| `dev:am`          | Runs the Vite dev server and the Tauri app in antimalware mode (`tauri dev`). |
| `dev:fw`          | Runs the Vite dev server and the Tauri app in firewall mode.                 |
| `prod:am`         | Builds a release binary in antimalware mode (`tauri build --no-bundle`).     |
| `prod:fw`         | Builds a release binary in firewall mode.                                    |
| `web:build:am` / `web:build:fw` | Builds only the frontend into `browser/`.                      |
| `prod:service:am` / `prod:service:fw` | Publishes XvirusService using its Windows publish profile. |

## Project Structure

```
XvirusUI/
├── src/
│   ├── index.html          # App entry point
│   ├── main.tsx            # Preact root mount
│   ├── App.tsx             # Root component, starts the SSE subscription on mount
│   ├── api/                # Typed fetch wrappers for each XvirusService endpoint
│   ├── components/         # Shared UI components (BottomNav, WindowControls)
│   ├── model/              # TypeScript interfaces mirroring C# DTOs
│   ├── services/
│   │   ├── env.ts          # Mode detection (isFirewall / isAntimalware)
│   │   └── native.ts       # Tauri API wrappers (window, notifications, dialogs), SSE subscription
│   ├── styles/
│   │   └── app.css         # Global CSS
│   └── views/              # One component per screen
├── src-tauri/
│   ├── src/main.rs         # Native shell: window placement, hide-on-close, system tray, commands
│   ├── capabilities/       # Permissions granted to the webview
│   ├── icons/              # App / tray icons
│   ├── tauri.conf.json     # Window and build configuration (antimalware mode)
│   └── tauri.fw.conf.json  # Overrides for firewall mode
├── browser/                # Vite build output (embedded into the exe by Tauri)
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
- **Startup:** The window starts hidden; it is shown from the tray or when a threat needs a decision.
- **Notifications:** On startup the app registers its AppUserModelID (the Tauri `identifier`) under `HKCU\Software\Classes\AppUserModelId` so Windows shows its toast notifications.

Window placement, hide-on-close, and the tray are implemented natively in `src-tauri/src/main.rs`.
The header is draggable through the `data-tauri-drag-region` attribute.

## SSE Integration

XvirusUI subscribes to `<service>/events` (port 5236 or 5237 depending on mode) at startup and maintains a persistent connection that auto-reconnects every 5 seconds on failure.

Incoming events are dispatched to view components via `onServerEvent()`:

| Event             | UI response                                                                          |
|-------------------|--------------------------------------------------------------------------------------|
| `updating`        | OS notification shown; HomeView update card switches to loading state.               |
| `update-complete` | OS notification shown; HomeView update card refreshes with the new last-check time.  |
| `threat`          | If action is required: AlertView opens and the window is brought to the foreground. If auto-quarantined: OS notification shown. |
