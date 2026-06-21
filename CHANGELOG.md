# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [5.1.3]

### Added

- Cloud reputation scanner, gated by the new `SettingsDTO.EnableCloudScan` flag (off by default).
  After local engines (signatures, heuristics, AI) fail to reach a verdict, the engine queries the
  Xvirus cloud (`cloud.xvirus.net/api/reputation`) with the file's MD5 hash and returns
  `Cloud.Suspicious` when the cloud flags it as malware or `Safe` when explicitly clean. The
  lookup is fail-open with a 4-second timeout: any network error, offline state, or unknown hash
  returns `null` and local scanning continues uninterrupted — no file content is ever uploaded,
  only the hash. Documented in all SDK and CLI READMEs.
- Archive unpacking in the scan engine, gated by the new `SettingsDTO.EnableArchiveScan` flag
  (off by default). When enabled, the engine extracts zip-based archives (`.zip`, `.jar`, `.war`,
  `.ear`, `.apk`, `.xpi`, `.nupkg`, `.whl`, `.egg`) to a temp directory and recursively scans each
  entry. New configurable guards: `MaxArchiveDepth` (default 3), `MaxArchiveTotalSize`
  (default 100 MB), and `MaxArchiveFileCount` (default 1000). Zip-slip / zip-bomb attempts are
  detected and reported as `Suspicious.ArchiveBomb`. A malware hit on an inner entry is reported
  against the outer archive path so quarantine targets the archive. rar/7z support is pending a
  SharpCompress license check.
- Added `OnlyScanExecutables` to `Settings` and wired it up in
  `Scanner.ScanFile`. When enabled (the default), the engine short-circuits non-executable files
  as Safe before the expensive hash computation and database lookups. Archives are exempt when
  `EnableArchiveScan` is on, since they may contain executables worth scanning.

## [5.1.2]

### Added

- Scan cancellation support across the engine, all SDKs, and the CLI. A scan is identified by the
  file/folder path being scanned — cancel an in-progress scan by passing that same path:
  - `Scanner.ScanFile` and `Scanner.ScanFolder` register the scan under its path; new `CancelScan(string path)` cancels it. Both also accept an optional `CancellationToken`.
  - CSharpSDK: new `CancelScan(path)` and `CancelAllScans()` plus `Scan`/`ScanFolder` overloads that accept a `CancellationToken`
  - NativeSDK: new `cancelScan(path)` and `cancelAllScans` C exports
  - NodeSDK: new `cancelScan(path)` and `cancelAllScans()` functions
  - XvirusCLI: <kbd>Ctrl</kbd>+<kbd>C</kbd> cancels a running scan and still prints partial results
- Bloom filter pre-scan to speed up signature checks: the engine now consults a compact BloomFilter built from the safe and malware hash sets before performing exact hash lookups, allowing known-clean files to skip costly I/O and full-hash verification. The BloomFilter is used across SDKs to improve common-case scanning performance.

### Changed

- A cancelled folder scan returns the results gathered so far instead of discarding them

## [5.1.1]

### Fixed

- Removed `System.Security.Cryptography.Pkcs` dependency — replaced `SignedCms` with `X509Certificate2Collection.Import()` for AOT-compatible PE digital signature extraction on Linux

## [5.1]

### Added

- XvirusAI now works on C++ bindings (NativeSDK)
- Added XvirusSDK Node.js bindings

### Changed

- XvirusAI now uses ONNX model instead of ML.NET
- Updated `ImageSharp` to version 3.1.12

## [5.0]

### Added

- New settings: `EnableSignatures`, `HeuristicsLevel`, `AILevel`, `MaxHeuristicsPeScanLength`, `MaxHeuristicsOthersScanLength`, `MaxAIScanLength`, `CheckSDKUpdates`

### Changed

- Updated to .NET 8
- XvirusAI is now out of beta
- Improved performance of heuristics engine

## [4.2.3]

### Fixed

- Windows scan performance regression
- `ScanFolder` command not working in CLI
- `ScanFolderString` JSON not formatted correctly
- Update check always returning that there was an update

## [4.2.2]

### Added

- `ScanResult` now returns the file path
- New `ScanFolder()` and `ScanFolderString()` functions

### Changed

- Optimized scanning speed of PDF files

## [4.2.1]

### Changed

- Optimized scanning speed of large files
- Optimized scanning speed on Linux

## [4.2]

### Added

- `Logging()` function to enable/disable logging
- `BaseFolder()` function to set a custom base folder
- New setting `DatabaseFolder` to set the database folder path

### Changed

- Reduced minimum glibc version to 2.17 on Linux

### Fixed

- C++ binding now returns `Success=false` correctly when failing to scan a file

## [4.1]

### Added

- C++ bindings now also support Linux

### Changed

- Upgraded from .NET 5 to .NET 7
- Changed how exceptions are handled in C++ bindings

## [4.0]

### Added

- Linux support (CLI and C# bindings)
- XvirusAI scan engine (BETA)
- `checkUpdate()` function can now check for SDK updates
- New settings: `EnableAIScan`, `MaxScanLength`, `DatabaseVersion`

### Changed

- Completely redone in .NET 5
- Scan speed is up to 2× faster
- Removed file size limit for scanned files by default

### Fixed

- Memory usage spike when scanning large files
