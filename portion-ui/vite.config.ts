import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

/** Backend origin used by the dev-server proxy so the browser never needs CORS. */
const BACKEND_ORIGIN = 'http://localhost:5000';

const proxy = {
  '/api': { target: BACKEND_ORIGIN, changeOrigin: true },
  '/health': { target: BACKEND_ORIGIN, changeOrigin: true },
};

/** Split the heavy vendor libraries into their own long-cacheable chunks. */
function vendorChunk(id: string): string | undefined {
  if (!id.includes('node_modules')) return undefined;
  if (/node_modules\/(react|react-dom|react-is|react-router|react-router-dom|scheduler)\//.test(id)) return 'react';
  if (/node_modules\/(recharts|d3-|victory-vendor|decimal\.js-light|fast-equals)/.test(id)) return 'charts';
  if (/node_modules\/(react-markdown|remark|rehype|unified|micromark|mdast|hast|property-information|space-separated|comma-separated|zwitch|longest-streak|ccount|escape-string-regexp|markdown-table|trim-lines|devlop|parse-entities|character-entities|decode-named-character-reference|vfile|vfile-message|unist-util|unified|bail|is-plain-obj|trough)/.test(id)) {
    return 'markdown';
  }
  if (id.includes('node_modules/lucide-react')) return 'icons';
  return 'vendor';
}

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy,
  },
  preview: {
    port: 4173,
    proxy,
  },
  build: {
    sourcemap: true,
    rollupOptions: {
      output: {
        manualChunks: vendorChunk,
      },
    },
  },
});
