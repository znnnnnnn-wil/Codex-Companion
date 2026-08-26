import type { ConnectionState, DeviceCredential, Envelope } from '../protocol/types'
import { Capacitor } from '@capacitor/core'

interface SocketLike {
  readyState: number
  onopen: ((event: Event) => void) | null
  onclose: ((event: CloseEvent) => void) | null
  onerror: ((event: Event) => void) | null
  onmessage: ((event: MessageEvent) => void) | null
  send(data: string): void
  close(): void
}

type SocketFactory = (url: string) => SocketLike

type RequestIdCrypto = Pick<Crypto, 'getRandomValues'> & Partial<Pick<Crypto, 'randomUUID'>>

export function createRequestId(source: RequestIdCrypto = globalThis.crypto): string {
  if (typeof source.randomUUID === 'function') return source.randomUUID()
  const bytes = source.getRandomValues(new Uint8Array(16))
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex = Array.from(bytes, (value) => value.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

export class CompanionSocket {
  private socket: SocketLike | null = null
  private stopped = true
  private attempt = 0
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null
  private readonly messageListeners = new Set<(message: Envelope) => void>()
  private readonly stateListeners = new Set<(state: ConnectionState) => void>()
  private readonly url: string
  private readonly credential: DeviceCredential
  private readonly factory: SocketFactory

  constructor(
    url: string,
    credential: DeviceCredential,
    factory: SocketFactory = (value) => new WebSocket(value),
  ) {
    this.url = url
    this.credential = credential
    this.factory = factory
  }

  start(): void {
    if (!this.stopped) return
    this.stopped = false
    this.connect()
  }

  stop(): void {
    this.stopped = true
    if (this.reconnectTimer) clearTimeout(this.reconnectTimer)
    this.reconnectTimer = null
    const socket = this.socket
    this.socket = null
    if (socket) {
      socket.onopen = null
      socket.onclose = null
      socket.onerror = null
      socket.onmessage = null
      socket.close()
    }
    this.emitState('disconnected')
  }

  onMessage(listener: (message: Envelope) => void): () => void {
    this.messageListeners.add(listener)
    return () => this.messageListeners.delete(listener)
  }

  onState(listener: (state: ConnectionState) => void): () => void {
    this.stateListeners.add(listener)
    return () => this.stateListeners.delete(listener)
  }

  sendRequest(type: string, threadId: string | undefined, payload: unknown): string {
    if (!this.socket || this.socket.readyState !== 1) throw new Error('Relay 尚未连接')
    const requestId = createRequestId()
    this.socket.send(JSON.stringify({
      type,
      requestId,
      deviceId: this.credential.deviceId,
      threadId,
      timestamp: Date.now(),
      payload,
    }))
    return requestId
  }

  private connect(): void {
    if (this.stopped) return
    this.emitState('connecting')
    const socket = this.factory(this.url)
    this.socket = socket
    socket.onopen = () => {
      this.attempt = 0
      socket.send(JSON.stringify({
        type: 'device.hello',
        requestId: createRequestId(),
        deviceId: this.credential.deviceId,
        timestamp: Date.now(),
        payload: {
          deviceId: this.credential.deviceId,
          credential: this.credential.credential,
        },
      }))
      this.emitState('connected')
    }
    socket.onmessage = (event) => {
      try {
        const message = JSON.parse(String(event.data)) as Envelope
        for (const listener of this.messageListeners) listener(message)
      } catch {
        // Ignore malformed frames; the authenticated server will reconnect on protocol failure.
      }
    }
    socket.onerror = () => socket.close()
    socket.onclose = () => {
      if (this.socket === socket) this.socket = null
      this.emitState('disconnected')
      this.scheduleReconnect()
    }
  }

  private scheduleReconnect(): void {
    if (this.stopped || this.reconnectTimer) return
    const delay = Math.min(1_000 * 2 ** this.attempt, 30_000)
    this.attempt += 1
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null
      this.connect()
    }, delay)
  }

  private emitState(state: ConnectionState): void {
    for (const listener of this.stateListeners) listener(state)
  }
}

export function relayWebSocketUrl(): string {
  const configured = import.meta.env.VITE_RELAY_WS_URL as string | undefined
  if (configured) return configured
  if (Capacitor.isNativePlatform()) {
    throw new Error('Android 构建缺少 VITE_RELAY_WS_URL')
  }
  const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:'
  return `${protocol}//${location.host}/ws/web`
}

export async function claimPairing(code: string, url = relayWebSocketUrl()): Promise<DeviceCredential> {
  return await new Promise<DeviceCredential>((resolve, reject) => {
    const socket = new WebSocket(url)
    const timeout = setTimeout(() => {
      socket.close()
      reject(new Error('配对请求超时'))
    }, 10_000)
    socket.onopen = () => socket.send(JSON.stringify({
      type: 'pairing.claim',
      requestId: createRequestId(),
      timestamp: Date.now(),
      payload: { code: code.trim().toUpperCase() },
    }))
    socket.onmessage = (event) => {
      const envelope = JSON.parse(String(event.data)) as Envelope<{ deviceId?: string; webCredential?: string; message?: string }>
      if (envelope.type === 'pairing.claimed' && envelope.payload.deviceId && envelope.payload.webCredential) {
        clearTimeout(timeout)
        socket.close()
        resolve({ deviceId: envelope.payload.deviceId, credential: envelope.payload.webCredential })
      } else if (envelope.type === 'error') {
        clearTimeout(timeout)
        socket.close()
        reject(new Error(envelope.payload.message ?? '配对失败'))
      }
    }
    socket.onerror = () => {
      clearTimeout(timeout)
      reject(new Error('无法连接 Relay'))
    }
  })
}
