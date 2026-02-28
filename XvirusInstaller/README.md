# XvirusInstaller - Setup Wizard

Preact + Neutralino.js installer for Xvirus Anti-Malware and Xvirus Firewall.

## Build Modes

| Mode | Product | UI Binary | Service |
|------|---------|-----------|---------|
| `am` | Xvirus Anti-Malware | `XvirusAM.exe` | `XvirusAntiMalwareService` |
| `fw` | Xvirus Firewall | `XvirusFW.exe` | `XvirusFirewallService` |

## Resources Preparation

Before building, populate `resources/` with the files to be installed. The UI binary must already be renamed to the product-specific name for the target mode:

```
XvirusInstaller/resources/
├── XvirusAM.exe            (AM: renamed from xvirus-*-win_x64.exe)
│   XvirusFW.exe            (FW: renamed from xvirus-*-win_x64.exe)
├── resources.neu           (from XvirusUI/dist/ after `neu build`)
├── XvirusService.exe       (published C# service binary)
└── unin.exe                (published XvirusUnin binary)
```

> The Neutralino build script will still auto-rename any `xvirus-*-win_x64.exe` it finds during install if the file is not pre-renamed.

## Building

### Prerequisites
- Node.js 16+ with npm
- Neutralino CLI (`neu`)

### Build Commands

```bash
cd XvirusInstaller

# Install dependencies (first time only)
npm install

# Production build - Anti-Malware mode
npm run build:am
# Output: dist/xvirus-installer-win_x64.exe

# Production build - Firewall mode
npm run build:fw
# Output: dist/xvirus-installer-win_x64.exe
```

## Build Process

1. **Resource Embedding** (`scripts/build-resources.mjs`):
   - Reads product info for the selected mode (name, `uiExeName`, `serviceName`, version, …)
   - Zips all files in `resources/`
   - Base64-encodes the zip
   - Generates `src/generated/resources.ts` with the embedded data and `PRODUCT_INFO` constant

2. **Frontend Build** (Vite):
   - Bundles TypeScript/TSX and CSS into `browser/`

3. **App Build** (Neutralino):
   - Packages everything into a portable single-file binary

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
resources.zip
  ↓ (base64 encode)
RESOURCES_B64 constant in resources.ts
  ↓ (Vite + neu build)
xvirus-installer-win_x64.exe  (single self-contained binary)
```

### At Installation Time
```
1. Decode RESOURCES_B64 → ArrayBuffer
2. Write to %TEMP%\xvirus_install_<ts>.zip
3. Generate PowerShell script with mode-specific parameters
4. Launch PowerShell elevated (Start-Process -Verb RunAs)
5. PowerShell script:
   a. Expands archive to install path
   b. Renames xvirus-*-win_x64.exe → XvirusAM.exe / XvirusFW.exe (if not pre-renamed)
   c. Registers Windows Service (auto-start)
   d. Creates registry entries under:
      HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\<ProductName>
   e. Sets UninstallString to: <installPath>\unin.exe am|fw
   f. Creates shortcuts if selected (desktop + Start Menu)
6. Installer polls %TEMP%\xvirus_progress.json every 500 ms
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
→ The PowerShell script requests elevation automatically — confirm the UAC prompt

**Service not registered**
→ Check `services.msc` for `XvirusAntiMalwareService` / `XvirusFirewallService`
→ Check `%TEMP%\xvirus_progress.json` for error details

## Uninstallation

Run `unin.exe` with the mode argument from the installation folder:

```
"C:\Program Files\Xvirus Anti-Malware\unin.exe" am
"C:\Program Files\Xvirus Firewall\unin.exe" fw
```

Or use **Windows Settings → Apps → Xvirus Anti-Malware / Xvirus Firewall → Uninstall** — the registry `UninstallString` already includes the mode argument.
