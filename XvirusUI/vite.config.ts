import { defineConfig } from 'vite';
import preactPreset from '@preact/preset-vite';

// `--mode fw` builds the Firewall UI; any other mode builds the Anti-Malware UI.
export default defineConfig(({ mode }) => ({
  plugins: [preactPreset()],
  root: 'src',
  publicDir: 'assets',
  clearScreen: false,
  define: {
    __XVIRUS_MODE__: JSON.stringify(mode === 'fw' ? 'fw' : 'am'),
  },
  build: {
    outDir: '../browser',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    strictPort: true,
  },
}));
