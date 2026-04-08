const express = require('express');
const { rateLimit } = require('express-rate-limit');
const mongoose = require('mongoose');
const multer = require('multer');
const os = require('os');
const path = require('path');
const fs = require('fs');
const crypto = require('crypto');

const app = express();

// Trust the reverse proxy (CapRover/nginx) so req.ip reflects the real client IP.
app.set('trust proxy', 1);

// ---- SDK setup ---------------------------------------------------------------
// SDK files must be placed in ./sdk/ (XvirusNodeSDK.mjs + XvirusNodeSDK.node).
// settings.json inside ./sdk/ should set DatabaseFolder to "../public/database".

let sdk = null;
let sdkReady = false;

async function loadSDK() {
  try {
    const sdkPath = path.join(__dirname, 'sdk', 'XvirusNodeSDK.mjs');
    if (!fs.existsSync(sdkPath)) {
      console.warn('[SDK] XvirusNodeSDK.mjs not found in ./sdk/ — scanner disabled.');
      return;
    }
    const { XvirusNodeSDK } = await import(sdkPath);
    sdk = XvirusNodeSDK;
    sdk.baseFolder(path.join(__dirname, 'sdk'));
    sdk.load(false);
    sdkReady = true;
    console.log('[SDK] Xvirus engine loaded — version', sdk.version());
  } catch (err) {
    console.error('[SDK] Failed to load engine:', err.message);
  }
}

// ---- Rate limiters -----------------------------------------------------------
// Scan endpoint: expensive CPU work — 20 requests per 15 min per IP.
const scanLimiter = rateLimit({
  windowMs: 15 * 60 * 1000,
  limit: 20,
  standardHeaders: 'draft-8',
  legacyHeaders: false,
  message: { error: 'Too many scan requests. Please try again later.' },
});

// Legacy lookup / vote endpoints: 60 requests per 15 min per IP.
const legacyLimiter = rateLimit({
  windowMs: 15 * 60 * 1000,
  limit: 60,
  standardHeaders: 'draft-8',
  legacyHeaders: false,
  message: 'Too many requests.',
});

// ---- Concurrent scan guard ---------------------------------------------------
// Scanning is CPU-intensive; cap the number of parallel scans.
const MAX_CONCURRENT_SCANS = parseInt(process.env.MAX_CONCURRENT_SCANS ?? '3', 10);
let activeScanCount = 0;

// ---- Multer (file upload) ----------------------------------------------------
const upload = multer({
  dest: os.tmpdir(),
  limits: { fileSize: 20 * 1024 * 1024 }, // 20 MB
});

// ---- MongoDB ----------------------------------------------------------------
mongoose.connect(process.env.MONGODB_URI);
mongoose.model(
  'submissions',
  new mongoose.Schema({
    hash: { type: String, required: true, unique: true },
    allow: { type: Number, required: true, default: 0 },
    block: { type: Number, required: true, default: 0 },
  }),
);

const ipVoteSchema = new mongoose.Schema({
  hash: { type: String, required: true },
  ip: { type: String, required: true },
  vote: { type: String, enum: ['allow', 'block'], required: true },
});
ipVoteSchema.index({ hash: 1, ip: 1 }, { unique: true });
mongoose.model('ipvotes', ipVoteSchema);

mongoose.model(
  'scanResults',
  new mongoose.Schema({
    md5: { type: String, required: true, unique: true },
    isMalware: { type: Boolean, required: true },
    name: { type: String },
    malwareScore: { type: Number, required: true },
    scannedAt: { type: Date, required: true, default: Date.now },
  }),
);

app.use(express.urlencoded({ extended: true }));
app.use(express.json());

