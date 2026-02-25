# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

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
