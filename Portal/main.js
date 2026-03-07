import express from 'express';
import helmet from 'helmet';
import compression from 'compression';

const app = express();

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
        connectSrc: ["'self'"],
      },
    },
    crossOriginEmbedderPolicy: false,
  }),
);

// ── Gzip compression (improves page speed / SEO) ──
app.use(compression());

// ── Parse JSON bodies (limit size to prevent abuse) ──
app.use(express.json({ limit: '16kb' }));

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

// ── 404 catch‑all ──
app.use((_req, res) => {
  res.status(404).sendFile('index.html', { root: 'public' });
});

app.listen(80, () => console.log('server is listening on port 80!'));
