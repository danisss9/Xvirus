# XvirusCLI

Xvirus Command Line Interface 5.1

## Table of Contents

- [XvirusCLI](#xviruscli)
  - [Table of Contents](#table-of-contents)
  - [Minimum Requirements](#minimum-requirements)
  - [Get Started](#get-started)
  - [Commands](#commands)
  - [Interactive Mode](#interactive-mode)
  - [Settings](#settings)
    - [Engine Settings](#engine-settings)
    - [Scan Levels](#scan-levels)
    - [File Size Limits](#file-size-limits)
    - [Update Settings](#update-settings)
  - [Known Issues](#known-issues)
  - [Exceptions](#exceptions)

## Minimum Requirements

To use Xvirus CLI you need:

- .NET 8 Runtime - [download](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

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

Place the `Database` folder and `settings.json` next to the `XvirusCLI` executable, then run a command:

```bash
XvirusCLI scan "C:\path\to\file.exe"
```

## Commands

| Command                   | Alias     | Description                                                                                      |
|---------------------------|-----------|--------------------------------------------------------------------------------------------------|
| `load [true]`             | `l`       | Loads the scan engine into memory. Pass `true` to force a reload.                               |
| `unload`                  | `u`       | Unloads the scan engine from memory.                                                             |
| `scan <path>`             | `s`       | Scans the file at `<path>` and prints the result as JSON.                                        |
| `scanfolder <path>`       | `sf`      | Scans all files in `<path>` recursively and prints results as JSON.                              |
| `update [true]`           | `up`      | Checks and downloads the latest databases and AI model. Pass `true` to reload the engine after. |
| `settings`                | `st`      | Prints the current `settings.json` as JSON.                                                     |
| `logging [true\|false]`   | `log`     | Gets or sets logging. Omit the argument to only print the current state.                        |
| `basefolder [path]`       | `bf`      | Gets or sets the base folder path. Omit the argument to only print the current path.            |
| `version`                 | `v`       | Prints the CLI version.                                                                          |
| `interactive`             | `i`       | Enters interactive mode (see below).                                                             |
| `quit`                    | `q`       | Exits interactive mode.                                                                          |

### Examples

```bash
# Scan a single file
XvirusCLI scan "C:\Windows\System32\notepad.exe"

# Scan a folder
XvirusCLI scanfolder "C:\Users\user\Downloads"

# Update databases and reload engine
XvirusCLI update true

# Print current settings
XvirusCLI settings

# Enable logging
XvirusCLI logging true

# Print SDK version
XvirusCLI version
```

## Interactive Mode

Interactive mode allows running multiple commands in a single session without reloading the engine each time, improving performance for repeated scans.

```bash
XvirusCLI interactive
```

Once started, type commands one per line. Enter `quit` or `q` to exit.

```
load
scan C:\path\to\file1.exe
scan C:\path\to\file2.exe
quit
```

## Settings

Settings are located in the `settings.json` file next to the executable. Available options:

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
- **DatabaseVersion** - Key-value list of database file versions. Updated automatically by the `update` command.

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

## Known Issues

- The `update` command can check for SDK/CLI updates but cannot update the CLI automatically.

## Exceptions

If any command fails, an error message is printed to stderr.

All exceptions are logged in the `errorlog.txt` file next to the executable.
