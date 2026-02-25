import type { ElectrobunConfig } from 'electrobun';

export default {
  app: {
    name: 'Xvirus Anti-Malware',
    identifier: 'xvirus-anti-malware.xvirus.net',
    version: '0.0.1',
  },
  runtime: {
    exitOnLastWindowClosed: false,
  },
  build: {
    // Vite builds to dist/, we copy from there
    copy: {
      'dist/index.html': 'views/mainview/index.html',
      'dist/assets': 'views/mainview/assets',
      'src/assets/tray-icon.ico': 'views/assets/tray-icon.ico',
    },
    mac: {
      bundleCEF: false,
    },
    linux: {
      bundleCEF: false,
    },
    win: {
      bundleCEF: false,
    },
  },
} satisfies ElectrobunConfig;
