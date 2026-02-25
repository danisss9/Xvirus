# CSharpSDK

Xvirus C# SDK 5.1

## Table of Contents

- [CSharpSDK](#csharpsdk)
  - [Table of Contents](#table-of-contents)
  - [Minimum Requirements](#minimum-requirements)
  - [Get Started](#get-started)
  - [Available Functions](#available-functions)
  - [Model](#model)
  - [Settings](#settings)
    - [Engine Settings](#engine-settings)
    - [Scan Levels](#scan-levels)
    - [File Size Limits](#file-size-limits)
    - [Update Settings](#update-settings)
  - [Exceptions](#exceptions)

## Minimum Requirements

To use Xvirus C# SDK you need:

- .NET 8 SDK - [download](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

The following Operating Systems are supported:

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

## Get Started

Add a reference to `XvirusSDK.dll` in your .NET 8 project and place the `Database` folder and `settings.json` next to your executable.

```csharp
using Xvirus;

// Load the scan engine into memory
XvirusSDK.Load();

// Scan a file
var result = XvirusSDK.Scan(@"C:\path\to\file.exe");
Console.WriteLine(result.IsMalware ? $"Malware: {result.Name}" : "Safe");

// Unload when done
XvirusSDK.Unload();
```

## Available Functions

- **Load** - Loads the Xvirus scan engine into memory. If `force`=true it will reload the engine even if already loaded.
- **Unload** - Unloads the Xvirus scan engine from memory.
- **Scan** - Scans the file at `filePath`. Returns a [`ScanResult`](#model).
- **ScanString** - Scans the file at `filePath`. Returns a JSON string representation of [`ScanResult`](#model).
- **ScanFolder** - Scans all files inside `folderPath` recursively. Returns an `IEnumerable<ScanResult>`.
- **ScanFolderString** - Scans all files inside `folderPath` recursively. Returns a JSON string with all scan results.
- **CheckUpdates** - Checks and downloads the latest databases and AI model. If `loadDBAfterUpdate`=true it reloads the engine after the update. Returns one of:
  - `"There is a new SDK version available!"`
  - `"Database was updated!"`
  - `"Database is up-to-date!"`
- **GetSettings** - Returns the current `SettingsDTO` object.
- **GetSettingsString** - Returns a JSON string representation of the current settings.
- **Logging** - Gets or sets logging. If no argument is passed it only returns the current state.
- **BaseFolder** - Gets or sets the base folder path used to resolve relative paths. If no argument is passed it only returns the current path.
- **Version** - Returns the version of the SDK.

## Model

`Scan` and `ScanFolder` return a `ScanResult` object:

```csharp
public class ScanResult
{
    public bool IsMalware { get; set; }   // true if malware
    public string Name { get; set; }      // detection name
    public double MalwareScore { get; set; } // 0–1 probability, -1 if there was an error
    public string Path { get; set; }      // scanned file path
}
```

`Name` can be one of:
- `"Safe"` — no malware detected
- `"Malware"` — malware detected but family unknown
- _Malware family name_ — e.g. `"Trojan.Downloader"`
- `"AI.{score}"` — AI-only verdict, score from `0.00` to `100.00` (e.g. `"AI.99.99"`)
- `"File not found!"` — file does not exist at the given path
- `"File too big!"` — file exceeds the configured size limit
- `"Could not get file hash!"` — error computing the file hash

## Settings

Settings are located in the `settings.json` file in the SDK root folder. Available options:

### Engine Settings

- **EnableSignatures** - Enables signature-based scanning of files. Default: _true_
- **EnableHeuristics** - Enables heuristics scanning of files. Default: _true_
- **EnableAIScan** - Enables XvirusAI scan engine. Default: _true_

### Scan Levels

- **HeuristicsLevel** - Heuristics aggressiveness level from 1 to 5, higher is more aggressive. Default: _4_
- **AILevel** - AI scan aggressiveness level from 1 to 100, higher is more aggressive. Default: _10_

### File Size Limits

- **MaxScanLength** - Maximum file size to be scanned in bytes. `null` = no limit. Default: _null_
- **MaxHeuristicsPeScanLength** - Maximum PE file size for heuristics scanning in bytes. `null` = no limit. Default: _20971520_ (20 MB)
- **MaxHeuristicsOthersScanLength** - Maximum non-PE file size for heuristics scanning in bytes. `null` = no limit. Default: _10485760_ (10 MB)
- **MaxAIScanLength** - Maximum file size for AI scanning in bytes. `null` = no limit. Default: _20971520_ (20 MB)

### Update Settings

- **CheckSDKUpdates** - Enables checking for SDK updates. Default: _true_
- **DatabaseFolder** - Path to the database folder, accepts both relative and absolute paths. Default: _"Database"_
- **DatabaseVersion** - Key-value list of database file versions. Updated automatically by `CheckUpdates()`.

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

## Exceptions

If any function fails it may throw an [exception](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/exceptions/).

All exceptions are logged in the `errorlog.txt` file.
