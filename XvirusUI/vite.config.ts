import { defineConfig } from 'vite';
import preactPreset from '@preact/preset-vite';

export default defineConfig({
  plugins: [preactPreset()],
  root: 'src',
  publicDir: 'assets',
  build: {
    outDir: '../browser',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    strictPort: true,
  },
});
