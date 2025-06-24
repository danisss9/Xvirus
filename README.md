# Xvirus

Xvirus SDK 5.0

## Table of Contents

- [Xvirus](#xvirus)
  - [Table of Contents](#table-of-contents)
  - [Minimum Requirements](#minimum-requirements)
  - [Changelog](#changelog)
  - [Known Issues](#known-issues)
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
  - Windows Server Core	2012
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

## Changelog

- Version **5.0**:
  - Updated to .NET 8
  - XvirusAI is now out of beta
  - XvirusAI now works on C++ bindings
  - Improved performance of heuristics engine
  - Added new settings: `EnableSignatures, HeuristicsLevel, AILevel, MaxHeuristicsPeScanLength, MaxHeuristicsOthersScanLength, MaxAIScanLength, CheckSDKUpdates`

- Version **4.2.3**:
  - Fixed Windows scan performance regression
  - Fixed ScanFolder command not working in CLI
  - Fixed ScanFolderString JSON not formatted correctly 
  - Fixed update check always returning there was a update

- Version **4.2.2**:
  - Optimized scanning speed of PDF files
  - ScanResult now returns the file path
  - Added new ScanFolder() and ScanFolderString() functions

- Version **4.2.1**:
  - Optimized scanning speed of big files
  - Optimized scanning speed in Linux version
  
- Version **4.2**:
  - Reduced glibc minimum version to 2.17 on Linux
  - Added "Logging()" function to enable/disable logging
  - Added "BaseFolder()" function to set a custom base folder
  - Added new setting "DatabaseFolder" to set the Database folder path
  - Fixed C++ binding will return "Success=false" correctly when failing to scan a file 

- Version **4.1**:
  - Upgraded from .NET 5 to .NET 7
  - C++ bindings now also support Linux
  - Changed how exceptions are handled in C++ bindings

- Version **4.0**:
  - Completely redone in .NET 5
  - Now supports Linux (CLI and C# bindings only)
  - Added XvirusAI scan engine (BETA)
  - Scan speed is up to 2x faster
  - Fixed memory usage spike when scanning large files
  - Removed file size limit for scanned files by default
  - The checkUpdate function can now check for SDK updates
  - Added 3 new settings "EnableAIScan", "MaxScanLength" and "DatabaseVersion"

## Known Issues

- The checkUpdate function can check for SDK/CLI updates but can't update it automatically
- When loading or scanning files with the AI scanner, the TensorFlow library might print diagnostic information to the console. To suppress these messages, set the environment variable "TF_CPP_MIN_LOG_LEVEL" to "3" before starting the SDK/CLI.

## Get Started

The "`example`" folder contains an example project on how to import and use Xvirus SDK in C# (.NET 8).

You can run it by building it and then running executable file in the output folder.

## Available Functions

You have the following functions available:

- **Load** - Loads Xvirus Scan Engine into memory, if set `force`=true it will reload the scan engine, even if it is already loaded.
- **Unload** - Unloads Xvirus Scan Engine from memory.
- **Scan** - Scans the file located at `filepath`. It will return a [`ScanResult`](#Model).
- **ScanString** - Scans the file located at `filepath`. It will return one of the following strings:
  - "**Safe**" - If no malware is detected.
  - "**Malware**" - If malware is detected but the name isn't known.
  - **_Malware Name_** - If it is malware from a known family (example: "Trojan.Downloader").
  - "**AI.{aiScore}**" - Score of the file using XvirusAI from 0 to 100, the higher the score the more probable it is malicious (example: "AI.99.99").
  - "**File not found!**" - If no file is found in the submitted path.
  - "**File too big!**" - If the file size is bigger than the set limit.
  - "**Could not get file hash!**" - There was an error calculating the hash of the file.
- **ScanFolder** - Scans all the files inside the folder at `folderpath`. It will return an IEnumerable of [`ScanResult`](#Model).
- **ScanFolderString** - Scans all the files inside the folder at `folderpath`. It will return the scan result message for each file scanned.
- **CheckUpdates** - Checks and updates the databases and AI engine to the most recent versions. If `loadDBAfterUpdate`=true then it will reload the Xvirus Scan Engine after the update is done. It can return the following strings:
  - "**There is a new SDK version available!**"
  - "**Database was updated!**"
  - "**Database is up-to-date!**"
- **GetSettings** - returns object representation of the `settings.json` file.
- **GetSettingsAsString** - returns a string representation of the `settings.json` file.
- **Logging** - Sets and returns if `Logging` is enabled. If no `enableLogging` value is provided it will only return.
- **BaseFolder** - Sets and returns the `BaseFolder` path. If no `baseFolder` value is provided it will only return.
- **Version** - returns the version of the SDK/CLI.

## Model

The `scan` and `scanFolder` functions return a class `ScanResult` with the following properties:

```c#
public class ScanResult
{
    public bool IsMalware { get; set; } // true if malware
    public string Name { get; set; } // detection name
    public double MalwareScore { get; set; } // between 0 and 1, higher score means more likely to be malware, -1 if there was an error
    public string Path { get; set; } // file path
}
```

## Settings

Settings are located in the "`settings.json`" file in the root folder of the SDK/CLI. Available options:

### Engine Settings

- **EnableSignatures** - Enables signature-based scanning of files. Default: _true_
- **EnableHeuristics** - Enables heuristics scanning of files. Default: _true_
- **EnableAIScan** - Enables XvirusAI scan engine. Default: _true_

### Scan Levels

- **HeuristicsLevel** - Heuristics aggressiveness level from 1 to 5, higher is more aggressive. Default: _4_
- **AILevel** - AI scan aggressiveness level from 1 to 100, higher is more aggressive. Default: _10_

### File Size Limits

- **MaxScanLength** - Maximum file size to be scanned in bytes. If set "null" then there is no limit. Default: _null_
- **MaxHeuristicsPeScanLength** - Maximum PE file size for heuristics scanning in bytes. If set "null" then there is no limit. Default: _20971520_ (20MB)
- **MaxHeuristicsOthersScanLength** - Maximum non-PE file size for heuristics scanning in bytes. If set "null" then there is no limit. Default: _10485760_ (10MB)  
- **MaxAIScanLength** - Maximum file size for AI scanning in bytes. If set "null" then there is no limit. Default: _20971520_ (20MB)

### Update Settings

- **CheckSDKUpdates** - Enables checking for SDK updates. Default: _true_
- **DatabaseFolder** - Path to the database folder, it accepts both relative and absolute paths. Default: _"Database"_
- **DatabaseVersion** - KeyValue list of database files version. This is updated automatically when using the "checkUpdate()" function.

Example of a `settings.json` file:

```JSON
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

If any of the functions fail it may return an [exception](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/exceptions/).

All exceptions are logged in the `errorlog.txt` file.