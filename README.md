# Xvirus SDK

Xvirus SDK 4.2.3

## Table of Contents

- [Xvirus SDK](#xvirus-sdk)
  - [Table of Contents](#table-of-contents)
  - [Minimum Requirements](#minimum-requirements)
  - [Changelog](#changelog)
  - [Known Issues](#known-issues)
  - [Get Started](#get-started)
  - [Avaiable Functions](#avaiable-functions)
  - [Model](#model)
  - [Settings](#settings)
  - [Exceptions](#exceptions)

## Minimum Requirements

To use Xvirus C# SDK you need:

- .NET 7 SDK - [download](https://dotnet.microsoft.com/en-us/download/dotnet/7.0)

The following Operating Systems are supported:

- Windows:
  - Windows 10 1607
  - Windows 11 22000
  - Windows Server 2012
  - Windows Server Core	2012
- Linux (glibc 2.17):
  - Alpine Linux 3.15
  - CentOS 7
  - Debian 10
  - Fedora 36
  - openSUSE 15
  - Oracle Linux 7
  - Red Hat Enterprise Linux 7
  - SUSE Enterprise Linux (SLES) 12 SP2
  - Ubuntu 18.04

## Changelog

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

- XvirusAI engine is still in BETA. It is not recomended to use in production yet.
- XvirusAI engine does not work in C++ bindings.
- The checkUpdate function can now check for SDK updates but can't update it

## Get Started

The "`example`" folder contains an example project on how to import and use Xvirus SDK in C# (.NET 7).

You can run it by building it and then running executable file in the output folder.

## Avaiable Functions

You have the following functions available:

- **Load** - Loads Xvirus Scan Engine into memory, if set `force`=true it will reload the scan engine, even if it is already loaded.
- **Unload** - Unloads Xvirus Scan Engine from memory.
- **Scan** - Scans the file located at `filepath`. It will return a [`ScanResult`](#Model).
- **ScanString** - Scans the file located at `filepath`. It will return one of the following strings:
  - "**Safe**" - If no malware is detected.
  - "**Malware**" - If malware is detected but the name isn't known.
  - **_Malware Name_** - If it is malware from a known family (example: "Trojan.Downloader").
  - "**AI.{aiScore}**" - Score of the file using XvirusAI from 0 to 100, the higher the score the more probable it is malicious (example: "AI.99").
  - "**File not found!**" - If no file is found in the submited path.
  - "**File too big!**" - If the file size is bigger than the set limit.
  - "**Could not get file hash!**" - There was an error calculating the hash of the file.
- **ScanFolder** - Scans all the files inside the folder at `folderpath`. It will return an IEnumerable of [`ScanResult`](#Model).
- **ScanFolderString** - Scans all the files inside the folder at `folderpath`. It will return the scan result message for each file scanned.
- **CheckUpdates** - Checks and updates the databases and AI engine to the most recent versions. If `checkSDKUpdates`=true then it will also check for SDK updates. If `loadDBAfterUpdate`=true then it will reload the Xvirus Scan Engine after the update is done. It can return the following strings:
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

Settings are located in the "`settings.json`" file in the root folder of the SDK. There are 5 avaiable options:

- **EnableHeuristics** - Enables heuristics scanning of files. Default: _true_
- **EnableAIScan** - Enables XvirusAI scan engine. This feature is still in BETA. Default: _false_
- **MaxScanLength** - Maximum file size to be scanned in bytes. If set "null" then there is no limit. Default: _null_
- **DatabaseFolder** - Path to the database folder, it accepts both relative and absolute paths. Default: _"Database"_
- **DatabaseVersion** - KeyValue list of database files version. This is updated automatically when using the "checkUpdate()" function.

Example of a `settings.json` file:

```JSON
{
  "EnableHeuristics": true,
  "EnableAIScan": false,
  "MaxScanLength": null,
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

If any of the functions fail it may return an [exception](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/exceptions/).

All exceptions are logged in the `errorlog.txt` file.
