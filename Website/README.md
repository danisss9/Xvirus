# Website

Xvirus Website — Express server for `xvirus.net`.

## Overview

Serves the Xvirus website and handles the contact form and file/URL submission endpoints. Uploaded files are stored in MongoDB GridFS and notified via email. Uses [ALTCHA](https://altcha.org) proof-of-work for spam protection on all form submissions.

## Minimum Requirements

- Node.js 18 or later
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
