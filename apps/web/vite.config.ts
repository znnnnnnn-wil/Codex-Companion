import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
import { VitePWA } from 'vite-plugin-pwa'
import { fileURLToPath } from 'node:url'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const basePath = process.env.VITE_BASE_PATH ?? '/'
  return {
    base: basePath,
    server: {
      host: true,
      proxy: { '/ws': { target: 'ws://127.0.0.1:8080', ws: true } },
    },
    plugins: [
      react(),
      ...(mode.startsWith('android') ? [] : [VitePWA({
        registerType: 'autoUpdate',
        includeAssets: ['companion.svg'],
        workbox: {
          navigateFallbackDenylist: [/\/demo(?:\/|$)/],
        },
        manifest: {
          name: 'Codex Companion',
          short_name: 'Companion',
          description: '在手机上继续操作电脑中的真实 Codex 会话',
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
