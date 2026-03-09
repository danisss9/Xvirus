# Cloud

Xvirus Cloud Scanner backend — Node.js/Express service deployed at `cloud.xvirus.net`.

## Overview

Hosts the Xvirus Cloud Scanner web UI and its API. Integrates the [NodeSDK](../NodeSDK/README.md) to scan uploaded files server-side, caches results in MongoDB, and exposes community voting and update-info endpoints. Also serves the static database files downloaded by the SDK/CLI updater.

## Minimum Requirements

- Node.js 18 or later
- MongoDB instance (URI via environment variable)
- NodeSDK binary built for Linux x64 (see [Build SDK](#build-sdk))

## Get Started

```bash
npm install
npm start
```

The server listens on port 80. Place the NodeSDK files in `./sdk/` and the database files in `./public/database/` before starting.

## Build SDK

Builds the NodeSDK for Linux x64 and copies the output to `./sdk/`:

```bash
npm run build:sdk
```

This runs `dotnet publish` on the `NodeSDK` project and copies `XvirusNodeSDK.node`, `XvirusNodeSDK.mjs`, `import.cjs`, and all `.so` files into `./sdk/`. Create a `settings.json` inside `./sdk/` with `DatabaseFolder` set to `"../public/database"`.

## Deploy

```bash
npm run deploy
```

Deploys to CapRover under the app name `cloud`.

## Environment Variables

| Variable              | Description                                                              |
|-----------------------|--------------------------------------------------------------------------|
| `MONGODB_URI`         | MongoDB connection string                                                |
| `MAX_CONCURRENT_SCANS`| Maximum parallel file scans (default: `3`)                              |

## API Endpoints

### `POST /api/scan`

Scans an uploaded file. Accepts `multipart/form-data` with a `file` field (max 20 MB).

- Returns a cached result if the file was scanned within the last 24 hours.
- Pass `?force=true` to bypass the cache and force a fresh scan (only allowed if cached result is older than 24 hours).
- Rate limit: 20 requests per 15 minutes per IP.
- Concurrent limit: `MAX_CONCURRENT_SCANS` parallel scans.

**Response:**
```json
{
  "isMalware": false,
  "name": "Safe",
  "malwareScore": 0.01,
  "fileName": "example.exe",
  "fileSize": 102400,
  "md5": "D41D8CD98F00B204E9800998ECF8427E",
  "votes": { "allow": 5, "block": 0 },
  "cached": false,
  "scannedAt": "2026-01-01T00:00:00.000Z"
}
```

### `GET /api/vote/:hash`

Returns whether the current IP has already voted for a file identified by its MD5 hash.

**Response:** `{ "voted": false }` or `{ "voted": true, "vote": "allow" }`

### `POST /api/vote`

Submits a community vote for a file. One vote per IP per file.

**Body:** `{ "hash": "<MD5>", "vote": "allow" | "block" }`

**Response:** `{ "allow": 5, "block": 1 }`

### `GET /api/updateInfo`

Returns current database and app versions for the SDK/CLI updater. Pass `?app=sdk`, `?app=cli`, `?app=antimalware`, or `?app=firewall` to get the corresponding app version entry.

### Legacy Endpoints

| Endpoint             | Description                                      |
|----------------------|--------------------------------------------------|
| `POST /cloudscan.php`| Hash lookup — returns `allow,block` vote counts  |
| `POST /submitvote.php`| Submit a vote by hash (legacy format)           |
