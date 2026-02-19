import { defineConfig } from 'vite';
import preactPreset from '@preact/preset-vite';

export default defineConfig({
  plugins: [preactPreset()],
  root: 'src/mainview',
  build: {
    outDir: '../../dist',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    strictPort: true,
  },
});
