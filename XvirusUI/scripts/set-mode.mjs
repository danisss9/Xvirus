import { readFileSync, writeFileSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const configPath = join(__dirname, '..', 'neutralino.config.json');

const mode = (process.argv[2] || '').toLowerCase();
const MODES = {
  am: {
    applicationId: 'net.xvirus.antimalware',
    version: '8.0.0',
    title: 'Xvirus Anti-Malware',
    binaryName: 'xvirus-antimalware',
    isFirewall: false,
    isAntimalware: true,
  },
  fw: {
    applicationId: 'net.xvirus.firewall',
    version: '5.0.0',
    title: 'Xvirus Firewall',
    binaryName: 'xvirus-firewall',
    isFirewall: true,
    isAntimalware: false,
  },
};

const spec = MODES[mode];
if (!spec) {
  console.error(`Invalid mode '${mode}'. Expected: am | fw`);
  process.exit(1);
}

const config = JSON.parse(readFileSync(configPath, 'utf-8'));
config.applicationId = spec.applicationId;
config.version = spec.version;
config.globalVariables = {
  IS_FIREWALL: spec.isFirewall,
  IS_ANTIMALWARE: spec.isAntimalware,
};
config.modes = config.modes || {};
config.modes.window = config.modes.window || {};
config.modes.window.title = spec.title;
config.cli = config.cli || {};
config.cli.binaryName = spec.binaryName;

writeFileSync(configPath, JSON.stringify(config, null, 2) + '\n');
console.log(`Configured XvirusUI neutralino.config.json for mode '${mode}' (${spec.title} v${spec.version})`);
