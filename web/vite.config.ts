/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { VitePWA } from 'vite-plugin-pwa'
import { appAssets, icons } from './scripts/appAssets.ts'
import { appContentSecurityPolicy, fakeAppHtml } from './src/preview/fakeApp.ts'

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
      // MCP app frames load HTML from Marshal's origin; the preview serves its sample app there with the app CSP.
      configureServer(server: import('vite').ViteDevServer) {
        server.middlewares.use('/mcp-apps/', (_request, response) => {
          response.setHeader('Content-Type', 'text/html; charset=utf-8')
          response.setHeader('Content-Security-Policy', appContentSecurityPolicy)
          response.end(fakeAppHtml)
        })
      },
    },
    appAssets(),
    VitePWA({
      // Always a minified production worker (vitest sets NODE_ENV=test, which would switch Workbox to development).
      mode: 'production',
      registerType: 'autoUpdate',
      injectRegister: 'script',
      // Icons and the offline page are emitted by appAssets; the manifest only references them.
      includeManifestIcons: false,
      manifest: {
        name: 'Marshal',
        short_name: 'Marshal',
        description: 'Your home agent: approvals, chat, printer and memory.',
        display: 'standalone',
        start_url: './',
        scope: './',
        background_color: '#0C0E10',
        theme_color: '#0C0E10',
        icons,
      },
      workbox: {
        // App shell assets only. index.html is never cached: the host rewrites its <base href> per base path.
        globPatterns: ['**/*.{js,css,woff2,png,svg}', 'offline.html', 'offline.css', 'offline.js'],
        globIgnores: ['index.html'],
        navigateFallback: null,
        cleanupOutdatedCaches: true,
        // Navigations always go to the network; without one the static offline page answers. No other route exists,
        // so API calls and /health never touch a cache.
        runtimeCaching: [
          {
            urlPattern: ({ request }) => request.mode === 'navigate',
            handler: 'NetworkOnly',
            options: {
              plugins: [{ handlerDidError: async () => (await caches.match('offline.html', { ignoreSearch: true })) ?? Response.error() }],
            },
          },
        ],
      },
    }),
  ],
  // Relative asset URLs: the host serves the SPA under any configured base path.
  base: './',
  server: {
    proxy: { '/api': api, '/openapi': api, '/signin-oidc': api, '/signout-callback-oidc': api, '/health': api },
  },
  build: { outDir: 'dist', emptyOutDir: true },
  test: { environment: 'jsdom', include: ['src/**/*.test.ts'] },
}))
