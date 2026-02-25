# NativeSDK

Xvirus Native SDK 5.1

## Table of Contents

- [NativeSDK](#nativesdk)
  - [Table of Contents](#table-of-contents)
  - [Minimum Requirements](#minimum-requirements)
  - [Get Started](#get-started)
  - [Available Functions](#available-functions)
  - [Structs](#structs)
  - [Settings](#settings)
    - [Engine Settings](#engine-settings)
    - [Scan Levels](#scan-levels)
    - [File Size Limits](#file-size-limits)
    - [Update Settings](#update-settings)
  - [Exceptions](#exceptions)

## Minimum Requirements

To use Xvirus Native SDK you need:

- .NET 8 Runtime (AOT, self-contained) — no external .NET install required in the final binary

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

The NativeSDK is compiled as an AOT native shared library (`XvirusSDK.dll` on Windows, `XvirusSDK.so` on Linux). It can be loaded from any language that supports C-compatible FFI (C, C++, Python, Rust, etc.).

Place the `Database` folder and `settings.json` next to the library before calling any function.

**C++ example:**

```cpp
#include <windows.h>
#include <iostream>

typedef struct { bool Sucess; const wchar_t* Result; const wchar_t* Error; } ActionResult;
typedef struct { bool Sucess; const wchar_t* Error; bool IsMalware; const wchar_t* Name; double MalwareScore; const wchar_t* Path; } ScanResult;

typedef ActionResult (*LoadFn)(bool);
typedef ScanResult   (*ScanFn)(const wchar_t*);

int main() {
    HMODULE lib = LoadLibraryW(L"XvirusSDK.dll");
    LoadFn load = (LoadFn)GetProcAddress(lib, "load");
    ScanFn scan = (ScanFn)GetProcAddress(lib, "scan");

    load(false);
    ScanResult r = scan(L"C:\\path\\to\\file.exe");
    std::wcout << (r.IsMalware ? L"Malware: " : L"Safe: ") << r.Name << std::endl;

    FreeLibrary(lib);
}
```

## Available Functions

All functions use C-compatible calling conventions. Strings are passed and returned as UTF-16 (`wchar_t*` / `IntPtr` to Unicode).

| Entry point        | Parameters                     | Returns        | Description                                                                 |
|--------------------|--------------------------------|----------------|-----------------------------------------------------------------------------|
| `load`             | `bool force`                   | `ActionResult` | Loads the scan engine. If `force`=true reloads even if already loaded.      |
| `unload`           | —                              | `ActionResult` | Unloads the scan engine from memory.                                        |
| `scan`             | `wchar_t* filePath`            | `ScanResult`   | Scans a single file. Returns a `ScanResult` struct.                         |
| `scanAsString`     | `wchar_t* filePath`            | `ActionResult` | Scans a single file. Returns result as a JSON string in `ActionResult`.     |
| `scanFolder`       | `wchar_t* folderPath`          | `ScanResult*`  | Scans all files in a folder. Returns a pointer to an array of `ScanResult`. |
| `scanFolderAsString` | `wchar_t* folderPath`        | `ActionResult` | Scans all files in a folder. Returns results as a JSON string.              |
| `checkUpdates`     | `bool loadDBAfterUpdate`       | `ActionResult` | Checks and downloads the latest databases and AI model.                     |
| `getSettings`      | —                              | `ActionResult` | Returns the current settings as a JSON string.                              |
| `logging`          | `bool? enableLogging`          | `bool`         | Gets or sets logging state.                                                 |
| `baseFolder`       | `wchar_t* baseFolder`          | `wchar_t*`     | Gets or sets the base folder used to resolve relative paths.                |
| `version`          | —                              | `wchar_t*`     | Returns the SDK version string.                                             |

`checkUpdates` result string is one of:
- `"There is a new SDK version available!"`
- `"Database was updated!"`
- `"Database is up-to-date!"`

## Structs

### ActionResult

Used by most functions to return a success/failure result.

```c
typedef struct {
    bool     Sucess; // true on success
    wchar_t* Result; // result string (on success)
    wchar_t* Error;  // error message (on failure)
} ActionResult;
```

### ScanResult

Used by `scan` and `scanFolder` to return scan details.

```c
typedef struct {
    bool     Sucess;       // true on success
    wchar_t* Error;        // error message (on failure)
    bool     IsMalware;    // true if malware detected
    wchar_t* Name;         // detection name or status string
    double   MalwareScore; // 0–1 probability, -1 if there was an error
    wchar_t* Path;         // scanned file path
} ScanResult;
```

`Name` can be one of:
- `"Safe"` — no malware detected
- `"Malware"` — malware detected but family unknown
- _Malware family name_ — e.g. `"Trojan.Downloader"`
- `"AI.{score}"` — AI-only verdict, score from `0.00` to `100.00` (e.g. `"AI.99.99"`)

## Settings

Settings are located in the `settings.json` file next to the library. Available options:

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
- **DatabaseVersion** - Key-value list of database file versions. Updated automatically by `checkUpdates`.

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

If a function fails, `ActionResult.Sucess` will be `false` and `ActionResult.Error` will contain the error message.

All exceptions are also logged in the `errorlog.txt` file next to the library.
