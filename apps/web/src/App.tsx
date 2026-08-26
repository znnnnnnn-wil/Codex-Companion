import { useEffect, useRef, useState, useSyncExternalStore } from 'react'
import { App as CapacitorApp } from '@capacitor/app'
import { Capacitor } from '@capacitor/core'
import { StatusBar, Style } from '@capacitor/status-bar'
import { CompanionSocket, relayWebSocketUrl } from './api/CompanionSocket'
import { clearCredentialAsync, loadCredentialAsync } from './api/credential'
import { ThreadList } from './components/ThreadList'
import { ChatView } from './features/chat/ChatView'
import { encodeAttachments } from './features/chat/attachments'
import { ThreadStore } from './features/threads/ThreadStore'
import { PairingPage } from './pages/PairingPage'
import type { DeviceCredential, Envelope, ThreadItem, ThreadSummary } from './protocol/types'
import './App.css'

export default function App() {
  const [credential, setCredential] = useState<DeviceCredential | null>(null)
  const [credentialReady, setCredentialReady] = useState(false)

  useEffect(() => {
    let active = true
    void loadCredentialAsync().then((value) => {
      if (active) {
        setCredential(value)
        setCredentialReady(true)
      }
    })
    return () => { active = false }
  }, [])

  useEffect(() => {
    if (!Capacitor.isNativePlatform()) return
    void StatusBar.setStyle({ style: Style.Light })
    void StatusBar.setBackgroundColor({ color: '#ffffff' })
    void StatusBar.setOverlaysWebView({ overlay: false })
  }, [])

  if (!credentialReady) return <main className="pairing-page" aria-busy="true" />
  return credential
    ? <Companion credential={credential} onUnpair={() => { void clearCredentialAsync(); setCredential(null) }} />
    : <PairingPage onPaired={setCredential} />
}

