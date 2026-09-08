import type { Envelope } from '../../protocol/types'

export interface UsageWindow { usedPercent: number | null; windowDurationMins: number | null; resetsAt: number | null }
export interface UsageBucket { limitId: string; limitName: string | null; primary: UsageWindow | null; secondary: UsageWindow | null }
export interface AccountUsage { buckets: UsageBucket[]; fetchedAt: number }
interface UsageState { online: boolean; loading: boolean; data?: AccountUsage; error?: string }

export class UsageStore {
  private state: UsageState = { online: false, loading: false }
  private listeners = new Set<() => void>()
  private pending?: string
  private timer?: ReturnType<typeof setTimeout>
  private autoRefresh = true
  private send: () => string
  constructor(send: () => string) { this.send = send }
  subscribe = (listener: () => void) => { this.listeners.add(listener); return () => { this.listeners.delete(listener) } }
  getSnapshot = () => this.state
  private update(value: Partial<UsageState>) { this.state = { ...this.state, ...value }; this.listeners.forEach((fn) => fn()) }
  setOnline(online: boolean) {
    if (online === this.state.online) return
    this.cancel()
    this.autoRefresh = true
    this.update({ online, loading: false, data: undefined, error: undefined })
    if (online) this.refresh()
  }
  refresh = (automatic = false) => {
    if (!this.state.online || this.pending || (automatic && !this.autoRefresh)) return
    try {
      this.pending = this.send()
      this.update({ loading: true, error: undefined })
      this.timer = setTimeout(() => this.fail(), 35_000)
    } catch { this.fail() }
  }
  apply(envelope: Envelope): boolean {
    if (!this.pending || envelope.requestId !== this.pending) return false
    if (envelope.type === 'error') { this.fail(); return true }
    if (envelope.type !== 'account.rateLimits.response') return false
    const data = envelope.payload as AccountUsage
    this.cancel()
    if (!data || !Array.isArray(data.buckets) || !Number.isFinite(data.fetchedAt)) { this.fail(); return true }
    this.autoRefresh = true
    this.update({ loading: false, data, error: undefined })
    return true
  }
  private fail() {
    this.cancel()
    this.autoRefresh = false
    this.update({ loading: false, data: undefined, error: '暂时无法读取额度，请检查电脑端 ChatGPT 登录，并升级 Relay、Bridge 和 Codex CLI 后重试。' })
  }
  private cancel() { clearTimeout(this.timer); this.pending = undefined }
  dispose() { this.cancel() }
}

export function remainingPercent(used: number | null): number | null {
  return typeof used === 'number' && Number.isFinite(used) ? Math.round(Math.max(0, Math.min(100, 100 - used))) : null
}
export function windowLabel(minutes: number | null, fallback: string): string {
  if (!minutes || minutes < 0) return fallback
  if (minutes % 10080 === 0) return `${minutes / 10080} 周`
  if (minutes % 1440 === 0) return `${minutes / 1440} 天`
  if (minutes % 60 === 0) return `${minutes / 60} 小时`
  return `${minutes} 分钟`
}
