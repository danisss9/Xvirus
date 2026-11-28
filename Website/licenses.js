// ══════════════════════════════════════════════════════════
//   License delivery for Polar.sh purchases
//
//   Polar redirects buyers to /thanks?checkout_id={CHECKOUT_ID}. The page calls
//   POST /license/claim, which verifies the checkout with the Polar API, reserves
//   one random key per seat from the pre-loaded pool and emails them to the buyer.
//
//   The key pool lives only in MongoDB (collection `licenseKeys`), never in this
//   repository. Load it with `npm run import-keys` (see scripts/import-license-keys.mjs).
// ══════════════════════════════════════════════════════════

import rateLimit from 'express-rate-limit';

const POLAR_API_URL = (process.env.POLAR_API_URL || 'https://api.polar.sh/v1').replace(/\/+$/, '');
const POLAR_API_VERSION = process.env.POLAR_API_VERSION || '2026-10';
const POLAR_ACCESS_TOKEN = process.env.POLAR_ACCESS_TOKEN || '';

const PRODUCT_NAMES = { fw: 'Xvirus Firewall Pro', am: 'Xvirus Anti-Malware Pro' };

// A checkout in one of these states can be picked up again by the next request.
const RETRYABLE = ['email_failed', 'out_of_keys'];
// A `processing` lock older than this is treated as abandoned (e.g. the server restarted mid-way).
const STALE_LOCK_MS = 5 * 60 * 1000;

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// POLAR_PRODUCTS maps Polar product IDs to their key pool, e.g. {"<product-uuid>": {"product": "fw"}}.
// One key is issued per seat on the checkout (seat-based pricing). `seats` is only a fallback
// for products without seat pricing, e.g. {"product": "fw", "seats": 3} for a fixed 3-PC pack.
function loadProductMap() {
  const raw = process.env.POLAR_PRODUCTS;
  if (!raw) {
    console.warn('POLAR_PRODUCTS is not set: license claims will be rejected.');
    return {};
  }
  try {
    const parsed = JSON.parse(raw);
    for (const [id, cfg] of Object.entries(parsed)) {
      if (!PRODUCT_NAMES[cfg?.product]) {
        throw new Error(`product "${cfg?.product}" for ${id} must be "fw" or "am"`);
      }
    }
    return parsed;
  } catch (err) {
    console.error('Invalid POLAR_PRODUCTS:', err.message);
    return {};
  }
}

const positiveInt = (v) => (Number.isInteger(v) && v > 0 ? v : null);

const maskEmail = (email) => {
  const [user, domain] = String(email).split('@');
  if (!domain) return '';
  return `${user.slice(0, 2)}${'*'.repeat(Math.max(1, user.length - 2))}@${domain}`;
};

