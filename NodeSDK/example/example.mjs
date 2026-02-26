// Place this file next to XvirusNodeSDK.mjs (at the root of the NodeSDK distribution).
// Run with: node example.mjs [file] [folder]

import { XvirusNodeSDK } from './XvirusNodeSDK.mjs';

// ---- 1. Print SDK version --------------------------------------------------

console.log('Xvirus SDK ' + XvirusNodeSDK.version());

// ---- 2. Load the engine (databases + AI model) -----------------------------

XvirusNodeSDK.load();

// ---- 3. Single file scan ---------------------------------------------------

const targetFile = process.argv[2]
    ?? (process.platform === 'win32' ? 'C:\\Windows\\System32\\notepad.exe' : '/usr/bin/bash');

const result = XvirusNodeSDK.scan(targetFile);
console.log(`\nFile:      ${result.path}`);
console.log(`Malware:   ${result.isMalware}`);
console.log(`Detection: ${result.name}`);
console.log(`Score:     ${(result.malwareScore * 100).toFixed(2)}%`);

// ---- 4. Folder scan --------------------------------------------------------

const targetFolder = process.argv[3];
if (targetFolder) {
    console.log(`\nScanning folder: ${targetFolder}`);
    const results = XvirusNodeSDK.scanFolder(targetFolder);
    for (const r of results) {
        if (r.isMalware)
            console.log(`  THREAT  ${r.name.padEnd(30)} ${r.path}`);
    }
    console.log(`Scanned ${results.length} file(s).`);
}

// ---- 5. Check for updates --------------------------------------------------

const updateInfo = XvirusNodeSDK.checkUpdates();
console.log(`\nUpdate info: ${updateInfo}`);

// ---- 6. Unload the engine --------------------------------------------------

XvirusNodeSDK.unload();
