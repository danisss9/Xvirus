# XvirusInstaller - Setup Wizard

Preact + Tauri installer for Xvirus Anti-Malware and Xvirus Firewall.

## Build Modes

| Mode | Product | UI Binary | Service |
|------|---------|-----------|---------|
| `am` | Xvirus Anti-Malware | `XvirusAM.exe` | `XvirusAntiMalwareService` |
| `fw` | Xvirus Firewall | `XvirusFW.exe` | `XvirusFirewallService` |

## Resources Preparation

`npm run prepare:am` / `prepare:fw` (`scripts/prebuild.mjs`) builds XvirusUI, XvirusService and XvirusUnin and stages them in `resources/`:

```
XvirusInstaller/resources/
├── XvirusAM.exe            (AM: XvirusUI/src-tauri/target/release/xvirus-ui.exe, renamed)
│   XvirusFW.exe            (FW: same binary built in firewall mode)
├── XvirusService.exe       (published C# service binary)
├── onnxruntime.dll
├── onnxruntime_providers_shared.dll
└── unin.exe                (published XvirusUnin binary)
```

The XvirusUI frontend is embedded in its exe by Tauri, so no separate resource file is needed.

## Building

### Prerequisites
- Node.js 26+ (current LTS) with npm
- Rust (stable, MSVC toolchain) and the [Tauri prerequisites for Windows](https://v2.tauri.app/start/prerequisites/)
- .NET 8 SDK (for `prepare:*`, which publishes XvirusService and XvirusUnin)

### Build Commands

```bash
cd XvirusInstaller

# Install dependencies (first time only)
npm install

# Stage resources and build - Anti-Malware mode
npm run release:am
# Output: dist/xvirus-installer.exe

# Stage resources and build - Firewall mode
npm run release:fw
# Output: dist/xvirus-installer.exe

# Run the wizard in development (Vite dev server + Tauri window)
npm run dev
```

`build:am` / `build:fw` skip the staging step and reuse whatever is already in `resources/`.

## Build Process

1. **Resource Embedding** (`scripts/build-resources.mjs`):
   - Reads product info for the selected mode (name, `uiExeName`, `serviceName`, version, …)
   - Zips all files in `resources/` into `src-tauri/payload.zip`
   - Generates `src/generated/resources.ts` with the `PRODUCT_INFO` constant

2. **App Build** (`tauri build --no-bundle`):
   - Vite bundles TypeScript/TSX and CSS into `browser/`
   - `src-tauri/build.rs` embeds `payload.zip` in the binary (`include_bytes!`)
   - Produces a single self-contained `src-tauri/target/release/xvirus-installer.exe`

3. **Patch** (`scripts/patch-installer.mjs`):
   - Copies the binary to `dist/xvirus-installer.exe` and sets its version metadata with rcedit

## Installation Flow

1. **Welcome** — Product logo and introduction
2. **Configuration** — Choose installation path and shortcut options
3. **Progress** — Real-time installation progress bar
4. **Done** — Success or error screen

## What the Installer Does

### At Build Time
```
resources/
  ↓ (zip via PowerShell Compress-Archive)
src-tauri/payload.zip
  ↓ (include_bytes! via build.rs + tauri build)
xvirus-installer.exe  (single self-contained binary)
```

### At Installation Time
```
1. (Rust write_payload command) Read the zip embedded in the binary
2. Write to %TEMP%\xvirus_install_<ts>.zip
3. Generate PowerShell script with mode-specific parameters
4. Launch PowerShell elevated (Start-Process -Verb RunAs)
5. PowerShell script:
   a. Expands archive to install path
   b. Registers Windows Service (auto-start)
   c. Creates registry entries under:
      HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\<ProductName>
   d. Sets UninstallString to: <installPath>\unin.exe am|fw
   e. Creates shortcuts if selected (desktop + Start Menu)
6. Installer polls %TEMP%\xvirus_progress_<ts>.json every 500 ms
7. Shows progress and completion status
```

## PRODUCT_INFO Fields

Generated into `src/generated/resources.ts` by `build-resources.mjs`:

| Field | `am` | `fw` |
|-------|------|------|
| `mode` | `am` | `fw` |
| `name` | Xvirus Anti-Malware | Xvirus Firewall |
| `installFolder` | Xvirus Anti-Malware | Xvirus Firewall |
| `uiExeName` | XvirusAM.exe | XvirusFW.exe |
| `serviceName` | XvirusAntiMalwareService | XvirusFirewallService |
| `serviceDescription` | Xvirus Anti-Malware Protection Service | Xvirus Firewall Protection Service |
| `version` | 8.0.0 | 5.0.0 |
| `publisher` | Xvirus | Xvirus |

## Troubleshooting

**Build fails: "resources directory is empty"**
→ Populate `resources/` with required files (see Resources Preparation above)

**Build fails: "PowerShell Compress-Archive not found"**
→ Ensure PowerShell 5.1+ is installed (included with Windows 10/11)

**Installation fails: "Access Denied"**
→ The PowerShell script requests elevation automatically — confirm the UAC prompt. If the prompt is declined, the wizard shows an error.

**Service not registered**
→ Check `services.msc` for `XvirusAntiMalwareService` / `XvirusFirewallService`
→ Check `%TEMP%\xvirus_progress_<ts>.json` for error details

## Uninstallation

Run `unin.exe` with the mode argument from the installation folder:

```
"C:\Program Files\Xvirus Anti-Malware\unin.exe" am
"C:\Program Files\Xvirus Firewall\unin.exe" fw
```

Or use **Windows Settings → Apps → Xvirus Anti-Malware / Xvirus Firewall → Uninstall** — the registry `UninstallString` already includes the mode argument.
