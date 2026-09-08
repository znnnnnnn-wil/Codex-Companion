import { useEffect, useState, useSyncExternalStore } from 'react'
import { remainingPercent, windowLabel, type UsageStore, type UsageWindow } from './UsageStore'

export function UsagePanel({ store }: { store: UsageStore }) {
  const state = useSyncExternalStore(store.subscribe, store.getSnapshot)
  const [now, setNow] = useState(Date.now)
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 30_000)
    return () => clearInterval(timer)
  }, [])
  return <section className="usage-panel" aria-label="剩余用量" aria-busy={state.loading}>
    <div className="usage-heading"><strong>剩余用量</strong><button type="button" onClick={() => store.refresh()} disabled={!state.online || state.loading} aria-label="刷新额度">{state.loading ? '读取中…' : '刷新'}</button></div>
    {!state.online ? <p>电脑离线，连接后显示额度</p> : state.error ? <p role="status">{state.error}</p> : state.data ? <>
      {state.data.buckets.length === 0 && <p>当前账户未提供额度数据</p>}
      {state.data.buckets.map((bucket) => <div className="usage-bucket" key={bucket.limitId}>
        {(state.data!.buckets.length > 1 || bucket.limitName) && <strong className="usage-bucket-name">{bucket.limitName || bucket.limitId}</strong>}
        <WindowRow window={bucket.primary} fallback="主额度" now={now} />
        <WindowRow window={bucket.secondary} fallback="次额度" now={now} />
      </div>)}
      <p className="usage-note">电脑端 Codex 账户 · {new Date(state.data.fetchedAt * 1000).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })} 更新</p>
    </> : <p>正在读取账户额度…</p>}
  </section>
}

function WindowRow({ window, fallback, now }: { window: UsageWindow | null; fallback: string; now: number }) {
  const expired = window?.resetsAt != null && window.resetsAt * 1000 <= now
  const remaining = remainingPercent(window?.usedPercent ?? null)
  const reset = window?.resetsAt ? new Date(window.resetsAt * 1000) : null
  return <div className="usage-window">
    <span>{windowLabel(window?.windowDurationMins ?? null, fallback)}</span>
    <strong>{expired ? '待刷新' : remaining === null ? '暂无数据' : `${remaining}%`}</strong>
    <time dateTime={reset?.toISOString()} title={reset ? `重置时间：${reset.toLocaleString('zh-CN')}` : undefined}>{reset ? `${reset.toLocaleString('zh-CN', { month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit' })} 重置` : '重置时间未知'}</time>
    {remaining !== null && !expired && <progress aria-label={`${windowLabel(window?.windowDurationMins ?? null, fallback)}剩余`} max={100} value={remaining} />}
  </div>
}
