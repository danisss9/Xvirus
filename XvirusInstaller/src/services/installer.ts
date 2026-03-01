import { InstallConfig, InstallProgress } from '../model/InstallConfig';
import { Neutralino } from './neutralino';
import { RESOURCES_B64, PRODUCT_INFO } from '../generated/resources';
import type { ProductInfo } from '../generated/resources';

/**
 * Convert base64 string to ArrayBuffer
 */
function base64ToArrayBuffer(b64: string): ArrayBuffer {
  const binaryStr = atob(b64);
  const bytes = new Uint8Array(binaryStr.length);
  for (let i = 0; i < binaryStr.length; i++) {
    bytes[i] = binaryStr.charCodeAt(i);
  }
  return bytes.buffer;
}

/**
 * Parse progress JSON file
 */
async function readProgress(progressFile: string): Promise<InstallProgress | null> {
  try {
    const content = await Neutralino.filesystem.readFile(progressFile);
    return JSON.parse(content) as InstallProgress;
  } catch {
    return null;
  }
}

/**
 * Get temp directory path
 */
async function getTempPath(): Promise<string> {
  // On Windows, use %TEMP% environment variable
  const result = await Neutralino.os.execCommand('powershell -Command "$env:TEMP"');
  return result.stdOut.trim();
}

/**
 * Generate PowerShell install script
 */
