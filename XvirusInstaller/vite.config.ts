import { defineConfig } from 'vite';
import preact from '@preact/preset-vite';

export default defineConfig({
  plugins: [preact()],
  root: 'src',
  publicDir: 'assets',
  build: {
    outDir: '../browser',
    emptyOutDir: true,
    minify: 'esbuild',
    target: 'ES2020',
  },
  server: {
    port: 3000,
  },
});
