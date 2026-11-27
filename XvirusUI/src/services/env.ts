// Injected at build time by vite.config.ts from the Vite mode (`--mode am` / `--mode fw`).
declare const __XVIRUS_MODE__: 'am' | 'fw';

export const isFirewall = __XVIRUS_MODE__ === 'fw';
export const isAntimalware = !isFirewall;

// Anti-Malware listens on 5236, Firewall on 5237 so both products can run side-by-side.
export const SERVICE_BASE_URL = isFirewall ? 'http://localhost:5237' : 'http://localhost:5236';
