import { readdirSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';
import rcedit from 'rcedit';

const __dirname = dirname(fileURLToPath(import.meta.url));
const installerRoot = join(__dirname, '..');

const mode = process.argv[2];
if (mode !== 'am' && mode !== 'fw') {
  console.error('Usage: node patch-installer.mjs [am|fw]');
  process.exit(1);
}

const info = {
  am: { description: 'Xvirus Anti-Malware Setup', version: '8.0.0.0' },
  fw: { description: 'Xvirus Firewall Setup', version: '5.0.0.0' },
}[mode];

const distRoot = join(installerRoot, 'dist');
const distFolders = readdirSync(distRoot);
if (distFolders.length === 0) {
  console.error('Error: dist/ is empty after neu build.');
  process.exit(1);
}
const distDir = join(distRoot, distFolders[0]);

const exeFiles = readdirSync(distDir).filter((f) => f.endsWith('-win_x64.exe'));
if (exeFiles.length === 0) {
  console.error(`Error: No *-win_x64.exe found in ${distDir}`);
  process.exit(1);
}
const exePath = join(distDir, exeFiles[0]);

console.log('\n── Patching installer exe metadata ──');
console.log(`  ${exeFiles[0]}`);
await rcedit(exePath, {
  'version-string': {
    FileDescription: info.description,
    ProductName: info.description,
    CompanyName: 'Xvirus',
    LegalCopyright: '© 2026 Xvirus',
  },
  'file-version': info.version,
  'product-version': info.version,
});
console.log(`✓ Patched: ${info.description} v${info.version}`);
