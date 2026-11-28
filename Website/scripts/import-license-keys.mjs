// Loads the license key pool into MongoDB (collection `licenseKeys`).
//
// Reads the `licenses` (Firewall) and `amLicenses` (Anti-Malware) arrays from the Login
// server's main.js, so the keys never have to be copied into this (public) repository.
// Safe to run again: keys already in the pool are left untouched, including their assignment.
//
// Usage (from the Website folder):
//   MONGODB_URI=mongodb://... npm run import-keys -- [--login <path/to/Login/main.js>] [--exclude <file>] [--dry-run]
//
//   --login    Path to the Login server's main.js (default: ../../Login/main.js)
//   --exclude  Text file with one key per line that were already sold elsewhere. They are
//              marked as used (checkoutId "legacy") so they are never handed out again.
//   --dry-run  Print what would be imported without touching the database.

import fs from 'node:fs';
import path from 'node:path';

const KEY_RE = /^[A-Z0-9]{5}(-[A-Z0-9]{5}){4}$/;

function arg(name) {
  const i = process.argv.indexOf(name);
  return i === -1 ? undefined : process.argv[i + 1];
}

function readArray(source, name) {
  const match = source.match(new RegExp(`const\\s+${name}\\s*=\\s*\\[([\\s\\S]*?)\\];`));
  if (!match) throw new Error(`Array "${name}" not found.`);
  return [...match[1].matchAll(/["']([^"']+)["']/g)].map((m) => m[1].trim().toUpperCase());
}

const loginPath = path.resolve(arg('--login') ?? '../../Login/main.js');
const excludePath = arg('--exclude');
const dryRun = process.argv.includes('--dry-run');

const source = fs.readFileSync(loginPath, 'utf8');
const pools = { fw: readArray(source, 'licenses'), am: readArray(source, 'amLicenses') };

// Validate before writing anything.
const seen = new Map();
for (const [product, list] of Object.entries(pools)) {
  for (const key of list) {
    if (!KEY_RE.test(key)) throw new Error(`Malformed ${product} key: ${key}`);
    if (seen.has(key)) throw new Error(`Duplicate key ${key} (${seen.get(key)} and ${product})`);
    seen.set(key, product);
  }
}

const excluded = excludePath
  ? fs.readFileSync(excludePath, 'utf8').split(/\r?\n/).map((l) => l.trim().toUpperCase()).filter(Boolean)
  : [];
const unknownExcluded = excluded.filter((k) => !seen.has(k));

console.log(`Source:   ${loginPath}`);
console.log(`Firewall: ${pools.fw.length} keys`);
console.log(`AM:       ${pools.am.length} keys`);
if (excludePath) console.log(`Exclude:  ${excluded.length} keys (${unknownExcluded.length} not in either list)`);

if (dryRun) {
  console.log('Dry run: nothing written.');
  process.exit(0);
}

if (!process.env.MONGODB_URI) {
  console.error('MONGODB_URI is not set.');
  process.exit(1);
}

const { MongoClient } = await import('mongodb');
const client = new MongoClient(process.env.MONGODB_URI);
await client.connect();
try {
  const keys = client.db().collection('licenseKeys');
  const importedAt = new Date();

  const ops = [...seen].map(([key, product]) => ({
    updateOne: {
      filter: { _id: key },
      update: { $setOnInsert: { product, checkoutId: null, importedAt } },
      upsert: true,
    },
  }));
  const result = await keys.bulkWrite(ops, { ordered: false });
  console.log(`Inserted ${result.upsertedCount} new keys, ${ops.length - result.upsertedCount} were already in the pool.`);

  if (excluded.length) {
    const marked = await keys.updateMany(
      { _id: { $in: excluded }, checkoutId: null },
      { $set: { checkoutId: 'legacy', assignedAt: importedAt } },
    );
    console.log(`Marked ${marked.modifiedCount} excluded keys as already sold.`);
  }

  await keys.createIndex({ product: 1, checkoutId: 1 });
  for (const product of ['fw', 'am']) {
    const free = await keys.countDocuments({ product, checkoutId: null });
    console.log(`${product.toUpperCase()} keys available: ${free}`);
  }
} finally {
  await client.close();
}
