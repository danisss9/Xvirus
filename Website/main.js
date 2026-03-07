import express from 'express';
import nodemailer from 'nodemailer';
import helmet from 'helmet';
import compression from 'compression';
import rateLimit from 'express-rate-limit';
import { createChallenge, verifySolution } from 'altcha-lib';
import crypto from 'node:crypto';
import multer from 'multer';
import fs from 'node:fs';
import { MongoClient, GridFSBucket, ObjectId } from 'mongodb';

// ── ALTCHA HMAC key (generate a unique key for your deployment) ──
const ALTCHA_HMAC_KEY = process.env.ALTCHA_HMAC_KEY || crypto.randomBytes(32).toString('hex');

const app = express();

// ── Trust first proxy (needed for express-rate-limit behind a reverse proxy) ──
app.set('trust proxy', 1);

// ── MongoDB connection & GridFS ──
const mongoClient = new MongoClient(process.env.MONGODB_URI);
await mongoClient.connect();
const db = mongoClient.db();
const gridFSBucket = new GridFSBucket(db, { bucketName: 'submissions' });

// ── Base URL for download links (defaults to SITE_URL env var) ──
const SITE_URL = process.env.SITE_URL || '';

// ── Security headers ──
app.use(
  helmet({
    contentSecurityPolicy: {
      directives: {
        defaultSrc: ["'self'"],
        styleSrc: ["'self'", "'unsafe-inline'", 'https://fonts.googleapis.com'],
        fontSrc: ["'self'", 'https://fonts.gstatic.com'],
        imgSrc: ["'self'", 'data:'],
        scriptSrc: ["'self'", "'unsafe-inline'", 'https://cdn.jsdelivr.net'],
        workerSrc: ["'self'", 'blob:'],
        connectSrc: ["'self'", 'https://cdn.jsdelivr.net'],
      },
    },
    crossOriginEmbedderPolicy: false,
  }),
);

// ── Gzip compression (improves page speed / SEO) ──
app.use(compression());

// ── Parse JSON bodies (limit size to prevent abuse) ──
app.use(express.json({ limit: '16kb' }));

// ── Rate limiter for contact form ──
const contactLimiter = rateLimit({
  windowMs: 15 * 60 * 1000, // 15 minutes
  max: 5, // 5 submissions per window
  message: { error: 'Too many messages sent. Please try again later.' },
  standardHeaders: true,
  legacyHeaders: false,
});

// ── Static files with caching ──
app.use(
  express.static('public', {
    extensions: ['html'],
    maxAge: '7d',
    setHeaders(res, filePath) {
      // Don't cache HTML (so meta‑tag updates propagate fast)
      if (filePath.endsWith('.html')) {
        res.setHeader('Cache-Control', 'no-cache');
      }
    },
  }),
);

// ── Email transporter (update host / auth to match your SMTP provider) ──
const transporter = nodemailer.createTransport({
  host: process.env.SMTP_HOST,
  port: Number(process.env.SMTP_PORT) || 465,
  secure: true,
  auth: {
    user: process.env.SMTP_USER,
    pass: process.env.SMTP_PASS,
  },
});

// ── GET /altcha-challenge ── serves a fresh PoW challenge ──
app.get('/altcha-challenge', async (_req, res) => {
  try {
    const challenge = await createChallenge({
      hmacKey: ALTCHA_HMAC_KEY,
      maxNumber: 50000,
    });
    res.json(challenge);
  } catch (err) {
    console.error('ALTCHA challenge error:', err);
    res.status(500).json({ error: 'Failed to create challenge.' });
  }
});

// ── Sanitise user input before embedding in HTML email ──
const esc = (s) =>
  String(s)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');

