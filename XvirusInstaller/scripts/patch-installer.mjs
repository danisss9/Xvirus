import { readdirSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';
import rcedit from 'rcedit';
import { getProduct, fourPartVersion } from './product-info.mjs';

const __dirname = dirname(fileURLToPath(import.meta.url));
const installerRoot = join(__dirname, '..');

const mode = process.argv[2];
const product = getProduct(mode);
const info = { description: product.setupDescription, version: fourPartVersion(product.version) };

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
    CompanyName: product.publisher,
    LegalCopyright: product.copyright,
  },
  'file-version': info.version,
  'product-version': info.version,
});
console.log(`✓ Patched: ${info.description} v${info.version}`);
