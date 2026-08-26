import { afterEach, describe, expect, it, vi } from 'vitest'
import { CompanionSocket, createRequestId } from './CompanionSocket'

class MockSocket {
  readyState = 0
  onopen: ((event: Event) => void) | null = null
  onclose: ((event: CloseEvent) => void) | null = null
  onerror: ((event: Event) => void) | null = null
  onmessage: ((event: MessageEvent) => void) | null = null
  sent: string[] = []
  send(data: string): void { this.sent.push(data) }
  close(): void { this.readyState = 3 }
  fail(): void { this.readyState = 3; this.onclose?.({} as CloseEvent) }
  open(): void { this.readyState = 1; this.onopen?.({} as Event) }
}

describe('CompanionSocket reconnect', () => {
  afterEach(() => vi.useRealTimers())

  it('reconnects with 1s, 2s exponential delays and sends device.hello after open', async () => {
    vi.useFakeTimers()
    const sockets: MockSocket[] = []
    const client = new CompanionSocket(
      'ws://relay/ws/web',
      { deviceId: 'device', credential: 'credential' },
      () => {
        const socket = new MockSocket()
        sockets.push(socket)
        return socket
      },
    )

    client.start()
    expect(sockets).toHaveLength(1)
    sockets[0].fail()
    await vi.advanceTimersByTimeAsync(999)
    expect(sockets).toHaveLength(1)
    await vi.advanceTimersByTimeAsync(1)
    expect(sockets).toHaveLength(2)

    sockets[1].fail()
    await vi.advanceTimersByTimeAsync(1_999)
    expect(sockets).toHaveLength(2)
    await vi.advanceTimersByTimeAsync(1)
    expect(sockets).toHaveLength(3)
    sockets[2].open()

    expect(JSON.parse(sockets[2].sent[0]).type).toBe('device.hello')
    client.stop()
  })

  it('does not let a stopped socket schedule a duplicate reconnect', async () => {
    vi.useFakeTimers()
    const sockets: MockSocket[] = []
    const client = new CompanionSocket(
      'ws://relay/ws/web',
      { deviceId: 'device', credential: 'credential' },
      () => {
        const socket = new MockSocket()
        sockets.push(socket)
        return socket
      },
    )

    client.start()
    client.stop()
    expect(sockets[0].onclose).toBeNull()
    client.start()
    await vi.advanceTimersByTimeAsync(30_000)

    expect(sockets).toHaveLength(2)
    client.stop()
  })
})

describe('createRequestId', () => {
  it('falls back to getRandomValues when randomUUID is unavailable on public HTTP', () => {
    const source = {
      getRandomValues<T extends ArrayBufferView | null>(array: T): T {
        const bytes = array as Uint8Array
        for (let index = 0; index < bytes.length; index++) bytes[index] = index
        return array
      },
    }

    expect(createRequestId(source)).toBe('00010203-0405-4607-8809-0a0b0c0d0e0f')
  })
})
