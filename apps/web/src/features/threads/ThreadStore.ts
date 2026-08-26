import type {
  ConnectionState,
  Envelope,
  PendingMessage,
  ThreadHistory,
  ThreadItem,
  ThreadSummary,
  MediaContent,
  MediaState,
} from '../../protocol/types'

export interface CompanionState {
  connection: ConnectionState
  pcOnline: boolean
  codexState: 'offline' | 'idle' | 'working'
  threads: ThreadSummary[]
  activeThreadId?: string
  items: ThreadItem[]
  media: Record<string, MediaState>
  pending: PendingMessage[]
  error?: string
}

const initialState: CompanionState = {
  connection: 'disconnected',
  pcOnline: false,
  codexState: 'offline',
  threads: [],
  items: [],
  media: {},
  pending: [],
}

export class ThreadStore {
  private state: CompanionState = initialState
  private readonly listeners = new Set<() => void>()

  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }

  getSnapshot = (): CompanionState => this.state

  setConnection(connection: ConnectionState): void {
    this.update({ connection, ...(connection === 'disconnected' ? { pcOnline: false } : {}) })
  }

  selectThread(threadId: string): void {
    this.update({ activeThreadId: threadId, items: [], media: {}, error: undefined })
  }

  activateThread(thread: ThreadSummary): void {
    const threads = [thread, ...this.state.threads.filter((candidate) => candidate.threadId !== thread.threadId)]
    this.update({ threads, activeThreadId: thread.threadId, items: [], media: {}, error: undefined })
  }

  selectDraft(): void {
    this.update({ activeThreadId: undefined, items: [], media: {}, error: undefined })
  }

  clearSelection(): void {
    this.update({ activeThreadId: undefined, items: [], media: {}, error: undefined })
  }

  setMediaLoading(itemId: string): void {
    this.update({ media: { ...this.state.media, [itemId]: { status: 'loading' } } })
  }

  setMediaFailed(itemId: string): void {
    this.update({ media: { ...this.state.media, [itemId]: { status: 'failed' } } })
  }

  addPending(requestId: string, threadId: string, text: string, attachments: string[] = []): void {
    this.update({
      pending: [...this.state.pending, { requestId, threadId, text, attachments, status: 'pending' }],
      error: undefined,
    })
  }

  clearError(): void {
    this.update({ error: undefined })
  }

  apply(envelope: Envelope): void {
    switch (envelope.type) {
      case 'device.online':
        this.update({ pcOnline: true })
        break
      case 'device.offline':
        this.update({ pcOnline: false, codexState: 'offline' })
        break
      case 'codex.status': {
        const payload = envelope.payload as { state?: string; codexRunning?: boolean }
        const state = !payload.codexRunning ? 'offline' : payload.state === 'working' ? 'working' : 'idle'
        this.update({ codexState: state })
        break
      }
      case 'thread.list.response': {
        const payload = envelope.payload as { threads?: ThreadSummary[] }
        this.update({ threads: payload.threads ?? [], error: undefined })
        break
      }
      case 'thread.create.response': {
        const payload = envelope.payload as { thread?: ThreadSummary }
        if (payload.thread) {
          const threads = [payload.thread, ...this.state.threads.filter(
            (thread) => thread.threadId !== payload.thread!.threadId,
          )]
          this.update({ threads, error: undefined })
        }
        break
      }
      case 'thread.create.failed': {
        const payload = envelope.payload as { code?: string; message?: string }
        this.update({ error: friendlyError(payload.code, payload.message) })
        break
      }
      case 'thread.read.response':
        this.reconcileHistory(envelope.payload as ThreadHistory)
        break
      case 'thread.updated': {
        const payload = envelope.payload as { items?: ThreadItem[]; status?: string }
        if (envelope.threadId === this.state.activeThreadId) {
          this.update({ items: mergeItems(this.state.items, payload.items ?? []) })
        }
        break
      }
      case 'media.read.response': {
        const payload = envelope.payload as MediaContent
        if (envelope.threadId === this.state.activeThreadId
          && payload?.itemId
          && /^image\/(png|jpeg|gif|webp)$/.test(payload.mimeType)
          && payload.dataBase64) {
          this.update({
            media: {
              ...this.state.media,
              [payload.itemId]: {
                status: 'loaded',
                dataUrl: `data:${payload.mimeType};base64,${payload.dataBase64}`,
              },
            },
          })
        }
        break
      }
      case 'message.accepted':
        this.setPendingStatus(envelope.requestId, 'accepted')
        break
      case 'message.confirmed':
        // Remain optimistic until a subsequent thread.read proves the message exists.
        this.setPendingStatus(envelope.requestId, 'confirmed', undefined, envelope.threadId)
        break
      case 'codex.stop.response':
        this.update({ codexState: 'idle', error: undefined })
        break
      case 'codex.stop.failed': {
        const payload = envelope.payload as { code?: string; message?: string }
        this.update({ error: friendlyError(payload.code, payload.message) })
        break
      }
      case 'message.failed': {
        const payload = envelope.payload as { message?: string }
        this.setPendingStatus(envelope.requestId, 'failed', payload.message ?? '发送失败')
        break
      }
      case 'error': {
        const payload = envelope.payload as { code?: string; message?: string }
        this.update({ error: friendlyError(payload.code, payload.message) })
        break
      }
    }
  }

  private reconcileHistory(history: ThreadHistory): void {
    if (!history || history.threadId !== this.state.activeThreadId) return
    const pending = this.state.pending.filter((message) => {
      if (message.threadId !== history.threadId || message.status === 'failed') return true
      return !history.items.some((item) =>
        item.type === 'message'
        && item.role === 'user'
        && normalize(item.content) === normalize(message.text))
    })
    this.update({ items: history.items ?? [], pending, error: undefined })
  }

  private setPendingStatus(
    requestId: string | undefined,
    status: PendingMessage['status'],
    error?: string,
    confirmedThreadId?: string,
  ): void {
    if (!requestId) return
    this.update({
      pending: this.state.pending.map((message) =>
        message.requestId === requestId
          ? { ...message, status, error, ...(confirmedThreadId ? { threadId: confirmedThreadId } : {}) }
          : message),
      ...(error ? { error } : {}),
    })
  }

  private update(patch: Partial<CompanionState>): void {
    this.state = { ...this.state, ...patch }
    for (const listener of this.listeners) listener()
  }
}

