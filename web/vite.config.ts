/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// The API runs on :8080 during development; the proxy marks requests as https (as the TLS reverse proxy does in
// production) so Secure cookies and the antiforgery check work on http://localhost.
const api = { target: 'http://localhost:8080', headers: { 'X-Forwarded-Proto': 'https' } }

export default defineConfig(({ mode }) => ({
  plugins: [
    vue(),
    // Preview mode (`npm run dev:preview`) swaps the entry for the fake-API one; the production entry never imports it.
    mode === 'preview' && {
      name: 'marshal-preview-entry',
      transformIndexHtml: (html: string) => html.replace('src/main.ts', 'src/preview/main.ts'),
    },
  ],
  // Relative asset URLs: the host serves the SPA under any configured base path.
  base: './',
  server: {
    proxy: { '/api': api, '/openapi': api, '/signin-oidc': api, '/signout-callback-oidc': api, '/health': api },
  },
  build: { outDir: 'dist', emptyOutDir: true },
  test: { environment: 'jsdom', include: ['src/**/*.test.ts'] },
}))
