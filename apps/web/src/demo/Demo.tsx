import { useCallback, useEffect, useState } from 'react'

const COMMAND = 'Run the tests and fix any remaining failures.'
const LOOP_DURATION = 14_500
const SCENES = [0, 1_200, 2_500, 3_300, 5_500, 6_200, 7_000, 7_800, 8_700, 9_600, 10_300, 11_000, 11_700, 12_300]

function initialStaticState(): boolean {
  return new URLSearchParams(window.location.search).has('still')
    || window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

export function Demo() {
  const [staticMode, setStaticMode] = useState(initialStaticState)
  const [scene, setScene] = useState(staticMode ? SCENES.length - 1 : 0)
  const [typedCommand, setTypedCommand] = useState(staticMode ? COMMAND : '')
  const [runKey, setRunKey] = useState(0)

  useEffect(() => {
    const query = new URLSearchParams(window.location.search)
    document.documentElement.dataset.capture = query.get('still') ?? ''
  }, [])

  useEffect(() => {
    if (staticMode) return

    let timers: number[] = []
    let loopTimer = 0

    const play = () => {
      timers.forEach(window.clearTimeout)
      window.clearTimeout(loopTimer)
      timers = []
      setScene(0)
      setTypedCommand('')
      for (let index = 1; index < SCENES.length; index += 1) {
        timers.push(window.setTimeout(() => setScene(index), SCENES[index]))
      }
      for (let index = 1; index <= COMMAND.length; index += 1) {
        timers.push(window.setTimeout(
          () => setTypedCommand(COMMAND.slice(0, index)),
          SCENES[3] + (index * 42),
        ))
      }
      loopTimer = window.setTimeout(play, LOOP_DURATION)
    }

    const handleVisibility = () => {
      if (document.hidden) {
        timers.forEach(window.clearTimeout)
        window.clearTimeout(loopTimer)
      } else {
        play()
      }
    }

    play()
    document.addEventListener('visibilitychange', handleVisibility)
    return () => {
      timers.forEach(window.clearTimeout)
      window.clearTimeout(loopTimer)
      document.removeEventListener('visibilitychange', handleVisibility)
    }
  }, [runKey, staticMode])

  const replay = useCallback(() => {
    setStaticMode(false)
    setRunKey((value) => value + 1)
  }, [])

  return (
    <main className={`demo ${scene >= 12 ? 'is-complete' : ''}`}>
      <header className="demo-header">
        <a className="wordmark" href="../" aria-label="Codex Companion home">
          <CompanionMark />
          <span>Codex Companion</span>
        </a>
        <div className="header-meta">
          <span className="demo-label"><i /> Interactive demo</span>
          <button className="replay" type="button" onClick={replay}>
            <ReplayIcon />
            Replay
          </button>
        </div>
      </header>

      <section className="intro" aria-labelledby="demo-title">
        <p className="capture-brand">Codex Companion</p>
        <p className="kicker">WINDOWS CODEX DESKTOP · PHONE BROWSER</p>
        <h1 id="demo-title">Remote Codex, <span>not your computer.</span></h1>
        <p>Continue your real Windows Codex Desktop threads<br className="wide-break" /> from any phone browser.</p>
        <p className="og-subtitle">Self-hosted mobile access<br />to Windows Codex Desktop</p>
        <div className="trust-tags" aria-label="Product characteristics">
          <span>Self-hosted</span><i />
          <span>Outbound-only</span><i />
          <span>No phone VPN</span><i />
          <span>Minimal permissions</span>
        </div>
      </section>

      <section className="product-stage" aria-label="Codex Companion product demonstration">
        <Desktop scene={scene} />
        <Connection scene={scene} />
        <Phone scene={scene} typedCommand={typedCommand} />
      </section>

      <footer className="demo-footer">
        <span className="step-copy">{sceneLabel(scene)}</span>
        <span className="truth-source"><i /> Codex thread is the source of truth</span>
      </footer>
    </main>
  )
}

function Desktop({ scene }: { scene: number }) {
  return (
    <article className="desktop-device" aria-label="Windows Codex Desktop">
      <div className="window-chrome">
        <div className="window-dots"><i /><i /><i /></div>
        <span>Codex Desktop</span>
        <span className="window-platform">Windows</span>
      </div>
      <div className="desktop-body">
        <aside className="desktop-sidebar">
          <div className="desktop-app-title"><CompanionGlyph /><span>Codex</span></div>
          <p>PROJECTS</p>
          <div className="project-row"><FolderIcon /><span>TimeFlow</span><small>1</small></div>
          <button className="desktop-thread selected" type="button">
            <span>Fix WebSocket reconnect issue</span>
            <time>now</time>
          </button>
          <div className="sidebar-fade-row" />
          <div className="sidebar-fade-row short" />
        </aside>
        <section className="desktop-thread-view">
          <div className="thread-heading">
            <div><small>TIMEFLOW</small><h2>Fix WebSocket reconnect issue</h2></div>
            <span className="real-thread"><i /> REAL THREAD</span>
          </div>
          <div className="desktop-messages">
            <Message role="User">Fix the reconnect issue in the WebSocket client.</Message>
            <Message role="Codex">I updated the reconnect state handling and added tests.</Message>
            {scene >= 4 && <Message role="User" pending={scene === 4}>{COMMAND}</Message>}
            {scene >= 5 && <ExecutionLog scene={scene} />}
          </div>
          <div className="desktop-composer">
            <span>{scene >= 5 && scene < 9 ? 'Codex is working…' : 'Ask Codex to work on this project'}</span>
            <button type="button" aria-label="Send" disabled>↑</button>
          </div>
        </section>
      </div>
    </article>
  )
}

function Message({ role, children, pending = false }: { role: string; children: string; pending?: boolean }) {
  return (
    <div className={`desktop-message ${role === 'User' ? 'from-user' : ''} ${pending ? 'pending' : ''}`}>
      <small>{role}</small>
      <p>{children}</p>
      {pending && <span className="syncing">syncing</span>}
    </div>
  )
}

function ExecutionLog({ scene }: { scene: number }) {
  return (
    <div className="execution-card">
      <div className="execution-title">
        <span className={scene < 9 ? 'spinner' : 'check'}>{scene < 9 ? '' : '✓'}</span>
        <strong>{scene < 9 ? 'Working on TimeFlow' : 'Completed'}</strong>
      </div>
      <ol>
        <li className={scene >= 5 ? 'visible' : ''}><i className="neutral" />Running tests...</li>
        <li className={scene >= 6 ? 'visible' : ''}><i className="failure" />2 tests failed.</li>
        <li className={scene >= 7 ? 'visible' : ''}><i className="neutral" />Investigating...</li>
        <li className={scene >= 8 ? 'visible' : ''}><i className="success" />Fixed reconnect cleanup race condition.</li>
        <li className={scene >= 9 ? 'visible' : ''}><i className="success" />All tests passed.</li>
      </ol>
    </div>
  )
}

function Connection({ scene }: { scene: number }) {
  const outbound = scene === 4
  const inbound = scene >= 9 && scene <= 11
  return (
    <div className={`connection ${outbound ? 'outbound' : ''} ${inbound ? 'inbound' : ''}`} aria-label="Outbound WebSocket connection through self-hosted Relay">
      <span className="endpoint-label desktop-label">Windows Bridge</span>
      <div className="connection-line">
        <span className="packet" />
        <i className="line-dot start" />
        <i className="line-dot end" />
      </div>
      <div className="relay-node">
        <ServerIcon />
        <strong>Relay</strong>
        <small>self-hosted</small>
      </div>
      <span className="endpoint-label phone-label">Phone browser</span>
      <span className="wss-label">Outbound WSS</span>
    </div>
  )
}

function Phone({ scene, typedCommand }: { scene: number; typedCommand: string }) {
  const openThread = scene >= 2
  const resultVisible = scene >= 10
  return (
    <article className={`phone-device ${scene >= 1 ? 'connected' : ''}`} aria-label="Phone browser">
      <div className="phone-speaker" />
      <div className="phone-screen">
        <div className="phone-browser-bar">
          <span className="lock-icon">⌁</span>
          <span>companion.example.com</span>
          <i />
        </div>
        <div className="phone-app-bar">
          <CompanionMark />
          <span>Companion</span>
          <i className="online-dot" />
        </div>
        {scene < 1
          ? <div className="phone-waiting"><span /><p>Phone browser</p><small>Ready to connect</small></div>
          : !openThread
            ? <ThreadPicker />
            : <PhoneThread scene={scene} typedCommand={typedCommand} resultVisible={resultVisible} />}
      </div>
    </article>
  )
}

function ThreadPicker() {
  return (
    <div className="thread-picker">
      <p className="connected-label">Connected to</p>
      <h3><i /> DESKTOP-DEV</h3>
      <p className="phone-section-label">THREADS</p>
      <button className="phone-thread-card auto-click" type="button">
        <span className="phone-project"><FolderIcon /> TimeFlow</span>
        <strong>Fix WebSocket reconnect issue</strong>
        <small>Continue real Codex thread <b>›</b></small>
        <span className="tap-ring" />
      </button>
    </div>
  )
}

function PhoneThread({ scene, typedCommand, resultVisible }: { scene: number; typedCommand: string; resultVisible: boolean }) {
  return (
    <div className="phone-thread-view">
      <div className="phone-thread-heading">
        <button type="button" aria-label="Back">‹</button>
        <div><small>TIMEFLOW</small><strong>Fix WebSocket reconnect issue</strong></div>
      </div>
      <div className="phone-messages">
        <div className="phone-bubble user">Fix the reconnect issue in the WebSocket client.</div>
        <div className="phone-bubble codex">I updated the reconnect state handling and added tests.</div>
        {scene >= 4 && <div className="phone-bubble user new-command">{COMMAND}<small>{scene < 10 ? 'Sent to Windows' : 'Delivered'}</small></div>}
        {resultVisible && (
          <div className="phone-result">
            <span className="phone-author">Codex</span>
            {scene >= 10 && <p>Fixed the reconnect cleanup race condition.</p>}
            {scene >= 11 && <p>All tests are now passing.</p>}
            {scene >= 12 && <strong><i>✓</i> 42 tests passed</strong>}
          </div>
        )}
      </div>
      {scene >= 12 && <div className="same-thread"><LinkIcon /> Continued on Windows Codex</div>}
      <div className={`phone-composer ${scene === 3 ? 'typing' : ''}`}>
        <span>{scene >= 3 && scene < 4 ? typedCommand : 'Message Codex…'}{scene === 3 && <i className="caret" />}</span>
        <button className={scene === 4 ? 'sending' : ''} type="button" aria-label="Send">↑</button>
      </div>
    </div>
  )
}

function sceneLabel(scene: number): string {
  if (scene < 1) return 'A real thread already exists on Windows Codex'
  if (scene < 2) return 'Phone connects to your Windows Bridge'
  if (scene < 4) return 'Continue the same thread from your phone'
  if (scene < 5) return 'Request travels through your self-hosted Relay'
  if (scene < 9) return 'Windows Codex runs the work locally'
  if (scene < 12) return 'The real thread streams back to your phone'
  return 'Same Codex thread. Two convenient screens.'
}

function CompanionMark() {
  return <svg className="companion-mark" viewBox="0 0 32 32" aria-hidden="true"><rect x="2" y="2" width="28" height="28" rx="9" /><path d="M10 11.5h8.8a4.2 4.2 0 0 1 0 8.4h-3.2l-3.5 2.8v-2.8H10a4.2 4.2 0 0 1 0-8.4Z" /></svg>
}

function CompanionGlyph() {
  return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6.6 7.2h7.3a3.5 3.5 0 0 1 0 7H11l-3 2.4v-2.4H6.6a3.5 3.5 0 0 1 0-7Z" /></svg>
}

function FolderIcon() {
  return <svg viewBox="0 0 20 20" aria-hidden="true"><path d="M2.5 5.2h5l1.7 1.7h8.3v7.9H2.5Z" /></svg>
}

function ServerIcon() {
  return <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="4" y="4" width="16" height="6" rx="2" /><rect x="4" y="14" width="16" height="6" rx="2" /><path d="M8 7h.01M8 17h.01M12 7h5M12 17h5" /></svg>
}

function LinkIcon() {
  return <svg viewBox="0 0 20 20" aria-hidden="true"><path d="M8.2 12.4 7 13.6a3 3 0 0 1-4.2-4.2l2.3-2.3a3 3 0 0 1 4.2 0M11.8 7.6 13 6.4a3 3 0 0 1 4.2 4.2l-2.3 2.3a3 3 0 0 1-4.2 0M7.2 10h5.6" /></svg>
}

function ReplayIcon() {
  return <svg viewBox="0 0 20 20" aria-hidden="true"><path d="M15.7 7A6.2 6.2 0 1 0 16 12M15.7 7V3.5M15.7 7h-3.5" /></svg>
}
