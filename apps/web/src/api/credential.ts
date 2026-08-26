import type { DeviceCredential } from '../protocol/types'
import { Capacitor } from '@capacitor/core'
import { SecureStorage } from '@aparajita/capacitor-secure-storage'

const key = 'codex-companion.web-credential.v1'
const nativeKey = 'codex-companion.device-credential.v1'

function parseCredential(value: unknown): DeviceCredential | null {
  if (!value) return null
  try {
    const parsed = typeof value === 'string' ? JSON.parse(value) as DeviceCredential : value as DeviceCredential
    return parsed.deviceId && parsed.credential ? parsed : null
  } catch {
    return null
  }
}

export function loadCredential(): DeviceCredential | null {
  try {
    const value = localStorage.getItem(key)
    if (!value) return null
    return parseCredential(value)
  } catch {
    return null
  }
}

export function saveCredential(credential: DeviceCredential): void {
  localStorage.setItem(key, JSON.stringify(credential))
}

export function clearCredential(): void {
  localStorage.removeItem(key)
}

/** Native builds use Android Keystore-backed storage; browser dev keeps localStorage. */
export async function loadCredentialAsync(): Promise<DeviceCredential | null> {
  if (!Capacitor.isNativePlatform()) return loadCredential()
  try {
    const native = parseCredential(await SecureStorage.get(nativeKey))
    if (native) return native
    const legacy = loadCredential()
    if (!legacy) return null
    await SecureStorage.set(nativeKey, JSON.stringify(legacy))
    clearCredential()
    return legacy
  } catch {
    return null
  }
}

export async function saveCredentialAsync(credential: DeviceCredential): Promise<void> {
  if (Capacitor.isNativePlatform()) {
    await SecureStorage.set(nativeKey, JSON.stringify(credential))
    return
  }
  saveCredential(credential)
}

export async function clearCredentialAsync(): Promise<void> {
  clearCredential()
  if (Capacitor.isNativePlatform()) {
    try { await SecureStorage.remove(nativeKey) } catch { /* already absent */ }
  }
}