function mergeItems(current: ThreadItem[], changed: ThreadItem[]): ThreadItem[] {
  if (changed.length === 0) return current
  const replacements = new Map(changed.map((item) => [item.id, item]))
  const merged = current.map((item) => replacements.get(item.id) ?? item)
  const known = new Set(current.map((item) => item.id))
  for (const item of changed) if (!known.has(item.id)) merged.push(item)
  return merged
}

function normalize(value: string | undefined): string {
  return (value ?? '').replace(/[\r\n]+$/, '')
}

function friendlyError(code?: string, fallback?: string): string {
  const messages: Record<string, string> = {
    DEVICE_OFFLINE: '电脑 Bridge 当前离线。',
    CODEX_NOT_RUNNING: '电脑上的 Codex 当前未运行。',
    CODEX_APP_SERVER_UNAVAILABLE: '暂时无法读取 Codex 会话。',
    THREAD_CREATE_FAILED: '未能新建 Codex 会话，请稍后重试。',
    THREAD_NOT_FOUND: '找不到该 Codex 会话。',
    AMBIGUOUS_THREAD: '无法唯一定位该会话，请在电脑端先打开一次。',
    MEDIA_NOT_FOUND: '该 Codex 生成图片暂时无法读取。',
    CODEX_INPUT_NOT_FOUND: '找不到 Codex 输入框。',
    CODEX_ATTACHMENT_FAILED: '附件未能添加到 Codex Desktop。',
    ATTACHMENT_TOO_LARGE: '附件超过上传大小限制。',
    CODEX_SEND_FAILED: '消息未能发送到 Codex Desktop。',
    CODEX_NOT_WORKING: '该 Codex 会话当前没有正在执行的任务。',
    CODEX_STOP_FAILED: '未能中止 Codex 当前任务。',
    THREAD_CONFIRM_TIMEOUT: '消息未能从真实 Codex thread 中确认。',
    UNAUTHORIZED: '设备凭据无效，请重新配对。',
  }
  return (code && messages[code]) || fallback || '发生未知错误。'
}