// ── POST /contact (rate‑limited) ──
app.post('/contact', contactLimiter, async (req, res) => {
  const { name, email, subject, message, altcha } = req.body;

  if (!name || !email || !subject || !message) {
    return res.status(400).json({ error: 'All fields are required.' });
  }

  // Verify ALTCHA PoW solution
  if (!altcha) {
    return res.status(400).json({ error: 'ALTCHA verification is required.' });
  }
  const altchaOk = await verifySolution(altcha, ALTCHA_HMAC_KEY);
  if (!altchaOk) {
    return res.status(400).json({ error: 'ALTCHA verification failed.' });
  }

  try {
    await transporter.sendMail({
      from: process.env.SMTP_USER,
      to: process.env.CONTACT_EMAIL,
      replyTo: email,
      subject: `[Contact] ${esc(subject)}`,
      text: `Name: ${name}\nEmail: ${email}\n\n${message}`,
      html: `<p><strong>Name:</strong> ${esc(name)}</p>
             <p><strong>Email:</strong> ${esc(email)}</p>
             <hr>
             <p>${esc(message).replace(/\n/g, '<br>')}</p>`,
    });

    res.json({ ok: true });
  } catch (err) {
    console.error('Email send error:', err);
    res.status(500).json({ error: 'Failed to send email.' });
  }
});

// ── Multer config for file submissions (20 MB limit, .zip only) ──
const upload = multer({
  dest: 'uploads/',
  limits: { fileSize: 20 * 1024 * 1024 },
  fileFilter(_req, file, cb) {
    if (file.originalname.toLowerCase().endsWith('.zip')) {
      cb(null, true);
    } else {
      cb(new Error('Only .zip files are allowed.'));
    }
  },
});

// ── Rate limiter for submissions ──
const submitLimiter = rateLimit({
  windowMs: 15 * 60 * 1000,
  max: 5,
  message: { error: 'Too many submissions. Please try again later.' },
  standardHeaders: true,
  legacyHeaders: false,
});

// ── POST /submit (file/URL submission) ──
app.post(
  '/submit',
  submitLimiter,
  (req, res, next) => {
    // Use multer single-file upload; handle size/type errors gracefully
    upload.single('file')(req, res, (err) => {
      if (err instanceof multer.MulterError) {
        if (err.code === 'LIMIT_FILE_SIZE') {
          return res.status(400).json({ error: 'File must be no larger than 20 MB.' });
        }
        return res.status(400).json({ error: err.message });
      }
      if (err) {
        return res.status(400).json({ error: err.message });
      }
      next();
    });
  },
  async (req, res) => {
    const { name, email, rating, kind, url, altcha } = req.body;

    if (!name || !email || !rating || !kind) {
      return res
        .status(400)
        .json({ error: 'Name, email, rating, and submission kind are required.' });
    }
    if (!['suspicious', 'safe'].includes(rating)) {
      return res.status(400).json({ error: 'Invalid rating.' });
    }
    if (!['file', 'url'].includes(kind)) {
      return res.status(400).json({ error: 'Invalid submission kind.' });
    }

    // Verify ALTCHA
    if (!altcha) {
      return res.status(400).json({ error: 'ALTCHA verification is required.' });
    }
    const altchaOk = await verifySolution(altcha, ALTCHA_HMAC_KEY);
    if (!altchaOk) {
      return res.status(400).json({ error: 'ALTCHA verification failed.' });
    }

    // Validate kind-specific fields
    if (kind === 'url' && !url) {
      return res.status(400).json({ error: 'URL is required for URL submissions.' });
    }
    if (kind === 'file' && !req.file) {
      return res.status(400).json({ error: 'A .zip file is required for file submissions.' });
    }

    let fileId = null;
    try {
      const ratingLabel = rating === 'suspicious' ? 'Suspicious' : 'Safe';
      const subjectLine =
        kind === 'file'
          ? `[Submission] File (${ratingLabel}) from ${esc(name)}`
          : `[Submission] URL (${ratingLabel}) from ${esc(name)}`;

      const bodyParts = [
        `<p><strong>Name:</strong> ${esc(name)}</p>`,
        `<p><strong>Email:</strong> ${esc(email)}</p>`,
        `<p><strong>Rating:</strong> ${esc(ratingLabel)}</p>`,
        `<p><strong>Kind:</strong> ${esc(kind)}</p>`,
      ];

      if (kind === 'url') {
        bodyParts.push(`<p><strong>URL:</strong> ${esc(url)}</p>`);
      }

      // Upload file to GridFS if present
      if (kind === 'file' && req.file) {
        fileId = await new Promise((resolve, reject) => {
          const uploadStream = gridFSBucket.openUploadStream(req.file.originalname, {
            metadata: { name, email, rating, uploadedAt: new Date() },
          });
          const readStream = fs.createReadStream(req.file.path);
          readStream
            .pipe(uploadStream)
            .on('finish', () => resolve(uploadStream.id))
            .on('error', reject);
        });

        // Clean up temp file after GridFS upload
        fs.unlink(req.file.path, () => {});

        const authParam = `auth=${encodeURIComponent(process.env.SUBMISSION_AUTH || '')}`;
        const downloadLink = `${SITE_URL}/submission/file/${fileId}?${authParam}`;
        const deleteLink = `${SITE_URL}/submission/file/${fileId}/delete?${authParam}`;
        bodyParts.push(
          `<p><strong>File:</strong> ${esc(req.file.originalname)} (${(req.file.size / 1024).toFixed(1)} KB)</p>`,
        );
        bodyParts.push(
          `<p><strong>Download:</strong> <a href="${downloadLink}">${downloadLink}</a></p>`,
        );
        bodyParts.push(`<p><strong>Delete:</strong> <a href="${deleteLink}">${deleteLink}</a></p>`);
      }

      const mailOptions = {
        from: process.env.SMTP_USER,
        to: process.env.SAMPLES_EMAIL,
        replyTo: email,
        subject: subjectLine,
        text: bodyParts.map((p) => p.replace(/<[^>]+>/g, '')).join('\n'),
        html: bodyParts.join('\n'),
      };

      await transporter.sendMail(mailOptions);

      // Clean up uploaded file
      if (req.file) {
        fs.unlink(req.file.path, () => {});
      }

      res.json({ ok: true });
    } catch (err) {
      // Clean up uploaded file on error
      if (req.file) {
        fs.unlink(req.file.path, () => {});
      }
      console.error('Submit email error:', err);
      res.status(500).json({ error: 'Failed to send submission.' });
    }
  },
);

