import { useEffect, useState } from 'react'
import { Capacitor } from '@capacitor/core'

export function WebVersion() {
  const version = import.meta.env.VITE_APP_VERSION
  const [nextVersion, setNextVersion] = useState<string>()
  useEffect(() => {
    if (Capacitor.isNativePlatform()) return
    let active = true
    const controller = new AbortController()
    const check = async () => {
      if (document.hidden) return
      try {
        const response = await fetch(`${import.meta.env.BASE_URL}version.json`, { cache: 'no-store', signal: controller.signal })
        if (!response.ok) return
        const latest = await response.json() as { version?: string }
        if (active && typeof latest.version === 'string' && latest.version !== version) setNextVersion(latest.version)
      } catch { /* Offline and older deployments remain usable. */ }
    }
    void check()
    const onVisible = () => { void check() }
    const timer = setInterval(onVisible, 60_000)
    window.addEventListener('pageshow', onVisible)
    document.addEventListener('visibilitychange', onVisible)
    return () => {
      active = false
      controller.abort()
      clearInterval(timer)
      window.removeEventListener('pageshow', onVisible)
      document.removeEventListener('visibilitychange', onVisible)
    }
  }, [version])

  async function reload() {
    if ('serviceWorker' in navigator) {
      try {
        const registration = await navigator.serviceWorker.getRegistration()
        await registration?.update()
        const installing = registration?.installing
        if (installing) await new Promise<void>((resolve) => {
          const finish = () => { clearTimeout(timer); installing.removeEventListener('statechange', changed); resolve() }
          const changed = () => { if (['activated', 'redundant'].includes(installing.state)) finish() }
          const timer = setTimeout(finish, 5_000)
          installing.addEventListener('statechange', changed)
          changed()
        })
      } catch { /* HTTP deployments do not support service workers. */ }
    }
    const url = new URL(window.location.href)
    url.searchParams.set('web-version', nextVersion ?? version)
    url.searchParams.set('refresh', String(Date.now()))
    window.location.replace(url.href)
  }

  return <div className="web-version">
    <span>网页 v{version}</span>
    {!Capacitor.isNativePlatform() && <button type="button" onClick={() => { void reload() }}>{nextVersion ? '发现新版，刷新页面' : '刷新页面'}</button>}
  </div>
}
