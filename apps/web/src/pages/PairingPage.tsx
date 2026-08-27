import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { claimPairing } from '../api/CompanionSocket'
import { saveCredentialAsync } from '../api/credential'
import type { DeviceCredential } from '../protocol/types'

export function PairingPage({ onPaired }: { onPaired: (credential: DeviceCredential) => void }) {
  const [autoPairCode] = useState(() => readPairingCodeFromUrl())
  const [code, setCode] = useState(autoPairCode)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const autoPairAttempted = useRef(false)

  const submitCode = useCallback(async (rawCode: string) => {
    const normalizedCode = rawCode.trim().toUpperCase().replace(/[^A-Z2-9]/g, '').slice(0, 8)
    if (normalizedCode.length !== 8 || busy) return
    setBusy(true)
    setError('')
    try {
      const credential = await claimPairing(normalizedCode)
      await saveCredentialAsync(credential)
      onPaired(credential)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '配对失败')
    } finally {
      setBusy(false)
    }
  }, [busy, onPaired])

  useEffect(() => {
    if (autoPairCode.length !== 8 || autoPairAttempted.current) return
    autoPairAttempted.current = true
    // Keep the code out of browser history after the QR link has been consumed.
    window.history.replaceState({}, document.title, window.location.pathname)
    void submitCode(autoPairCode)
  }, [autoPairCode, submitCode])

  function submit(event: FormEvent) {
    event.preventDefault()
    void submitCode(code)
  }

  return (
    <main className="pairing-page">
      <section className="pairing-card">
        <img src="/companion.svg" alt="" className="pairing-logo" />
        <p className="eyebrow">CODEX COMPANION</p>
        <h1>连接你的电脑</h1>
        <p className="muted">先在 Windows Bridge 中运行 <code>dotnet run -- run</code>，然后输入屏幕上的 8 位配对码，或直接扫描 Bridge 显示的二维码。</p>
        <form onSubmit={submit}>
          <label htmlFor="pairing-code">配对码</label>
          <input
            id="pairing-code"
            value={code}
            onChange={(event) => setCode(event.target.value.toUpperCase().replace(/[^A-Z2-9]/g, '').slice(0, 8))}
            autoComplete="one-time-code"
            inputMode="text"
            placeholder="ABCDEFGH"
            maxLength={8}
          />
          <button type="submit" className="primary-button" disabled={busy || code.length !== 8}>
            {busy ? '正在连接…' : '连接电脑'}
          </button>
        </form>
        {error && <p className="form-error" role="alert">{error}</p>}
        <p className="security-note">凭据仅保存在此浏览器中；Relay 只保存不可逆哈希。</p>
      </section>
    </main>
  )
}

function readPairingCodeFromUrl() {
  if (typeof window === 'undefined') return ''
  const pairCode = new URLSearchParams(window.location.search).get('pair') ?? ''
  return pairCode.trim().toUpperCase().replace(/[^A-Z2-9]/g, '').slice(0, 8)
}