// ---- Legacy endpoints -------------------------------------------------------
app.post('/cloudscan.php', legacyLimiter, async (req, res) => {
  const hash = req.body.data;
  try {
    const doc = await mongoose.model('submissions').findOne({ hash });
    res.send(doc == null ? 'notfound' : `${doc.allow},${doc.block}`);
  } catch (err) {
    res.send('notfound');
  }
});
app.post('/submitvote.php', legacyLimiter, async (req, res) => {
  const hash = req.body.data;
  const isBlock = req.body.vote === 'block';
  try {
    await mongoose
      .model('submissions')
      .findOneAndUpdate(
        { hash },
        { $inc: { [isBlock ? 'block' : 'allow']: 1 } },
        { upsert: true, setDefaultsOnInsert: true },
      );
    res.send('success');
  } catch (err) {
    res.send('error');
  }
});

// ---- Scanner endpoint -------------------------------------------------------
app.post('/api/scan', scanLimiter, upload.single('file'), async (req, res) => {
  if (!sdkReady || !sdk) {
    return res.status(503).json({ error: 'Scanner engine not available.' });
  }
  if (!req.file) {
    return res.status(400).json({ error: 'No file uploaded.' });
  }
  if (activeScanCount >= MAX_CONCURRENT_SCANS) {
    fs.unlink(req.file.path, () => {});
    return res.status(429).json({ error: 'Server busy. Please try again shortly.' });
  }

  const force = req.query.force === 'true';
  activeScanCount++;
  const tmpPath = req.file.path;
  try {
    // Calculate MD5 via stream
    const md5 = await new Promise((resolve, reject) => {
      const hash = crypto.createHash('md5');
      fs.createReadStream(tmpPath)
        .on('data', (d) => hash.update(d))
        .on('end', () => resolve(hash.digest('hex').toUpperCase()))
        .on('error', reject);
    });

    const Submissions = mongoose.model('submissions');
    const ScanResults = mongoose.model('scanResults');

    // Look up existing community votes
    const existing = await Submissions.findOne({ hash: md5 });

    // Return cached scan result if available and not forcing a rescan
    const cached = await ScanResults.findOne({ md5 });
    if (cached && !force) {
      const oneDayAgo = new Date(Date.now() - 24 * 60 * 60 * 1000);
      return res.json({
        isMalware: cached.isMalware,
        name: cached.name,
        malwareScore: cached.malwareScore,
        fileName: req.file.originalname,
        fileSize: req.file.size,
        md5,
        votes: { allow: existing?.allow ?? 0, block: existing?.block ?? 0 },
        cached: true,
        scannedAt: cached.scannedAt,
        canRescan: cached.scannedAt < oneDayAgo,
      });
    }

    // Scan
    const result = sdk.scan(tmpPath);
    const scannedAt = new Date();

    // Upsert scan result
    await ScanResults.findOneAndUpdate(
      { md5 },
      {
        isMalware: result.isMalware,
        name: result.name ?? null,
        malwareScore: result.malwareScore,
        scannedAt,
      },
      { upsert: true },
    );

    // Upsert MD5 into submissions (creates doc with 0 votes if new)
    await Submissions.findOneAndUpdate(
      { hash: md5 },
      { $setOnInsert: { allow: 0, block: 0 } },
      { upsert: true },
    );

    res.json({
      isMalware: result.isMalware,
      name: result.name,
      malwareScore: result.malwareScore,
      fileName: req.file.originalname,
      fileSize: req.file.size,
      md5,
      votes: { allow: existing?.allow ?? 0, block: existing?.block ?? 0 },
      cached: false,
      scannedAt,
    });
  } catch (err) {
    res.status(500).json({ error: err.message || 'Scan failed.' });
  } finally {
    activeScanCount--;
    fs.unlink(tmpPath, () => {});
  }
});

// ---- Community vote ---------------------------------------------------------
app.get('/api/vote/:hash', legacyLimiter, async (req, res) => {
  const { hash } = req.params;
  if (!hash || !/^[0-9A-Fa-f]{32}$/.test(hash)) {
    return res.status(400).json({ error: 'Invalid hash.' });
  }
  const doc = await mongoose.model('ipvotes').findOne({
    hash: hash.toUpperCase(),
    ip: req.ip,
  });
  res.json(doc ? { voted: true, vote: doc.vote } : { voted: false });
});

