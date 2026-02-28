import { execSync } from 'child_process';
import { copyFileSync, mkdirSync, existsSync, readdirSync, rmSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const installerRoot = join(__dirname, '..'); // XvirusInstaller/
const sdkRoot = join(installerRoot, '..'); // XescSDK/
const resourcesDir = join(installerRoot, 'resources');

const mode = process.argv[2];
if (mode !== 'am' && mode !== 'fw') {
  console.error('Usage: node prebuild.mjs [am|fw]');
  process.exit(1);
}

const uiExeName = mode === 'am' ? 'XvirusAM.exe' : 'XvirusFW.exe';

function run(cmd, cwd) {
  console.log(`> ${cmd}`);
  execSync(cmd, { cwd, stdio: 'inherit' });
}

// ── 1. Build XvirusUI ────────────────────────────────────────────────────────
console.log('\n── Building XvirusUI ──');
run(`npm run prod:${mode}`, join(sdkRoot, 'XvirusUI'));

// ── 2. Publish XvirusService ─────────────────────────────────────────────────
console.log('\n── Publishing XvirusService ──');
const serviceDir = join(sdkRoot, 'XvirusService');
const servicePublishDir = join(serviceDir, 'bin', 'Publish');
run(
  `dotnet publish XvirusService.csproj /p:PublishProfile=Windows "/p:PublishDir=${servicePublishDir}\\"`,
  serviceDir,
);

// ── 3. Publish XvirusUnin ────────────────────────────────────────────────────
console.log('\n── Publishing XvirusUnin ──');
run('dotnet publish XvirusUnin.csproj /p:PublishProfile=Windows', join(sdkRoot, 'XvirusUnin'));

// ── 4. Stage resources/ ──────────────────────────────────────────────────────
console.log('\n── Staging resources/ ──');
if (existsSync(resourcesDir)) {
  rmSync(resourcesDir, { recursive: true });
}
mkdirSync(resourcesDir, { recursive: true });

// XvirusUI binary + resources
// neu build always outputs to dist/<binaryName>/ — find the folder
const distRoot = join(sdkRoot, 'XvirusUI', 'dist');
const distFolders = readdirSync(distRoot);
if (distFolders.length === 0) {
  console.error('Error: XvirusUI/dist/ is empty after build.');
  process.exit(1);
}
const distDir = join(distRoot, distFolders[0]);

const neuBinaryFiles = readdirSync(distDir).filter((f) => f.endsWith('-win_x64.exe'));
if (neuBinaryFiles.length === 0) {
  console.error(`Error: No *-win_x64.exe found in ${distDir}`);
  process.exit(1);
}
copyFileSync(join(distDir, neuBinaryFiles[0]), join(resourcesDir, uiExeName));
console.log(`  ${neuBinaryFiles[0]} → resources/${uiExeName}`);

// XvirusService.exe
copyFileSync(join(servicePublishDir, 'XvirusService.exe'), join(resourcesDir, 'XvirusService.exe'));
console.log(`  XvirusService.exe → resources/XvirusService.exe`);

// XvirusUnin.exe → unin.exe
copyFileSync(
  join(sdkRoot, 'XvirusUnin', 'publish', 'XvirusUnin.exe'),
  join(resourcesDir, 'unin.exe'),
);
console.log(`  XvirusUnin.exe → resources/unin.exe`);

console.log(`\n✓ resources/ ready for ${mode} (${uiExeName})`);
