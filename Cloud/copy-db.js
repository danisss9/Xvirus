const fs = require('node:fs');
const path = require('node:path');

const sourceDirectory = path.join(__dirname, 'public', 'database');
const destinationDirectory = path.join(__dirname, 'sdk', 'Database');
const files = [
  ['viruslist.db', 'viruslist.db'],
  ['dailylist.db', 'dailylist.db'],
  ['whitelist.db', 'whitelist.db'],
  ['dailywl.db', 'dailywl.db'],
  ['heurlist.db', 'heurlist.db'],
  ['heurlist2.db', 'heurlist2.db'],
  ['malvendor.db', 'malvendor.db'],
  ['model.new.ai', 'model.ai'],
  ['scriptmodel.gguf', 'scriptmodel.gguf'],
];

fs.mkdirSync(destinationDirectory, { recursive: true });

for (const [sourceName, destinationName] of files) {
  const source = path.join(sourceDirectory, sourceName);
  const destination = path.join(destinationDirectory, destinationName);
  fs.copyFileSync(source, destination);
  console.log(`Copied ${sourceName} to sdk/Database/${destinationName}`);
}
