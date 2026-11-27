import { copyFileSync, existsSync, mkdirSync, rmSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';
import rcedit from 'rcedit';
import { getProduct, fourPartVersion } from './product-info.mjs';

const __dirname = dirname(fileURLToPath(import.meta.url));
const installerRoot = join(__dirname, '..');

const mode = process.argv[2];
const product = getProduct(mode);
const info = { description: product.setupDescription, version: fourPartVersion(product.version) };

// `tauri build --no-bundle` writes the binary to src-tauri/target/release/
const builtExe = join(installerRoot, 'src-tauri', 'target', 'release', 'xvirus-installer.exe');
if (!existsSync(builtExe)) {
  console.error(`Error: ${builtExe} not found. Did \`tauri build\` succeed?`);
  process.exit(1);
}

const distDir = join(installerRoot, 'dist');
rmSync(distDir, { recursive: true, force: true });
mkdirSync(distDir, { recursive: true });
const exePath = join(distDir, 'xvirus-installer.exe');
copyFileSync(builtExe, exePath);

console.log('\n── Patching installer exe metadata ──');
console.log(`  ${exePath}`);
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