export async function registerLicenseRoutes(app, { db, transporter, esc }) {
  const products = loadProductMap();
  const keys = db.collection('licenseKeys'); // { _id: key, product, checkoutId: null | id, assignedAt }
  const checkouts = db.collection('licenseCheckouts'); // { _id: checkoutId, status, product, seats, email, keys, ... }

  await keys.createIndex({ product: 1, checkoutId: 1 });

  // ── Polar API ──
  async function fetchCheckout(checkoutId) {
    const res = await fetch(`${POLAR_API_URL}/checkouts/${checkoutId}`, {
      headers: {
        Authorization: `Bearer ${POLAR_ACCESS_TOKEN}`,
        Accept: 'application/json',
        'Polar-Version': POLAR_API_VERSION,
      },
      signal: AbortSignal.timeout(10_000),
    });
    if (res.status === 404 || res.status === 422) return null;
    if (!res.ok) throw new Error(`Polar API responded ${res.status}: ${await res.text()}`);
    return res.json();
  }

  // ── Reserve one random unused key, atomically ──
  async function reserveRandomKey(product, checkoutId) {
    for (let attempt = 0; attempt < 10; attempt++) {
      const [candidate] = await keys
        .aggregate([{ $match: { product, checkoutId: null } }, { $sample: { size: 1 } }])
        .toArray();
      if (!candidate) return null; // pool is empty
      // Only succeeds if nobody else took this key between the sample and the update.
      const result = await keys.updateOne(
        { _id: candidate._id, checkoutId: null },
        { $set: { checkoutId, assignedAt: new Date() } },
      );
      if (result.modifiedCount === 1) return candidate._id;
    }
    return null;
  }

  // ── Emails ──
  async function sendKeysEmail(to, product, licenseKeys) {
    const name = PRODUCT_NAMES[product];
    const plural = licenseKeys.length > 1;
    const subject = `Your ${name} license key${plural ? 's' : ''}`;
    const intro = `Thank you for purchasing ${name}! Here ${plural ? 'are your license keys' : 'is your license key'}:`;
    const howTo = plural
      ? 'Each key activates one PC. Open Xvirus on each computer, open the Pro activation window and enter one key.'
      : 'Open Xvirus, open the Pro activation window and enter the key above.';

    await transporter.sendMail({
      from: process.env.SMTP_USER,
      to,
      replyTo: process.env.CONTACT_EMAIL || undefined,
      subject,
      text: `${intro}\n\n${licenseKeys.join('\n')}\n\n${howTo}\n\nKeep this email safe. If you need help, just reply to it.\n\nThe Xvirus team`,
      html: `<p>${esc(intro)}</p>
             <ul style="padding-left:18px">${licenseKeys
               .map((k) => `<li style="font-family:Consolas,monospace;font-size:16px;margin:6px 0"><strong>${esc(k)}</strong></li>`)
               .join('')}</ul>
             <p>${esc(howTo)}</p>
             <p>Keep this email safe. If you need help, just reply to it.</p>
             <p>The Xvirus team</p>`,
    });
  }

  async function notifyAdmin(subject, lines) {
    if (!process.env.CONTACT_EMAIL) return;
    try {
      await transporter.sendMail({
        from: process.env.SMTP_USER,
        to: process.env.CONTACT_EMAIL,
        subject: `[Licenses] ${subject}`,
        text: lines.join('\n'),
      });
    } catch (err) {
      console.error('Admin notification failed:', err);
    }
  }

  // ── Reserve the missing keys for a locked checkout and email them ──
  // `doc` holds the checkout as it was before this request locked it.
  async function fulfil(doc) {
    const checkoutId = doc._id;
    const now = () => new Date();

    // The key collection is the source of truth, so keys reserved before a crash are reused.
    const assigned = (await keys.find({ checkoutId }, { projection: { _id: 1 } }).toArray()).map((k) => k._id);
    while (assigned.length < doc.seats) {
      const key = await reserveRandomKey(doc.product, checkoutId);
      if (!key) break;
      assigned.push(key);
    }

    if (assigned.length < doc.seats) {
      await checkouts.updateOne(
        { _id: checkoutId },
        { $set: { status: 'out_of_keys', keys: assigned, updatedAt: now() } },
      );
      if (doc.status !== 'out_of_keys') {
        await notifyAdmin(`Out of ${doc.product.toUpperCase()} keys`, [
          `Checkout ${checkoutId} needs ${doc.seats} key(s) but only ${assigned.length} could be reserved.`,
          `Customer: ${doc.email}`,
          'Import more keys, then the customer (or you) can reload the thank-you page to finish delivery.',
        ]);
      }
      return {
        http: 503,
        body: {
          status: 'error',
          error: 'We could not issue your license automatically. Our team has been notified and will email your key shortly.',
        },
      };
    }

    try {
      await sendKeysEmail(doc.email, doc.product, assigned);
    } catch (err) {
      console.error(`License email failed for checkout ${checkoutId}:`, err);
      await checkouts.updateOne(
        { _id: checkoutId },
        { $set: { status: 'email_failed', keys: assigned, error: String(err.message), updatedAt: now() } },
      );
      // Still show the keys: the customer has paid.
      return { http: 200, body: { status: 'email_failed', product: PRODUCT_NAMES[doc.product], keys: assigned } };
    }

    await checkouts.updateOne(
      { _id: checkoutId },
      { $set: { status: 'processed', keys: assigned, emailSentAt: now(), updatedAt: now() }, $unset: { error: '' } },
    );
    return {
      http: 200,
      body: { status: 'processed', product: PRODUCT_NAMES[doc.product], keys: assigned, email: maskEmail(doc.email) },
    };
  }

  const claimLimiter = rateLimit({
    windowMs: 15 * 60 * 1000,
    max: 60, // the page polls while a payment is still confirming
    message: { status: 'error', error: 'Too many requests. Please try again in a few minutes.' },
    standardHeaders: true,
    legacyHeaders: false,
  });

  // ── POST /license/claim ── { checkoutId } ──
  app.post('/license/claim', claimLimiter, async (req, res) => {
    const checkoutId = String(req.body?.checkoutId ?? '').trim().toLowerCase();
    if (!UUID_RE.test(checkoutId)) {
      return res.status(400).json({ status: 'error', error: 'Invalid checkout reference.' });
    }
    if (!POLAR_ACCESS_TOKEN) {
      console.error('POLAR_ACCESS_TOKEN is not set.');
      return res.status(500).json({ status: 'error', error: 'License delivery is not configured. Please contact support.' });
    }

    try {
      // ── Seen before: only resume a failed or abandoned attempt, otherwise do nothing ──
      if (await checkouts.findOne({ _id: checkoutId }, { projection: { _id: 1 } })) {
        const previous = await checkouts.findOneAndUpdate(
          {
            _id: checkoutId,
            $or: [
              { status: { $in: RETRYABLE } },
              { status: 'processing', updatedAt: { $lt: new Date(Date.now() - STALE_LOCK_MS) } },
            ],
          },
          { $set: { status: 'processing', updatedAt: new Date() } },
          { returnDocument: 'before' },
        );
        if (!previous) return res.json({ status: 'already_processed' });
        const result = await fulfil(previous);
        return res.status(result.http).json(result.body);
      }

      // ── New checkout: verify it with Polar ──
      const checkout = await fetchCheckout(checkoutId);
      if (!checkout) {
        return res.status(404).json({ status: 'error', error: 'We could not find this purchase.' });
      }
      if (checkout.status === 'open' || checkout.status === 'confirmed') {
        return res.json({ status: 'pending' });
      }
      if (checkout.status !== 'succeeded') {
        return res.status(400).json({ status: 'error', error: 'This payment was not completed.' });
      }

      const cfg = products[checkout.product_id];
      if (!cfg) {
        console.error(`Checkout ${checkoutId} is for unmapped product ${checkout.product_id}.`);
        await notifyAdmin('Unmapped product', [
          `Checkout ${checkoutId} succeeded for product ${checkout.product_id}, which is not in POLAR_PRODUCTS.`,
          `Customer: ${checkout.customer_email}`,
        ]);
        return res.status(500).json({
          status: 'error',
          error: 'We could not issue your license automatically. Our team has been notified and will email your key shortly.',
        });
      }
      // Seat-based products report the purchased seats on the checkout. Never guess a count:
      // under-delivering a 5-PC purchase is worse than asking support to step in.
      const seats = positiveInt(checkout.seats) ?? positiveInt(cfg.seats);
      if (!seats) {
        console.error(`Checkout ${checkoutId} has no seat count and POLAR_PRODUCTS sets no "seats".`);
        await notifyAdmin('Unknown seat count', [
          `Checkout ${checkoutId} (product ${checkout.product_id}) has no seat count.`,
          `Customer: ${checkout.customer_email}`,
        ]);
        return res.status(500).json({
          status: 'error',
          error: 'We could not issue your license automatically. Our team has been notified and will email your key shortly.',
        });
      }
      if (!checkout.customer_email) {
        console.error(`Checkout ${checkoutId} has no customer email.`);
        return res.status(500).json({ status: 'error', error: 'Your purchase has no email address. Please contact support.' });
      }

      const doc = {
        _id: checkoutId,
        status: 'processing',
        product: cfg.product,
        productId: checkout.product_id,
        seats,
        email: checkout.customer_email,
        keys: [],
        createdAt: new Date(),
        updatedAt: new Date(),
      };

      // The unique _id is the lock: a concurrent request for the same checkout loses here.
      try {
        await checkouts.insertOne(doc);
      } catch (err) {
        if (err?.code === 11000) return res.json({ status: 'already_processed' });
        throw err;
      }

      const result = await fulfil(doc);
      return res.status(result.http).json(result.body);
    } catch (err) {
      console.error(`License claim error for checkout ${checkoutId}:`, err);
      return res.status(500).json({ status: 'error', error: 'Something went wrong. Please reload this page in a minute.' });
    }
  });
}
