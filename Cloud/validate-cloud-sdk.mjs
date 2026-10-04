// Validates the shipped SDK layout on Linux: loads the engine and scans test files,
// asserting that the script AI engine (LLamaSharp llama.cpp backend) actually runs.
// Usage: node validate-cloud-sdk.mjs <sdkDir> <maliciousFile> <benignFile> <peFile> [--expect-disabled]
//
// Positive mode (default): the malicious script must be flagged as AI.Script.Malicious,
// the benign script must not be flagged, and a PE file must be scored by the ONNX engine.
// --expect-disabled: without the llama backend the script AI must be absent (scan degrades
// to Safe) — pair this with a grep of errorlog.txt for "Script AI scan disabled".

import path from 'node:path';

const argv = process.argv.slice(2);
const expectDisabled = argv.includes('--expect-disabled');
const positional = argv.filter((a) => a !== '--expect-disabled');
const [sdkDir, maliciousFile, benignFile, peFile] = positional;

if (!sdkDir || !maliciousFile || !benignFile) {
  console.error(
    'Usage: node validate-cloud-sdk.mjs <sdkDir> <maliciousFile> <benignFile> <peFile> [--expect-disabled]',
  );
  process.exit(2);
}

const { XvirusNodeSDK } = await import(path.resolve(sdkDir, 'XvirusNodeSDK.mjs'));
XvirusNodeSDK.baseFolder(path.resolve(sdkDir));
XvirusNodeSDK.load(false);

const results = {};
for (const [key, file] of Object.entries({ malicious: maliciousFile, benign: benignFile, pe: peFile })) {
  if (file) results[key] = XvirusNodeSDK.scan(path.resolve(file));
}
console.log(JSON.stringify(results, null, 2));

let failed = false;
const fail = (msg) => {
  console.error(`FAIL: ${msg}`);
  failed = true;
};

if (expectDisabled) {
  if (results.malicious.name.startsWith('AI.Script.'))
    fail(`script AI ran despite the missing llama backend: ${results.malicious.name}`);
} else {
  if (!results.malicious.isMalware || results.malicious.name !== 'AI.Script.Malicious')
    fail(`malicious script not flagged by the script AI, got ${results.malicious.name}`);
}

if (results.benign?.isMalware || results.benign?.name.startsWith('AI.Script.'))
  fail(`benign script flagged, got ${results.benign?.name}`);

// "AI.-1.00" means the ONNX session failed to load and the score defaulted to -1.
if (results.pe) {
  if (!results.pe.name.startsWith('AI.') || results.pe.name === 'AI.-1.00')
    fail(`PE scan did not produce a real ONNX score, got ${results.pe.name}`);
}

process.exit(failed ? 1 : 0);
