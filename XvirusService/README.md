# XvirusService

Xvirus Background Service 5.1

## Table of Contents

- [XvirusService](#xvirusservice)
  - [Table of Contents](#table-of-contents)
  - [Minimum Requirements](#minimum-requirements)
  - [Get Started](#get-started)
  - [REST API](#rest-api)
    - [Settings](#settings)
    - [History](#history)
    - [Rules](#rules)
    - [Quarantine](#quarantine)
    - [Update](#update)
    - [Network](#network)
    - [Actions](#actions)
    - [Server-Sent Events](#server-sent-events)
  - [SSE Event Types](#sse-event-types)
  - [Background Services](#background-services)
  - [App Settings](#app-settings)
  - [Engine Settings](#engine-settings)
  - [Exceptions](#exceptions)

## Minimum Requirements

To run XvirusService you need:

- Windows 10 1607 or later / Windows Server 2012 or later
- .NET 8 Runtime - [download](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- Administrator privileges (required for WMI process monitoring and Windows Service registration)

## Get Started

XvirusService is a Windows Service that exposes a local HTTP API on port **5236** and provides real-time protection, network monitoring, automatic updates, and scan capabilities to the Xvirus UI.

Place the `Database` folder, `settings.json`, and `appsettings.json` next to the executable. On first run (outside of Windows Service mode) the service starts immediately and also launches `XvirusUI.exe` if it is found in the same folder and is not already running.

**Install as a Windows Service:**

```powershell
sc create XvirusService binPath="C:\Path\To\XvirusService.exe"
sc start XvirusService
```

The service base URL is `http://localhost:5236`.

## REST API

All endpoints accept and return JSON. On error, endpoints return a `400 Bad Request` with the following body:

```json
{ "error": "Error message here" }
```

### Settings

| Method | Endpoint      | Description                                             |
|--------|---------------|---------------------------------------------------------|
| GET    | `/settings`   | Returns both engine settings and app settings combined. |
| PUT    | `/settings`   | Saves and reloads both engine settings and app settings. If `StartWithWindows` changed, the Windows startup entry is updated automatically. |

**GET `/settings` response:**

```json
{
  "settings": { /* SettingsDTO */ },
  "appSettings": { /* AppSettingsDTO */ }
}
```

### History

| Method | Endpoint    | Description                              |
|--------|-------------|------------------------------------------|
| GET    | `/history`  | Returns all persisted scan history logs. |
| DELETE | `/history`  | Clears the scan history log.             |

### Rules

| Method | Endpoint           | Description                                    |
|--------|--------------------|------------------------------------------------|
| GET    | `/rules`           | Returns all allow/block rules.                 |
| POST   | `/rules/allow`     | Adds an allow rule. Body: `"<path>"` (string). |
| POST   | `/rules/block`     | Adds a block rule. Body: `"<path>"` (string).  |
| DELETE | `/rules/{id}`      | Removes the rule with the given ID.            |

### Quarantine

| Method | Endpoint                      | Description                                              |
|--------|-------------------------------|----------------------------------------------------------|
| GET    | `/quarantine`                 | Returns all quarantined files.                           |
| DELETE | `/quarantine/{id}`            | Permanently deletes the quarantined entry with given ID. |
| POST   | `/quarantine/{id}/restore`    | Restores the quarantined file to its original location.  |

### Update

| Method | Endpoint              | Description                                                         |
|--------|-----------------------|---------------------------------------------------------------------|
| GET    | `/update/lastcheck`   | Returns the timestamp of the last update check.                     |
| POST   | `/update/check`       | Triggers an immediate database and AI model update check.           |

**POST `/update/check` response:**

```json
{
  "message": "Database was updated!",
  "lastUpdateCheck": "2025-01-01T12:00:00Z"
}
```

`message` is one of:
- `"There is a new SDK version available!"`
- `"Database was updated!"`
- `"Database is up-to-date!"`

### Network

| Method | Endpoint                  | Description                                        |
|--------|---------------------------|----------------------------------------------------|
| GET    | `/network/connections`    | Returns the current list of active network connections with associated process information. |

### Actions

| Method | Endpoint              | Description                                                                    |
|--------|-----------------------|--------------------------------------------------------------------------------|
| GET    | `/actions/pending`    | Returns all threats awaiting a user decision.                                  |
| POST   | `/actions/{id}`       | Submits a user decision for the pending threat with the given ID. Body: `{ "action": "quarantine" \| "allow", "rememberDecision": true \| false }` |

### Server-Sent Events

| Method | Endpoint    | Description                                                      |
|--------|-------------|------------------------------------------------------------------|
| GET    | `/events`   | Opens a persistent SSE stream. The service pushes events here whenever protection status changes or an update runs. |

## SSE Event Types

Events follow the standard SSE wire format: `event: <type>\ndata: <json>\n\n`.

| Event             | Payload                             | Description                                               |
|-------------------|-------------------------------------|-----------------------------------------------------------|
| `updating`        | `{ "message": "..." }`              | Sent when the AutoUpdater begins a database update check. |
| `update-complete` | `{ "message": "..." }`              | Sent when the update check finishes. `message` is the result string. |
| `threat`          | `ThreatEventDTO`                    | Sent by real-time or network protection when a threat is detected. |

## Background Services

| Service                      | Description                                                                                          |
|------------------------------|------------------------------------------------------------------------------------------------------|
| `RealTimeProtection`         | Monitors new process launches via WMI (`Win32_ProcessStartTrace`) and scans each executable.        |
| `NetworkRealTimeProtection`  | Polls active network connections with `netstat -ano` every 3 seconds and scans new process executables. |
| `AutoUpdater`                | Runs a database and AI model update check at service startup (if `CheckSDKUpdates` is enabled), then broadcasts progress via SSE. |
| `ScannerService`             | Wraps `BaseLibrary.Scanner` for use by protection services.                                          |
| `ServerEventService`         | Manages all connected SSE clients and broadcasts events to them.                                     |
| `ThreatAlertService`         | Queues threats that require a user decision (`ThreatAction = "ask"`) and resolves them on response. |
| `SettingsService`            | Loads and caches `settings.json` and `appsettings.json`; reloads on demand.                         |
| `WindowsStartupService`      | Adds or removes the service from the Windows startup registry based on `StartWithWindows`.           |
| `NetworkService`             | Resolves active TCP/UDP connections to their owning processes.                                       |

## App Settings

App settings are stored in `appsettings.json` in the service root folder:

| Setting               | Type    | Default    | Description                                                          |
|-----------------------|---------|------------|----------------------------------------------------------------------|
| `Language`            | string  | `"en"`     | UI language. Supported: `en`, `pt`.                                  |
| `DarkMode`            | bool    | `true`     | Enables dark mode in the UI.                                         |
| `StartWithWindows`    | bool    | `true`     | Registers the service to start automatically with Windows.           |
| `EnableContextMenu`   | bool    | `false`    | Adds a right-click context menu entry for scanning files.            |
| `PasswordProtection`  | bool    | `false`    | Requires a password to access the UI settings.                       |
| `EnableLogs`          | bool    | `true`     | Enables scan history logging.                                        |
| `OnlyScanExecutables` | bool    | `true`     | Restricts real-time protection to PE (executable) files only.        |
| `AutoQuarantine`      | bool    | `false`    | Automatically quarantines detected threats without user confirmation. |
| `ScheduledScan`       | string  | `"off"`    | Scheduled scan frequency. Values: `off`, `daily`, `weekly`, `monthly`. |
| `RealTimeProtection`  | bool    | `true`     | Enables real-time process monitoring.                                |
| `ThreatAction`        | string  | `"ask"`    | What to do on threat detection. Values: `auto` (quarantine immediately), `ask` (prompt user). |
| `BehaviorProtection`  | bool    | `false`    | Enables behavior-based protection (reserved).                        |
| `CloudScan`           | bool    | `false`    | Enables cloud-assisted scanning (reserved).                          |
| `NetworkProtection`   | bool    | `true`     | Enables network connection monitoring.                               |
| `SelfDefense`         | bool    | `false`    | Protects the service process from tampering (reserved).              |
| `ShowNotifications`   | bool    | `true`     | Shows OS notifications for threats and update events.                |

## Engine Settings

Engine settings are stored in `settings.json`. See the [root README](../README.md#settings) for the full reference.

## Exceptions

If a service encounters an unhandled error it is logged to `errorlog.txt` in the service root folder.