// ── GET /submission/file/:id ── download a submitted file from GridFS ──
app.get('/submission/file/:id', async (req, res) => {
  if (!process.env.SUBMISSION_AUTH || req.query.auth !== process.env.SUBMISSION_AUTH) {
    return res.status(403).json({ error: 'Unauthorized.' });
  }

  try {
    const fileId = ObjectId.createFromHexString(req.params.id);
    const files = await gridFSBucket.find({ _id: fileId }).toArray();

    if (!files.length) {
      return res.status(404).json({ error: 'File not found.' });
    }

    const file = files[0];
    res.set('Content-Type', 'application/zip');
    res.set('Content-Disposition', `attachment; filename="${file.filename}"`);

    const downloadStream = gridFSBucket.openDownloadStream(fileId);
    downloadStream.pipe(res);
  } catch (err) {
    console.error('File download error:', err);
    res.status(400).json({ error: 'Invalid file ID.' });
  }
});

// ── GET /submission/file/:id/delete ── delete a submitted file from GridFS ──
app.get('/submission/file/:id/delete', async (req, res) => {
  if (!process.env.SUBMISSION_AUTH || req.query.auth !== process.env.SUBMISSION_AUTH) {
    return res.status(403).json({ error: 'Unauthorized.' });
  }

  try {
    const fileId = ObjectId.createFromHexString(req.params.id);
    const files = await gridFSBucket.find({ _id: fileId }).toArray();

    if (!files.length) {
      return res.status(404).json({ error: 'File not found.' });
    }

    await gridFSBucket.delete(fileId);
    res.json({ ok: true, message: 'File deleted.' });
  } catch (err) {
    console.error('File delete error:', err);
    res.status(400).json({ error: 'Invalid file ID.' });
  }
});

// ── 404 catch‑all ──
app.use((_req, res) => {
  res.status(404).sendFile('index.html', { root: 'public' });
});

app.listen(80, () => console.log('server is listening on port 80!'));
