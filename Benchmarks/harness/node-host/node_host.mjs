// Xvirus Node.js SDK benchmark host.
// Usage: node node_host.mjs <sdkFolder> <planFile> <outFile>
// Plan lines: baseline | load | unload | update | scan <warm> <reps> <path> | folder <path>
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const [sdkFolder, planFile, outFile] = process.argv.slice(2);
const out = fs.openSync(outFile, 'w');
const plan = fs.readFileSync(planFile, 'utf8').split(/\r?\n/);

const cpuMs = () => { const u = process.cpuUsage(); return (u.user + u.system) / 1000; };
const MB = 1048576;

function emit(op, p, times, cpu, result, files = 0) {
  const ru = process.resourceUsage();
  const obj = {
    op, path: p, times_ms: times, cpu_ms: cpu,
    ws_mb: process.memoryUsage.rss() / MB, peak_ws_mb: ru.maxRSS / 1024, private_mb: null,
    result, files, threads: 1,
  };
  fs.writeSync(out, JSON.stringify(obj) + '\n');
}

let sdk = null;
async function ensureSdk() {
  if (sdk) return;
  ({ XvirusNodeSDK: sdk } = await import(pathToFileURL(path.join(sdkFolder, 'XvirusNodeSDK.mjs')).href));
  sdk.baseFolder(sdkFolder);
}

for (const raw of plan) {
  const line = raw.trim();
  if (!line || line.startsWith('#')) continue;
  const sp = line.indexOf(' ');
  const op = sp < 0 ? line : line.slice(0, sp);
  const rest = sp < 0 ? '' : line.slice(sp + 1);
  const c0 = cpuMs();

  if (op === 'baseline') { emit(op, '', [], 0, ''); continue; }
  await ensureSdk();

  if (op === 'load' || op === 'unload') {
    const t0 = performance.now(); sdk[op](); const t = performance.now() - t0;
    emit(op, '', [t], cpuMs() - c0, 'ok');
  } else if (op === 'update') {
    const t0 = performance.now();
    let res; try { res = sdk.checkUpdates(false); } catch (e) { res = 'ERROR: ' + e.message; }
    emit(op, '', [performance.now() - t0], cpuMs() - c0, res);
  } else if (op === 'scan') {
    const m = rest.match(/^(\d+) (\d+) (.+)$/);
    const warm = +m[1], reps = +m[2], p = m[3];
    let name = '';
    const warmT = [];
    for (let i = 0; i < warm; i++) { const t0 = performance.now(); name = sdk.scan(p).name; warmT.push(performance.now() - t0); }
    const c1 = cpuMs();
    const times = [];
    for (let i = 0; i < reps; i++) { const t0 = performance.now(); name = sdk.scan(p).name; times.push(performance.now() - t0); }
    emit(op, p, times, cpuMs() - c1, `${name}|warm=${warmT.map(t => t.toFixed(3)).join(',')}`);
  } else if (op === 'folder') {
    const t0 = performance.now();
    const results = sdk.scanFolder(rest);
    const t = performance.now() - t0;
    emit(op, rest, [t], cpuMs() - c0, `malware=${results.filter(r => r.isMalware).length}`, results.length);
  }
}
fs.closeSync(out);
