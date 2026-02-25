# Xvirus

Xvirus SDK 5.1 — Anti-malware scanning engine and desktop protection suite.

## Repository Overview

| Component | Description |
|-----------|-------------|
| [BaseLibrary](BaseLibrary/README.md) | Core .NET 8 library shared by all SDK bindings. Provides the scan engine, AI inference, database management, updater, and settings. |
| [CSharpSDK](CSharpSDK/README.md) | C# SDK wrapper. Use this when integrating Xvirus into a .NET 8 project. |
| [NativeSDK](NativeSDK/README.md) | Native AOT shared library (`XvirusSDK.dll` / `.so`) with C-compatible exports. Use this to integrate from C, C++, or any language with FFI support. |
| [NodeSDK](NodeSDK/README.md) | Node.js native addon (`.node` + ESM wrapper). Use this to integrate from Node.js or TypeScript. |
| [XvirusCLI](XvirusCLI/README.md) | Command-line interface for scanning files and folders, managing updates, and configuring settings from a terminal. |
| [AITrainer](AITrainer/README.md) | Tool to train and export the XvirusAI ONNX model from a dataset of malware and benign PE files. |
| [XvirusService](XvirusService/README.md) | Windows background service (ASP.NET Core, Native AOT). Hosts the HTTP API on port 5236, real-time process monitoring, network protection, and automatic updates. |
| [XvirusUI](XvirusUI/README.md) | Desktop UI (Preact + Neutralino.js). Connects to XvirusService and supports both antimalware and firewall modes. |

## Minimum Requirements

- .NET 8 SDK — [download](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

Supported operating systems:

- Windows: Windows 10 1607, Windows 11 22H2, Windows Server 2012, Windows Server Core 2012
- Linux (glibc 2.35): Alpine 3.19, Azure Linux 3.0, CentOS Stream 9, Debian 12, Fedora 41, openSUSE Leap 15.6, RHEL 8, SUSE 15.6, Ubuntu 22.04

## Choosing a Component

| Goal | Component |
|------|-----------|
| Integrate scanning into a C# / .NET project | [CSharpSDK](CSharpSDK/README.md) |
| Integrate scanning into a C, C++, or FFI consumer | [NativeSDK](NativeSDK/README.md) |
| Integrate scanning into a Node.js / TypeScript project | [NodeSDK](NodeSDK/README.md) |
| Scan files from a terminal or script | [XvirusCLI](XvirusCLI/README.md) |
| Run as a Windows background service with HTTP API | [XvirusService](XvirusService/README.md) |
| Desktop antimalware / firewall UI | [XvirusUI](XvirusUI/README.md) |
| Train or retrain the AI model | [AITrainer](AITrainer/README.md) |

## Known Issues

- The `checkUpdate` function can check for SDK/CLI updates but cannot update automatically.

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for the full version history.