app.post('/api/vote', legacyLimiter, async (req, res) => {
  const { hash, vote } = req.body;
  if (!hash || !/^[0-9A-Fa-f]{32}$/.test(hash)) {
    return res.status(400).json({ error: 'Invalid hash.' });
  }
  if (vote !== 'allow' && vote !== 'block') {
    return res.status(400).json({ error: 'Invalid vote.' });
  }
  const normalHash = hash.toUpperCase();
  try {
    await mongoose.model('ipvotes').create({ hash: normalHash, ip: req.ip, vote });
  } catch (err) {
    if (err.code === 11000) {
      return res.status(409).json({ error: 'You have already voted for this file.' });
    }
    return res.status(500).json({ error: 'Failed to record vote.' });
  }
  const updated = await mongoose
    .model('submissions')
    .findOneAndUpdate(
      { hash: normalHash },
      { $inc: { [vote]: 1 } },
      { upsert: true, setDefaultsOnInsert: true, new: true },
    );
  res.json({ allow: updated.allow, block: updated.block });
});

// ---- Update info ------------------------------------------------------------
app.get('/api/updateInfo', (req, res) => {
  const headerApp = req.query.app;
  const app = headerApp != null ? headerApp.toLowerCase() : 'sdk';

  const result = {
    maindb: {
      version: 105,
      downloadUrl: 'https://cloud.xvirus.net/database/viruslist.db',
      description: 'Main malware database',
    },
    dailydb: {
      version: 15162,
      downloadUrl: 'https://cloud.xvirus.net/database/dailylist.db',
      description: 'Malware database updated daily',
    },
    whitedb: {
      version: 380,
      downloadUrl: 'https://cloud.xvirus.net/database/whitelist.db',
      description: 'Main whitelist database',
    },
    dailywldb: {
      version: 2889,
      downloadUrl: 'https://cloud.xvirus.net/database/dailywl.db',
      description: 'Whitelist database updated daily',
    },
    heurdb: {
      version: 736,
      downloadUrl: 'https://cloud.xvirus.net/database/heurlist.db',
      description: 'Heuristic rules database for PE files',
    },
    heurdb2: {
      version: 104,
      downloadUrl: 'https://cloud.xvirus.net/database/heurlist2.db',
      description: 'Heuristic rules database for script files',
    },
    malvendordb: {
      version: 89,
      downloadUrl: 'https://cloud.xvirus.net/database/malvendor.db',
      description: 'Database of malicious digital signatures',
    },
    aimodel: {
      version: 3,
      downloadUrl: 'https://cloud.xvirus.net/database/model.ai',
      description: 'XvirusAI offline train model',
    },
  };

  switch (app) {
    case 'antimalware':
      result.app = {
        version: '7.0.5.0',
        downloadUrl: 'https://cloud.xvirus.net/download/xvirus-setup.exe',
        description: 'Xvirus Anti-Malware',
      };
      break;
    case 'firewall':
      result.app = {
        version: '4.5.0.0',
        downloadUrl: 'https://cloud.xvirus.net/download/xvirus-firewall-setup.exe',
        description: 'Xvirus Personal Firewall',
      };
      break;
    case 'sdk':
    case 'cli':
    default:
      result.aimodel.downloadUrl = 'https://cloud.xvirus.net/database/model.new.ai';
      result.app = {
        version: '5.1.1.0',
        downloadUrl: 'https://github.com/danisss9/Xvirus/releases',
        description: 'Xvirus Anti-Malware SDK/CLI',
      };
      break;
  }

  res.json(result);
});

app.use(express.static('public', { extensions: ['html'] }));

// ---- Global error handler ---------------------------------------------------
// Catches multer limit errors and any unhandled async errors from routes above.
// eslint-disable-next-line no-unused-vars
app.use((err, req, res, next) => {
  if (err.code === 'LIMIT_FILE_SIZE') {
    return res.status(413).json({ error: 'File too large. Maximum allowed size is 20 MB.' });
  }
  console.error('[Server] Unhandled error:', err.message);
  res.status(500).json({ error: 'Internal server error.' });
});

loadSDK();

app.listen(80, () => console.log('server is listening on port 80!'));
