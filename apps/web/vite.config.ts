import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
import { VitePWA } from 'vite-plugin-pwa'
import { fileURLToPath } from 'node:url'
import { readFileSync } from 'node:fs'

const appVersion = JSON.parse(readFileSync(new URL('./package.json', import.meta.url), 'utf8')).version as string

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const basePath = process.env.VITE_BASE_PATH ?? '/'
  return {
    base: basePath,
    define: { 'import.meta.env.VITE_APP_VERSION': JSON.stringify(appVersion) },
    server: {
      host: true,
      proxy: { '/ws': { target: 'ws://127.0.0.1:8080', ws: true } },
    },
    plugins: [
      react(),
      {
        name: 'companion-version',
        generateBundle() {
          this.emitFile({ type: 'asset', fileName: 'version.json', source: JSON.stringify({ version: appVersion }) })
        },
      },
      ...(mode.startsWith('android') ? [] : [VitePWA({
        registerType: 'autoUpdate',
        includeAssets: ['companion.svg'],
        workbox: {
          navigateFallback: null,
          runtimeCaching: [{
            urlPattern: ({ request }) => request.mode === 'navigate',
            handler: 'NetworkFirst',
            options: { cacheName: 'companion-pages', networkTimeoutSeconds: 5 },
          }],
        },
        manifest: {
          name: 'Codex Companion',
          short_name: 'Companion',
          description: 'Self-hosted mobile access to your real Windows Codex Desktop threads.',
          theme_color: '#0f766e',
          background_color: '#f4f7f6',
          display: 'standalone',
          start_url: basePath,
          icons: [{ src: `${basePath}companion.svg`, sizes: 'any', type: 'image/svg+xml', purpose: 'any maskable' }],
        },
      })]),
    ],
    build: {
      rollupOptions: {
        input: {
          app: fileURLToPath(new URL('./index.html', import.meta.url)),
          demo: fileURLToPath(new URL('./demo/index.html', import.meta.url)),
        },
      },
    },
  }
})