function Companion({ credential, onUnpair }: { credential: DeviceCredential; onUnpair: () => void }) {
  const [store] = useState(() => new ThreadStore())
  const state = useSyncExternalStore(store.subscribe, store.getSnapshot)
  const socketRef = useRef<CompanionSocket | null>(null)
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [stopping, setStopping] = useState(false)
  const [draftCwd, setDraftCwd] = useState<string>()
  const draftCwdRef = useRef<string | undefined>(undefined)
  const stopRequestId = useRef<string | null>(null)
  const createRequestId = useRef<string | null>(null)
  const [creatingCwd, setCreatingCwd] = useState<string>()
  const mediaRequests = useRef(new Map<string, string>())
  const requestedMedia = useRef(new Set<string>())

  useEffect(() => {
    if (!Capacitor.isNativePlatform()) return
    const listener = CapacitorApp.addListener('backButton', ({ canGoBack }) => {
      if (drawerOpen) {
        setDrawerOpen(false)
      } else if (state.activeThreadId || draftCwd) {
        store.clearSelection()
        setDraftCwd(undefined)
        draftCwdRef.current = undefined
        setDrawerOpen(true)
      } else if (canGoBack) {
        window.history.back()
      } else {
        void CapacitorApp.minimizeApp()
      }
    })
    return () => { void listener.then((handle) => handle.remove()) }
  }, [drawerOpen, draftCwd, state.activeThreadId, store])

  useEffect(() => {
    const socket = new CompanionSocket(relayWebSocketUrl(), credential)
    socketRef.current = socket
    const removeState = socket.onState((connection) => {
      store.setConnection(connection)
      if (connection === 'disconnected') {
        for (const itemId of mediaRequests.current.values()) store.setMediaFailed(itemId)
        mediaRequests.current.clear()
        requestedMedia.current.clear()
        createRequestId.current = null
        setCreatingCwd(undefined)
      }
      if (connection === 'connected') {
        try {
          socket.sendRequest('thread.list.request', undefined, {})
          const active = store.getSnapshot().activeThreadId
          if (active) socket.sendRequest('thread.read.request', active, {})
        } catch { /* the reconnect loop will retry */ }
      }
    })
    const removeMessage = socket.onMessage((envelope: Envelope) => {
      store.apply(envelope)
      const mediaItemId = envelope.requestId ? mediaRequests.current.get(envelope.requestId) : undefined
      if (mediaItemId && ['media.read.response', 'error'].includes(envelope.type)) {
        mediaRequests.current.delete(envelope.requestId!)
        if (envelope.type === 'error') store.setMediaFailed(mediaItemId)
      }
      if (envelope.type === 'thread.read.response' || envelope.type === 'thread.updated') {
        const payload = envelope.payload as { items?: ThreadItem[] }
        for (const item of payload.items ?? []) {
          const mediaIds = [
            ...(item.type === 'image' && item.status === 'completed' ? [item.id] : []),
            ...(item.attachments ?? [])
              .filter((attachment) => attachment.available && attachment.mimeType.startsWith('image/'))
              .map((attachment) => attachment.id),
          ]
          for (const itemId of mediaIds) {
            if (requestedMedia.current.has(itemId)) continue
            try {
              const requestId = socket.sendRequest('media.read.request', envelope.threadId, { itemId })
              requestedMedia.current.add(itemId)
              mediaRequests.current.set(requestId, itemId)
              store.setMediaLoading(itemId)
            } catch {
              store.setMediaFailed(itemId)
            }
          }
        }
      }
      if (envelope.type === 'thread.create.response') {
        const payload = envelope.payload as { draft?: { cwd?: string } }
        if (payload.draft?.cwd) {
          mediaRequests.current.clear()
          requestedMedia.current.clear()
          setDraftCwd(payload.draft.cwd)
          draftCwdRef.current = payload.draft.cwd
          store.selectDraft()
          setDrawerOpen(false)
        }
      }
      if (envelope.requestId === stopRequestId.current
        && ['codex.stop.response', 'codex.stop.failed', 'error'].includes(envelope.type)) {
        stopRequestId.current = null
        setStopping(false)
      }
      if (envelope.requestId === createRequestId.current
        && ['thread.create.response', 'thread.create.failed', 'error'].includes(envelope.type)) {
        createRequestId.current = null
        setCreatingCwd(undefined)
        if (envelope.type === 'thread.create.response') {
          const created = (envelope.payload as { thread?: ThreadSummary }).thread
          if (created) {
            mediaRequests.current.clear()
            requestedMedia.current.clear()
            store.selectThread(created.threadId)
            setDrawerOpen(false)
            setDraftCwd(undefined)
            draftCwdRef.current = undefined
          }
        }
      }
      if (envelope.type === 'message.confirmed' && envelope.threadId) {
        if (draftCwdRef.current) {
          setDraftCwd(undefined)
          draftCwdRef.current = undefined
          const confirmedThread = (envelope.payload as { thread?: ThreadSummary }).thread
          if (confirmedThread) {
            store.activateThread(confirmedThread)
          } else {
            // Older Bridges may omit the summary; resync before selecting it.
            try { socket.sendRequest('thread.list.request', undefined, {}) } catch { /* reconnect will resync */ }
          }
        }
        try { socket.sendRequest('thread.read.request', envelope.threadId, {}) } catch { /* reconnect will resync */ }
      }
    })
    socket.start()
    const resumeListener = Capacitor.isNativePlatform()
      ? CapacitorApp.addListener('resume', () => {
          socket.stop()
          socket.start()
        })
      : null
    return () => {
      if (resumeListener) void resumeListener.then((handle) => handle.remove())
      removeState()
      removeMessage()
      socket.stop()
      socketRef.current = null
    }
  }, [credential, store])

  const activeThread = state.threads.find((thread) => thread.threadId === state.activeThreadId)

  function selectThread(threadId: string) {
    setDraftCwd(undefined)
    draftCwdRef.current = undefined
    mediaRequests.current.clear()
    requestedMedia.current.clear()
    store.selectThread(threadId)
    setDrawerOpen(false)
    try {
      socketRef.current?.sendRequest('thread.read.request', threadId, {})
    } catch {
      store.apply({ type: 'error', timestamp: Date.now(), payload: { code: 'DEVICE_OFFLINE' } })
    }
  }

  async function send(text: string, files: File[]): Promise<boolean> {
    if (!activeThread && !draftCwd) return false
    try {
      const attachments = await encodeAttachments(files)
      const threadId = activeThread?.threadId
      const requestId = socketRef.current!.sendRequest('message.send', threadId, {
        text,
        attachments,
        ...(draftCwd ? { cwd: draftCwd } : {}),
      })
      store.addPending(requestId, threadId ?? `draft:${draftCwd}`, text, files.map((file) => file.name))
      return true
    } catch (error) {
      store.apply({
        type: 'error',
        timestamp: Date.now(),
        payload: { message: error instanceof Error ? error.message : '发送失败。' },
      })
      return false
    }
  }

  function stopCodex() {
    if (!activeThread || stopping) return
    try {
      const requestId = socketRef.current!.sendRequest('codex.stop', activeThread.threadId, {})
      stopRequestId.current = requestId
      setStopping(true)
    } catch {
      store.apply({ type: 'error', timestamp: Date.now(), payload: { code: 'DEVICE_OFFLINE' } })
    }
  }

  function refresh() {
    try { socketRef.current?.sendRequest('thread.list.request', undefined, {}) } catch { /* status already explains */ }
  }

  function createThread(cwd: string) {
    if (creatingCwd) return
    try {
      const requestId = socketRef.current!.sendRequest('thread.create.request', undefined, { cwd })
      createRequestId.current = requestId
      setCreatingCwd(cwd)
    } catch {
      store.apply({ type: 'error', timestamp: Date.now(), payload: { code: 'DEVICE_OFFLINE' } })
    }
  }

  return (
    <main className="app-shell">
      <header className="topbar">
        <button className="menu-button" type="button" onClick={() => setDrawerOpen(true)} aria-label="打开会话列表">☰</button>
        <div className="brand"><img src="/companion.svg" alt="" /><span>Codex Companion</span></div>
        <div className="topbar-actions">
          <span className={`presence ${state.pcOnline ? 'online' : ''}`}><i />{state.pcOnline ? 'PC 在线' : 'PC 离线'}</span>
          <button type="button" className="icon-button" onClick={refresh} aria-label="刷新会话">↻</button>
        </div>
      </header>
      <div className="workspace">
        {drawerOpen && <button type="button" className="drawer-backdrop" aria-label="关闭会话列表" onClick={() => setDrawerOpen(false)} />}
        <aside className={`sidebar ${drawerOpen ? 'open' : ''}`}>
          <div className="sidebar-header">
            <div><p className="eyebrow">真实 CODEX THREAD</p><h2>会话</h2></div>
            <button type="button" className="mobile-close" onClick={() => setDrawerOpen(false)} aria-label="关闭">×</button>
          </div>
          <ThreadList
            threads={state.threads}
            activeThreadId={state.activeThreadId}
            onSelect={selectThread}
            onCreate={createThread}
            creatingCwd={creatingCwd}
            canCreate={state.pcOnline && state.connection === 'connected'}
          />
          <div className="sidebar-footer">
            <span>{connectionLabel(state.connection)}</span>
            <button type="button" onClick={onUnpair}>解除本机浏览器绑定</button>
          </div>
        </aside>
        <ChatView
          thread={activeThread}
          draftCwd={draftCwd}
          items={state.items}
          media={state.media}
          pending={state.pending}
          online={state.pcOnline && state.connection === 'connected'}
          codexState={state.codexState}
          onSend={send}
          onStop={stopCodex}
          stopping={stopping}
        />
      </div>
      {state.error && <div className="toast" role="alert"><span>{state.error}</span><button type="button" onClick={() => store.clearError()}>×</button></div>}
    </main>
  )
}

function connectionLabel(connection: string): string {
  if (connection === 'connected') return 'Relay 已连接'
  if (connection === 'connecting') return '正在重连…'
  return 'Relay 已断开'
}
