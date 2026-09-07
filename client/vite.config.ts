// defineConfig comes from vitest/config, not vite, so the `test` block below type-checks.
// This is the single-config benefit in practice: plugins and aliases are declared once
// and both the dev server and the test runner read them.
import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

const API_TARGET = process.env.MUWBTA_API ?? 'http://localhost:5180'

export default defineConfig({
  plugins: [react()],

  resolve: {
    // `@` is src. It exists for the tests in tests/, which sit outside the tree they are about:
    // without it every one of them would open with a run of `../../src/`, and moving a test
    // between tests/builder and tests/game would mean rewriting its imports. Application code is
    // free to use it too, but relative paths inside a feature folder are shorter and stay.
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },

  server: {
    port: 5173,
    strictPort: true,

    // PLAN.md §3.2: auth is an HttpOnly, SameSite=Lax cookie, because the browser's native
    // EventSource cannot set an Authorization header. Proxying through the dev server keeps
    // the browser on a single origin (localhost:5173), so the cookie behaves in development
    // exactly as it will in production. Talking to Kestrel cross-origin instead would force
    // SameSite=None in dev only - which is precisely how cookie bugs ship.
    proxy: {
      '/api': {
        target: API_TARGET,
        changeOrigin: false,
        // Phase 1 note: /api/game/stream is Server-Sent Events. Buffering here would stall
        // the stream the same way a misconfigured reverse proxy does in production, so
        // verify events arrive incrementally through this proxy, not just against Kestrel.
      },
      '/health': {
        target: API_TARGET,
        changeOrigin: false,
      },
    },
  },

  // Vitest reads this same config, so path aliases and plugins are defined once.
  test: {
    environment: 'node',

    // Two tiers, and the split is about what a test is about rather than about how it is written.
    // A test that names one module sits beside it, where a change to the module and a change to
    // its test are one place in a diff. A test that drives several components together — the
    // builder shell, a whole tab, the phone layout — belongs to no single module, and parking it
    // in whichever folder held the component it happened to render first is how
    // `changeFeed.test.tsx` came to sit in builder/ next to no changeFeed.
    include: [
      'src/**/*.test.ts',
      'src/**/*.test.tsx',
      'tests/**/*.test.ts',
      'tests/**/*.test.tsx',
    ],
  },
})
