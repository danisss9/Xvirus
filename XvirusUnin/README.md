# Xvirus Uninstaller

Native AOT uninstaller for Xvirus products (Anti-Malware and Firewall). Runs silently with no console window, prompts for confirmation via a Win32 MessageBox, then removes all traces of the product.

## Requirements

- Windows x64
- Must run as Administrator (enforced via `app.manifest`)
- .NET 8 SDK with Native AOT workload (`dotnet workload install wasi-experimental` is not needed — just the standard `microsoft.net.sdk.windowsdesktop` with AOT support)

## Usage

```
XvirusUnin.exe <mode>
```

| Mode | Product             |
| ---- | ------------------- |
| `am` | Xvirus Anti-Malware |
| `fw` | Xvirus Firewall     |

**Examples:**

```
XvirusUnin.exe am
XvirusUnin.exe fw
```

If the mode argument is missing or invalid, an error dialog is shown and the process exits with code `1`.

## Uninstall Steps

When the user confirms the dialog, the following steps run in order:

1. **Kill UI process** — terminates `XvirusAM.exe` or `XvirusFW.exe` if running (waits up to 5 s)
2. **Stop & delete service** — runs `sc.exe stop <ServiceName>` then `sc.exe delete <ServiceName>`
3. **Remove registry entries** — deletes `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\<ProductName>`
4. **Delete shortcuts** — removes `%PUBLIC%\Desktop\<ProductName>.lnk` and `%ProgramData%\Microsoft\Windows\Start Menu\Programs\<ProductName>\`
5. **Schedule self-deletion** — writes a temp `.bat` script that waits 2 s then `rmdir /s /q` the install folder and deletes itself; launches it hidden and exits

## Product Configuration

Mode-specific names are resolved at runtime by `ProductConfig.FromMode()`:

| Field               | `am`                     | `fw`                  |
| ------------------- | ------------------------ | --------------------- |
| `ProductName`       | Xvirus Anti-Malware      | Xvirus Firewall       |
| `ServiceName`       | XvirusAntiMalwareService | XvirusFirewallService |
| `UiProcessName`     | XvirusAM                 | XvirusFW              |
| `InstallFolderName` | Xvirus Anti-Malware      | Xvirus Firewall       |
| `RegistryKey`       | Xvirus Anti-Malware      | Xvirus Firewall       |

## Build & Publish

```bash
dotnet publish XvirusUnin.csproj /p:PublishProfile=Windows
```

Output is written to `publish\`. The publish profile (`Properties/PublishProfiles/Windows.pubxml`) targets:

- Runtime: `win-x64`
- Configuration: `Release`
- Native AOT: enabled
- Self-contained: yes
- Symbols stripped: yes

## Project Structure

```
XvirusUnin/
├── Program.cs                          # All uninstaller logic
├── XvirusUnin.csproj                   # .NET 8 Native AOT WinExe project
├── app.manifest                        # Requires administrator + Common Controls v6
└── Properties/
    └── PublishProfiles/
        └── Windows.pubxml              # Publish profile for win-x64 AOT
```

## Notes

- The console window is hidden immediately on startup via `ShowWindow(SW_HIDE)` — no terminal flickers.
- The self-deletion safety check verifies that the executable's directory name matches `InstallFolderName` before issuing `rmdir`, preventing accidental deletion if the binary is run from an unexpected location.
- All uninstall steps are non-fatal: failures are logged to stdout and execution continues, so a partial install is always fully cleaned up.
