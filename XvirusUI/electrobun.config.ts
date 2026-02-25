import type { ElectrobunConfig } from 'electrobun';

const mode = process.env.APP_MODE ?? 'am';
const isFirewall = mode === 'fw';

export default {
  app: {
    name: isFirewall ? 'Xvirus Firewall' : 'Xvirus Anti-Malware',
    identifier: isFirewall ? 'xvirus-firewall.xvirus.net' : 'xvirus-anti-malware.xvirus.net',
    version: '0.0.1',
  },
  runtime: {
    exitOnLastWindowClosed: false,
    mode,
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
      icon: 'src/assets/tray-icon.ico',
    },
  },
} satisfies ElectrobunConfig;
