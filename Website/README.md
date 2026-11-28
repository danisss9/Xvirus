# Website

Xvirus Website — Express server for `xvirus.net`.

## Overview

Serves the Xvirus website and handles the contact form and file/URL submission endpoints. Uploaded files are stored in MongoDB GridFS and notified via email. Uses [ALTCHA](https://altcha.org) proof-of-work for spam protection on all form submissions.

## Minimum Requirements

- Node.js 26 or later (current LTS)
- MongoDB instance (URI via environment variable)
- SMTP server for outbound email

## Get Started

```bash
npm install
npm start
```

The server listens on port 80.

## Deploy

```bash
npm run deploy
```

Deploys to CapRover under the app name `www`.

## Environment Variables

| Variable          | Description                                                                    |
| ----------------- | ------------------------------------------------------------------------------ |
| `MONGODB_URI`     | MongoDB connection string (used for GridFS file storage)                       |
| `ALTCHA_HMAC_KEY` | HMAC key for ALTCHA PoW challenge signing. Auto-generated if not set.          |
| `SMTP_HOST`       | SMTP server hostname                                                           |
| `SMTP_PORT`       | SMTP server port (default: `465`)                                              |
| `SMTP_USER`       | SMTP username / sender address                                                 |
| `SMTP_PASS`       | SMTP password                                                                  |
| `CONTACT_EMAIL`   | Recipient address for contact form messages                                    |
| `SAMPLES_EMAIL`   | Recipient address for file/URL submission notifications                        |
| `SITE_URL`        | Public base URL (e.g. `https://xvirus.net`) — used in download links in emails |
| `SUBMISSION_AUTH` | Secret token required to download or delete submitted files via the API        |
| `POLAR_ACCESS_TOKEN` | Polar organization access token with `checkouts:read` (license delivery)    |
| `POLAR_PRODUCTS`  | JSON map of Polar product ID → key pool and keys per purchase, e.g. `{"<uuid>":{"product":"fw","seats":3}}`. `product` is `fw` or `am`. A seat count on the checkout (seat-based pricing) overrides `seats`. |
| `POLAR_API_URL`   | Polar API base URL (default `https://api.polar.sh/v1`; sandbox: `https://sandbox-api.polar.sh/v1`) |
| `POLAR_API_VERSION` | Value of the `Polar-Version` header (default `2026-10`)                      |

`CONTACT_EMAIL` also receives alerts when a paid checkout can't be fulfilled (unknown product, key pool empty).

## License Delivery (Polar)

After a purchase, Polar redirects the buyer to `/thanks?checkout_id={CHECKOUT_ID}`. The page calls `POST /license/claim`, which:

1. Verifies the checkout with the Polar API (`status` must be `succeeded`; `open`/`confirmed` make the page wait and retry).
2. Reserves one random unused key per seat from the pool for that product.
3. Emails the keys to the checkout's `customer_email` and shows them on the page.

Each checkout ID is processed once. Later requests for a processed checkout do nothing. A checkout whose email failed or whose pool ran out is retried on the next page load, keeping any keys it already reserved.

**Setup**

1. Set the success URL of every Polar product/checkout link to `https://xvirus.net/thanks?checkout_id={CHECKOUT_ID}`.
2. Set `POLAR_ACCESS_TOKEN` and `POLAR_PRODUCTS` (one entry per pack product).
3. Load the key pool into MongoDB (keys never go in this repo):

   ```bash
   MONGODB_URI=mongodb://... npm run import-keys -- --login ../../Login/main.js [--exclude sold-keys.txt]
   ```

   It reads the `licenses` (Firewall) and `amLicenses` (Anti-Malware) arrays. Re-running only adds new keys. `--exclude` takes a file with one key per line that were already sold, so they're never handed out again. Use `--dry-run` to check the counts first.

MongoDB collections: `licenseKeys` (`_id` = key, `product`, `checkoutId` once assigned) and `licenseCheckouts` (one document per processed checkout, with its status, email and keys).

### `POST /license/claim`

**Body:** `{ "checkoutId" }`. Returns `{ status }`, where `status` is one of `processed` (with `keys`, `product`, masked `email`), `email_failed` (with `keys`), `already_processed`, `pending` or `error` (with `error`). Rate limit: 60 requests per 15 minutes per IP.

## API Endpoints

### `GET /altcha-challenge`

Returns a fresh ALTCHA proof-of-work challenge. Must be solved client-side and submitted with contact/submission forms.

### `POST /contact`

Sends a contact form message by email. Rate limit: 5 requests per 15 minutes per IP.

**Body:** `{ "name", "email", "subject", "message", "altcha" }`

### `POST /submit`

Submits a suspicious or safe file (`.zip`, max 20 MB) or URL for analysis. The file is stored in MongoDB GridFS and a notification email with download/delete links is sent to `SAMPLES_EMAIL`. Rate limit: 5 requests per 15 minutes per IP.

**Body (multipart/form-data):** `name`, `email`, `rating` (`suspicious` | `safe`), `kind` (`file` | `url`), `url` (if `kind=url`), `file` (if `kind=file`), `altcha`

### `GET /submission/file/:id`

Downloads a submitted file from GridFS. Requires `?auth=<SUBMISSION_AUTH>`.

### `GET /submission/file/:id/delete`

Deletes a submitted file from GridFS. Requires `?auth=<SUBMISSION_AUTH>`.