function generatePowerShellScript(config: InstallConfig, zipPath: string, product: ProductInfo): string {
  const installPath = config.installPath.replace(/"/g, '\\"');
  const zipPathEsc = zipPath.replace(/"/g, '\\"');

  // In a TS template literal, $identifier (no braces) is NOT interpolated — only ${expr} is.
  // So PowerShell variables like $installPath are written as-is; no \$ escaping needed.
  // \\  in the template  →  \  in the PS1 file (used for Windows path separators).
  // \`" in the template  →  `" in the PS1 file (PowerShell escape for a literal quote char).
  return `# Auto-generated Xvirus installer script
# Mode: AM/FW
# Generated: ${new Date().toISOString()}

param()

$installPath = "${installPath}"
$zipPath = "${zipPathEsc}"
$desktopShortcut = $${config.desktopShortcut ? 'true' : 'false'}
$startMenuShortcut = $${config.startMenuShortcut ? 'true' : 'false'}
$serviceName = "${product.serviceName}"
$serviceDisplayName = "${product.serviceDescription}"
$version = "${product.version}"
$publisher = "${product.publisher}"
$productName = "${product.name}"
$uiExeName = "${product.uiExeName}"
$progressFile = "$env:TEMP\\xvirus_progress.json"

function Write-Progress-Step {
    param($step, $pct)
    @{step=$step; progress=$pct; done=$false} | ConvertTo-Json | Set-Content $progressFile
}

try {
    Write-Progress-Step "Extracting files..." 10
    New-Item -Path $installPath -ItemType Directory -Force | Out-Null
    Expand-Archive -Path $zipPath -DestinationPath $installPath -Force

    # Rename Neutralino binary to product-specific name
    $neuBinary = Get-ChildItem $installPath -Name -Filter "xvirus-*-win_x64.exe"
    if ($neuBinary) {
        Rename-Item -Path "$installPath\\$neuBinary" -NewName $uiExeName -Force
    }

    Write-Progress-Step "Registering service..." 40
    & sc.exe create $serviceName binPath= "\`"$installPath\\XvirusService.exe\`"" start= auto DisplayName= "\`"$serviceDisplayName\`""
    & sc.exe description $serviceName $serviceDisplayName
    & sc.exe start $serviceName

    Write-Progress-Step "Creating registry entries..." 60
    $regPath = "HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\$productName"
    New-Item -Path $regPath -Force | Out-Null
    Set-ItemProperty -Path $regPath -Name DisplayName -Value $productName
    Set-ItemProperty -Path $regPath -Name DisplayVersion -Value $version
    Set-ItemProperty -Path $regPath -Name Publisher -Value $publisher
    Set-ItemProperty -Path $regPath -Name InstallLocation -Value $installPath
    Set-ItemProperty -Path $regPath -Name UninstallString -Value "$installPath\\unin.exe ${product.mode}"
    Set-ItemProperty -Path $regPath -Name DisplayIcon -Value "$installPath\\$uiExeName,0"
    Set-ItemProperty -Path $regPath -Name InstallDate -Value (Get-Date -Format "yyyyMMdd")
    Set-ItemProperty -Path $regPath -Name NoModify -Value 1 -Type DWord
    Set-ItemProperty -Path $regPath -Name NoRepair -Value 1 -Type DWord

    # Add startup entry for XvirusUI
    Set-ItemProperty -Path "HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run" -Name $productName -Value "$installPath\\$uiExeName"

    Write-Progress-Step "Creating shortcuts..." 80
    if ($desktopShortcut) {
        $ws = New-Object -ComObject WScript.Shell
        $lnk = $ws.CreateShortcut("$env:PUBLIC\\Desktop\\$productName.lnk")
        $lnk.TargetPath = "$installPath\\$uiExeName"
        $lnk.IconLocation = "$installPath\\$uiExeName,0"
        $lnk.Save()
    }

    if ($startMenuShortcut) {
        $dir = "$env:ProgramData\\Microsoft\\Windows\\Start Menu\\Programs\\$productName"
        New-Item -Path $dir -ItemType Directory -Force | Out-Null
        $ws = New-Object -ComObject WScript.Shell
        $lnk = $ws.CreateShortcut("$dir\\$productName.lnk")
        $lnk.TargetPath = "$installPath\\$uiExeName"
        $lnk.IconLocation = "$installPath\\$uiExeName,0"
        $lnk.Save()
    }

    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    @{step="done"; progress=100; done=$true; success=$true} | ConvertTo-Json | Set-Content $progressFile
} catch {
    @{done=$true; success=$false; error=$_.Exception.Message} | ConvertTo-Json | Set-Content $progressFile
}
`;
}

/**
 * Execute the installation process
 */
export async function executeInstall(
  config: InstallConfig,
  onProgressUpdate: (progress: InstallProgress) => void
): Promise<{ success: boolean; error?: string }> {
  try {
    const tempPath = await getTempPath();
    const timestamp = Date.now();
    const zipFile = `${tempPath}\\xvirus_install_${timestamp}.zip`;
    const psFile = `${tempPath}\\xvirus_install_${timestamp}.ps1`;
    const progressFile = `${tempPath}\\xvirus_progress.json`;

    // 1. Decode and write zip file
    onProgressUpdate({ step: 'Preparing installation...', progress: 5, done: false });

    const zipBuffer = base64ToArrayBuffer(RESOURCES_B64);
    await Neutralino.filesystem.writeBinaryFile(zipFile, zipBuffer);

    // 2. Generate and write PowerShell script
    const psScript = generatePowerShellScript(config, zipFile, PRODUCT_INFO);
    await Neutralino.filesystem.writeFile(psFile, psScript);

    onProgressUpdate({ step: 'Launching installer...', progress: 10, done: false });

    // 3. Launch elevated PowerShell (fire and forget, no wait)
    const psCommand = `powershell -Command "Start-Process powershell -ArgumentList '-ExecutionPolicy Bypass -WindowStyle Hidden -File \\"${psFile}\\"' -Verb RunAs"`;
    await Neutralino.os.execCommand(psCommand);

    // 4. Poll progress file
    let maxAttempts = 120; // ~60 seconds with 500ms intervals
    let attempts = 0;

    return new Promise((resolve) => {
      const pollInterval = setInterval(async () => {
        attempts++;

        try {
          const progress = await readProgress(progressFile);

          if (progress) {
            onProgressUpdate(progress);

            if (progress.done) {
              clearInterval(pollInterval);

              // Cleanup temp files
              try {
                await Neutralino.os.execCommand(`powershell -Command "Remove-Item '${psFile}' -Force -ErrorAction SilentlyContinue"`);
              } catch {
                // Ignore cleanup errors
              }

              resolve({
                success: progress.success ?? false,
                error: progress.error
              });
            }
          }
        } catch (error) {
          // Still polling...
        }

        // Timeout after ~60 seconds
        if (attempts >= maxAttempts) {
          clearInterval(pollInterval);
          resolve({
            success: false,
            error: 'Installation timeout - the process took too long'
          });
        }
      }, 500);
    });
  } catch (error) {
    return {
      success: false,
      error: `Installation failed: ${error instanceof Error ? error.message : String(error)}`
    };
  }
}

export { PRODUCT_INFO };
export type { ProductInfo };
