import type { CapacitorConfig } from '@capacitor/cli'

const config: CapacitorConfig = {
  appId: 'com.codexcompanion.app',
  appName: 'Codex Companion',
  webDir: 'dist',
  server: {
    // Debug only: the current Stage A relay is ws://. Release keeps https://localhost and must use wss://.
    androidScheme: process.env.CAPACITOR_ANDROID_SCHEME === 'http' ? 'http' : 'https',
  },
  android: {
    backgroundColor: '#f4f7f6',
  },
}

export default config
