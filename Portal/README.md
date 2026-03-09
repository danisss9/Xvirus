# Portal

Xvirus Portal — Express static file server for the Xvirus web portal.

## Overview

Serves the Xvirus Portal single-page application from the `public/` directory. Applies security headers via Helmet and Gzip compression. All unknown routes fall back to `index.html` for client-side routing.

## Minimum Requirements

- Node.js 18 or later

## Get Started

```bash
npm install
npm start
```

The server listens on port 80 and serves the contents of `./public/`.

## Deploy

```bash
npm run deploy
```

Deploys to CapRover under the app name `portal`.
