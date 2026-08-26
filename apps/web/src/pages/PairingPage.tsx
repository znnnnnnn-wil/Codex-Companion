import { useState, type FormEvent } from 'react'
import { claimPairing } from '../api/CompanionSocket'
import { saveCredentialAsync } from '../api/credential'
import type { DeviceCredential } from '../protocol/types'

export function PairingPage({ onPaired }: { onPaired: (credential: DeviceCredential) => void }) {
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!code.trim()) return
    setBusy(true)
    setError('')
    try {
      const credential = await claimPairing(code)
      await saveCredentialAsync(credential)
      onPaired(credential)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '配对失败')
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="pairing-page">
      <section className="pairing-card">
        <img src="/companion.svg" alt="" className="pairing-logo" />
        <p className="eyebrow">CODEX COMPANION</p>
        <h1>连接你的电脑</h1>
        <p className="muted">先在 Windows Bridge 中运行 <code>dotnet run -- run</code>，然后输入屏幕上的 8 位配对码。</p>
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
