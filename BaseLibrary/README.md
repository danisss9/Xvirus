# BaseLibrary

Xvirus SDK Core Library 5.1

## Table of Contents

- [BaseLibrary](#baselibrary)
  - [Table of Contents](#table-of-contents)
  - [Overview](#overview)
  - [Minimum Requirements](#minimum-requirements)
  - [Modules](#modules)
  - [Models](#models)
  - [Settings](#settings)
    - [Engine Settings](#engine-settings)
    - [Scan Levels](#scan-levels)
    - [File Size Limits](#file-size-limits)
    - [Update Settings](#update-settings)

## Overview

`BaseLibrary` is the shared core used by all Xvirus SDK bindings (C#, Native, Node). It provides the scanning engine, AI inference, database management, updater, settings, quarantine, and logging.

**Version:** 5.1
**Target:** .NET 8 (AOT compatible)
**Dependencies:** `Microsoft.ML.OnnxRuntime`, `SixLabors.ImageSharp`

## Minimum Requirements

- .NET 8 SDK - [download](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

## Modules

| Class        | Description                                                                                       |
|--------------|---------------------------------------------------------------------------------------------------|
| `Scanner`    | Main scan engine. Runs signature, heuristics, and AI checks against a file or a folder.           |
| `AI`         | Loads and runs the ONNX model (`model.ai`). Converts PE files to grayscale images for inference.  |
| `DB`         | Loads all database files (hash lists, heuristics patterns, vendor list) into memory.              |
| `Updater`    | Downloads updated database files and AI model from the Xvirus update server.                      |
| `Settings`   | Reads and writes `settings.json` and `appsettings.json`.                                          |
| `Rules`      | Manages allow/block rules applied before scanning.                                                |
| `Quarantine` | Moves detected files to a quarantine folder and tracks quarantine entries.                        |
| `Logger`     | Logs exceptions to `errorlog.txt`.                                                                |
| `Utils`      | Shared helpers: path resolution, file reading, certificate extraction, version info.              |
| `Aho`        | Aho-Corasick pattern matcher used by the heuristics engine.                                       |

### Scanner

```csharp
var scanner = new Scanner(settings, database, ai, rules);

ScanResult result = scanner.ScanFile(filePath);
IEnumerable<ScanResult> results = scanner.ScanFolder(folderPath);
```

`ScanFile` applies rules, then runs signature → heuristics → AI checks in order, returning early as soon as a verdict is reached.

### AI

```csharp
var ai = new AI(settings);
float score = ai.ScanFile(filePath);            // 0.0–1.0 malware probability, -1 on error
float[] data = AI.GetInputData(filePath);       // preprocessed tensor input
byte[] png  = AI.GetFileImageBytes(filePath);   // grayscale PNG visualization
```

Files are mapped to 224×224 grayscale images (width adapts to file size) and normalized with ImageNet statistics before inference.

### Updater

```csharp
string result = Updater.CheckUpdates(settings);
// Returns: "Database was updated!", "Database is up-to-date!", or "There is a new SDK version available!"
```

### Settings

```csharp
SettingsDTO    s  = Settings.Load();
AppSettingsDTO as = Settings.LoadAppSettings();
Settings.Save(s);
Settings.SaveAppSettings(as);
```

## Models

### ScanResult

```csharp
public class ScanResult
{
    public bool   IsMalware    { get; set; } // true if malware
    public string Name         { get; set; } // detection name
    public double MalwareScore { get; set; } // 0–1 probability, -1 on error
    public string Path         { get; set; } // scanned file path
}
```

### SettingsDTO

Configuration for the scan engine, loaded from `settings.json`.

### AppSettingsDTO

Application-level configuration, loaded from `appsettings.json`.

## Settings

Settings are stored in `settings.json` in the root folder of the consuming application.

### Engine Settings

- **EnableSignatures** — Enables signature-based scanning. Default: _true_
- **EnableHeuristics** — Enables heuristics scanning. Default: _true_
- **EnableAIScan** — Enables XvirusAI ONNX scan engine. Default: _true_

### Scan Levels

- **HeuristicsLevel** — Heuristics aggressiveness from 1 to 5, higher is more aggressive. Default: _4_
- **AILevel** — AI aggressiveness from 1 to 100, higher is more aggressive. Default: _10_

### File Size Limits

- **MaxScanLength** — Maximum file size to scan in bytes. `null` = no limit. Default: _null_
- **MaxHeuristicsPeScanLength** — Maximum PE file size for heuristics in bytes. Default: _20971520_ (20 MB)
- **MaxHeuristicsOthersScanLength** — Maximum non-PE file size for heuristics in bytes. Default: _10485760_ (10 MB)
- **MaxAIScanLength** — Maximum file size for AI scanning in bytes. Default: _20971520_ (20 MB)

### Update Settings

- **CheckSDKUpdates** — Enables checking for SDK version updates. Default: _true_
- **DatabaseFolder** — Path to the database folder (relative or absolute). Default: _"Database"_
- **DatabaseVersion** — Tracks the version of each database file, updated automatically by `Updater.CheckUpdates()`.

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
