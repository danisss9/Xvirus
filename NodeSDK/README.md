# Xvirus Node.js SDK

Xvirus SDK 5.1 — Node.js native addon built with .NET Native AOT.

The module ships as a pre-compiled `.node` binary paired with a generated `.js` ESM wrapper. No .NET runtime is required on the target machine.

## Table of Contents

- [Xvirus Node.js SDK](#xvirus-nodejs-sdk)
  - [Table of Contents](#table-of-contents)
  - [Minimum Requirements](#minimum-requirements)
  - [Build](#build)
  - [Get Started](#get-started)
  - [Available Functions](#available-functions)
  - [Model](#model)
  - [Settings](#settings)
    - [Engine Settings](#engine-settings)
    - [Scan Levels](#scan-levels)
    - [File Size Limits](#file-size-limits)
    - [Update Settings](#update-settings)
  - [Errors](#errors)

## Minimum Requirements

To **build** the native module you need:

- .NET 8 SDK — [download](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

To **run** the native module you need:

- Node.js 18 or later

The following operating systems are supported:

- Windows:
  - Windows 10 1607
  - Windows 11 22H2
  - Windows Server 2012
  - Windows Server Core 2012
- Linux (glibc 2.35):
  - Alpine Linux 3.19
  - Azure Linux 3.0
  - CentOS Stream 9
  - Debian 12
  - Fedora 41
  - openSUSE Leap 15.6
  - Red Hat Enterprise Linux 8
  - SUSE Enterprise Linux 15.6
  - Ubuntu 22.04

## Build

Run `dotnet publish` from the `NodeSDK` directory, specifying your target [Runtime Identifier](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog):

```bash
# Windows x64
dotnet publish -r win-x64 -c Release

# Linux x64
dotnet publish -r linux-x64 -c Release

# Linux arm64
dotnet publish -r linux-arm64 -c Release
```

The build produces two files in the publish output directory:

| File                 | Description                                  |
| -------------------- | -------------------------------------------- |
| `XvirusNodeSDK.node` | Native binary (loaded automatically)         |
| `XvirusNodeSDK.js`   | Generated ESM wrapper — **import this file** |
| `XvirusNodeSDK.d.ts` | TypeScript type definitions                  |

## Get Started

Import `XvirusNodeSDK` from the generated `.js` wrapper. The path must point to the publish output directory.

```js
import { XvirusNodeSDK } from './publish/XvirusNodeSDK.js';

// Load the engine (done once at startup)
XvirusNodeSDK.load();

// Scan a file
const result = XvirusNodeSDK.scan('/path/to/file.exe');

if (result.isMalware) {
  console.log(`Malware detected: ${result.name} (score: ${result.malwareScore})`);
} else {
  console.log('File is clean');
}

// Unload when done
XvirusNodeSDK.unload();
```

TypeScript example:

```ts
import { XvirusNodeSDK, ScanResultNode } from './publish/XvirusNodeSDK.js';

XvirusNodeSDK.load();

const result: ScanResultNode = XvirusNodeSDK.scan('/path/to/file.exe');
console.log(result.isMalware, result.name, result.malwareScore);
```

> **Note:** All method names follow JavaScript camelCase conventions. For example, the C# method `ScanFolder` is called as `scanFolder` in JavaScript.

## Available Functions

All functions are accessed through the `XvirusNodeSDK` named export.

- **`load(force?: boolean)`** — Loads the Xvirus scan engine into memory. If `force` is `true`, reloads the engine even if it is already loaded.

- **`unload()`** — Unloads the scan engine from memory and releases held resources.

- **`scan(filePath: string): ScanResultNode`** — Scans the file at `filePath`. Returns a [`ScanResultNode`](#model) object. Throws an error if the file cannot be scanned.

- **`scanAsString(filePath: string): string`** — Scans the file at `filePath`. Returns the result as a JSON string.

- **`scanFolder(folderPath: string): ScanResultNode[]`** — Scans all files inside the folder at `folderPath`. Returns an array of [`ScanResultNode`](#model) objects.

- **`scanFolderAsString(folderPath: string): string`** — Scans all files inside the folder at `folderPath`. Returns the results as a JSON string.

- **`checkUpdates(loadDBAfterUpdate?: boolean): string`** — Checks for database and SDK updates. If `loadDBAfterUpdate` is `true`, reloads the engine after a successful update. Returns one of the following strings:
  - `"There is a new SDK version available!"`
  - `"Database was updated!"`
  - `"Database is up-to-date!"`

- **`getSettings(): string`** — Returns the current engine settings as a JSON string.

- **`logging(enableLogging?: boolean): boolean`** — Gets or sets whether logging is enabled. Omit the argument to query the current state without changing it.

- **`baseFolder(baseFolder?: string): string`** — Gets or sets the base folder used to locate databases and resources. Omit the argument to query the current path without changing it.

- **`version(): string`** — Returns the SDK version string.

## Model

`scan` and `scanFolder` return a `ScanResultNode` object with the following properties:

```ts
interface ScanResultNode {
  isMalware: boolean; // true if the file is classified as malware
  name: string; // detection name, empty string when the file is clean
  malwareScore: number; // probability score [0, 1]; higher means more likely malicious
  path: string; // absolute path of the scanned file
}
```

## Settings

Settings are read from the `settings.json` file in the SDK base folder. Available options:

### Engine Settings

- **EnableSignatures** — Enables signature-based scanning. Default: _true_
- **EnableHeuristics** — Enables heuristics scanning. Default: _true_
- **EnableAIScan** — Enables the XvirusAI scan engine. Default: _true_

### Scan Levels

- **HeuristicsLevel** — Heuristics aggressiveness from 1 to 5; higher is more aggressive. Default: _4_
- **AILevel** — AI scan aggressiveness from 1 to 100; higher is more aggressive. Default: _10_

### File Size Limits

- **MaxScanLength** — Maximum file size to scan in bytes. Set `null` for no limit. Default: _null_
- **MaxHeuristicsPeScanLength** — Maximum PE file size for heuristics scanning in bytes. Set `null` for no limit. Default: _20971520_ (20 MB)
- **MaxHeuristicsOthersScanLength** — Maximum non-PE file size for heuristics scanning in bytes. Set `null` for no limit. Default: _10485760_ (10 MB)
- **MaxAIScanLength** — Maximum file size for AI scanning in bytes. Set `null` for no limit. Default: _20971520_ (20 MB)

### Update Settings

- **CheckSDKUpdates** — Enables checking for SDK updates when calling `checkUpdates()`. Default: _true_
- **DatabaseFolder** — Path to the database folder; accepts relative and absolute paths. Default: _"Database"_
- **DatabaseVersion** — Key-value list of database file versions. Updated automatically by `checkUpdates()`.

Example `settings.json`:

```json
{
  "EnableSignatures": true,
  "EnableHeuristics": true,
  "EnableAIScan": true,
  "HeuristicsLevel": 4,
  "AILevel": 10,
  "MaxScanLength": null,
  "MaxHeuristicsPeScanLength": 20971520,
  "MaxHeuristicsOthersScanLength": 10485760,
  "MaxAIScanLength": 20971520,
  "CheckSDKUpdates": true,
  "DatabaseFolder": "Database",
  "DatabaseVersion": {
    "AIModel": 0,
    "MainDB": 0,
    "DailyDB": 0,
    "WhiteDB": 0,
    "DailywlDB": 0,
    "HeurDB": 0,
    "HeurDB2": 0,
    "MalvendorDB": 0
  }
}
```

## Errors

`scan` throws a JavaScript `Error` when a file cannot be scanned (file not found, file too large, hash calculation failure, etc.). All errors are also logged to `errorlog.txt` in the base folder.

```js
try {
  const result = XvirusNodeSDK.scan('/path/to/file.exe');
} catch (err) {
  console.error('Scan failed:', err.message);
}
```
