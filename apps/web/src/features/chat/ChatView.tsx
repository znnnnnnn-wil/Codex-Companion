import { useEffect, useRef, useState, type ChangeEvent, type FormEvent, type KeyboardEvent } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import type { MediaState, PendingMessage, ThreadItem, ThreadSummary } from '../../protocol/types'
import { MAX_ATTACHMENT_FILES, validateAttachments } from './attachments'

interface Props {
  thread?: ThreadSummary
  draftCwd?: string
  items: ThreadItem[]
  media: Record<string, MediaState>
  pending: PendingMessage[]
  online: boolean
  codexState: string
  onSend: (text: string, files: File[]) => Promise<boolean>
  onStop: () => void
  stopping: boolean
}

interface SelectedAttachment {
  file: File
  preview?: string
}

export function ChatView({ thread, draftCwd, items, media, pending, online, codexState, onSend, onStop, stopping }: Props) {
  const [text, setText] = useState('')
  const [attachments, setAttachments] = useState<SelectedAttachment[]>([])
  const [attachmentError, setAttachmentError] = useState<string>()
  const [sending, setSending] = useState(false)
  const [showEvents, setShowEvents] = useState(false)
  const endRef = useRef<HTMLDivElement>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const attachmentsRef = useRef<SelectedAttachment[]>([])

  useEffect(() => endRef.current?.scrollIntoView({ behavior: 'smooth' }), [items, pending, codexState])

  useEffect(() => { attachmentsRef.current = attachments }, [attachments])
  useEffect(() => () => attachmentsRef.current.forEach((attachment) => {
    if (attachment.preview) URL.revokeObjectURL(attachment.preview)
  }), [])

  async function submit(event?: FormEvent) {
    event?.preventDefault()
    const value = text.trim()
    if ((!value && attachments.length === 0) || (!thread && !draftCwd) || !online || sending || codexState === 'working') return
    setSending(true)
    const sent = await onSend(value, attachments.map((attachment) => attachment.file))
    setSending(false)
    if (sent) {
      attachments.forEach((attachment) => {
        if (attachment.preview) URL.revokeObjectURL(attachment.preview)
      })
      setAttachments([])
      setText('')
      setAttachmentError(undefined)
      if (fileInputRef.current) fileInputRef.current.value = ''
    }
  }

  function keyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault()
      submit()
    }
  }

  function chooseFiles(event: ChangeEvent<HTMLInputElement>) {
    const incoming = Array.from(event.target.files ?? [])
    const combined = [...attachments.map((attachment) => attachment.file), ...incoming]
    const error = validateAttachments(combined)
    if (error) {
      setAttachmentError(error)
      event.target.value = ''
      return
    }
    setAttachmentError(undefined)
    setAttachments((current) => [...current, ...incoming.map((file) => ({
      file,
      preview: file.type.startsWith('image/') ? URL.createObjectURL(file) : undefined,
    }))])
    event.target.value = ''
  }

  function removeAttachment(index: number) {
    setAttachments((current) => {
      const target = current[index]
      if (target?.preview) URL.revokeObjectURL(target.preview)
      return current.filter((_, candidate) => candidate !== index)
    })
    setAttachmentError(undefined)
  }

  if (!thread && !draftCwd) {
    return <section className="chat-empty"><div><span>⌁</span><h2>选择一个 Codex 会话</h2><p>聊天记录直接读取自电脑上的真实 thread。</p></div></section>
  }

  const displayTitle = thread?.title ?? '新会话'
  const displayCwd = thread?.cwd ?? draftCwd ?? ''
  const pendingKey = thread?.threadId ?? `draft:${draftCwd}`

  const visibleItems = showEvents ? items : items.filter((item) => item.type === 'message' || item.type === 'image')

  return (
    <section className="chat-panel">
      <div className="chat-toolbar">
        <div><h2>{displayTitle}</h2><p>{displayCwd}</p></div>
        <button type="button" className="ghost-button" onClick={() => setShowEvents((value) => !value)}>
          {showEvents ? '隐藏执行事件' : '显示执行事件'}
        </button>
      </div>
      <div className="message-scroll" aria-live="polite">
        {visibleItems.map((item) => <Message key={item.id} item={item} media={media} />)}
        {pending.filter((message) => message.threadId === pendingKey).map((message) => (
          <div key={message.requestId} className="message-row user pending-message">
            <div className="message-bubble">
              {message.text && <p>{message.text}</p>}
              {message.attachments.length > 0 && <div className="pending-attachments">{message.attachments.map((name) => <span key={name}>📎 {name}</span>)}</div>}
              <small className={message.status === 'failed' ? 'failed' : ''}>
                {message.status === 'failed' ? message.error : message.status === 'confirmed' ? '正在同步…' : '发送中…'}
              </small>
            </div>
          </div>
        ))}
        {codexState === 'working' && <div className="working-indicator"><i /><i /><i /><span>Codex 正在工作，可点击输入框右侧停止</span></div>}
        <div ref={endRef} />
      </div>
      <div className="composer-area">
        {attachments.length > 0 && <div className="attachment-strip">
          {attachments.map((attachment, index) => <div className="attachment-chip" key={`${attachment.file.name}-${index}`}>
            {attachment.preview ? <img src={attachment.preview} alt="" /> : <span className="file-icon">📄</span>}
            <span title={attachment.file.name}>{attachment.file.name}</span>
            <button type="button" onClick={() => removeAttachment(index)} aria-label={`移除 ${attachment.file.name}`}>×</button>
          </div>)}
        </div>}
        {attachmentError && <div className="attachment-error" role="alert">{attachmentError}</div>}
        <form className="composer" onSubmit={submit}>
          <input ref={fileInputRef} className="file-input" type="file" multiple onChange={chooseFiles} />
          <button
            type="button"
            className="attach-button"
            aria-label="上传图片或文件"
            title={`上传图片或文件（最多 ${MAX_ATTACHMENT_FILES} 个）`}
            disabled={!online || codexState === 'working' || sending}
            onClick={() => fileInputRef.current?.click()}
          >＋</button>
          <textarea
            value={text}
            onChange={(event) => setText(event.target.value)}
            onKeyDown={keyDown}
            rows={1}
            placeholder={!online ? '电脑离线，暂时无法发送' : codexState === 'working' ? 'Codex 正在工作…' : '输入消息…'}
            disabled={!online || codexState === 'working' || sending}
            aria-label="发送给 Codex"
          />
          {codexState === 'working'
            ? <button type="button" className="stop-button" aria-label="中止 Codex" onClick={onStop} disabled={!online || stopping}><span /></button>
            : <button type="submit" aria-label="发送" disabled={!online || sending || (!text.trim() && attachments.length === 0)}>↑</button>}
        </form>
      </div>
    </section>
  )
}

function Message({ item, media }: { item: ThreadItem; media: Record<string, MediaState> }) {
  if (item.type === 'unsupported') {
    return <div className="event-row"><span>{eventLabel(item.rawType)}</span>{item.status && <small>{item.status}</small>}</div>
  }
  if (item.type === 'image') {
    return (
      <div className="message-row assistant">
        <div className="message-author">Codex</div>
        <div className="message-bubble generated-image-card">
          {media[item.id]?.status === 'loaded' && media[item.id].dataUrl
            ? <a href={media[item.id].dataUrl} target="_blank" rel="noreferrer"><img src={media[item.id].dataUrl} alt={item.content ?? 'Codex 生成的图片'} /></a>
            : media[item.id]?.status === 'failed'
              ? <div className="generated-image-placeholder failed">图片暂时无法加载</div>
              : <div className="generated-image-placeholder">正在加载生成的图片…</div>}
          <small>Codex 生成的图片</small>
        </div>
      </div>
    )
  }
  return (
    <div className={`message-row ${item.role}`}>
      <div className="message-author">{item.role === 'user' ? '你' : 'Codex'}</div>
      <div className={`message-bubble ${item.role === 'user' ? 'user-message-body' : 'markdown-body'}`}>
        {item.role === 'user'
          ? <>{item.content && <p>{unescapeDesktopMarkdown(item.content)}</p>}<HistoricalAttachments attachments={item.attachments ?? []} media={media} /></>
          : <ReactMarkdown remarkPlugins={[remarkGfm]}>{item.content ?? ''}</ReactMarkdown>}
      </div>
    </div>
  )
}

function HistoricalAttachments({ attachments, media }: { attachments: NonNullable<ThreadItem['attachments']>; media: Record<string, MediaState> }) {
  if (attachments.length === 0) return null
  return <div className="history-attachments">
    {attachments.map((attachment) => {
      const state = media[attachment.id]
      if (attachment.mimeType.startsWith('image/')) {
        return <div className="history-attachment image" key={attachment.id}>
          {state?.status === 'loaded' && state.dataUrl
            ? <a href={state.dataUrl} target="_blank" rel="noreferrer"><img src={state.dataUrl} alt={attachment.name} /></a>
            : <div className={`attachment-placeholder ${!attachment.available || state?.status === 'failed' ? 'failed' : ''}`}>
                {!attachment.available || state?.status === 'failed' ? '原图片已不在电脑上' : '正在加载图片…'}
              </div>}
          <span title={attachment.name}>📎 {attachment.name}</span>
        </div>
      }
      return <div className="history-attachment file" key={attachment.id}><span title={attachment.name}>📎 {attachment.name}</span></div>
    })}
  </div>
}

function unescapeDesktopMarkdown(value: string): string {
  return value.replace(/\\([_#*`])/g, '$1')
}

function eventLabel(rawType: string): string {
  const labels: Record<string, string> = {
    reasoning: '思考过程', commandExecution: '执行命令', fileChange: '修改文件', webSearch: '搜索网页',
  }
  return labels[rawType] ?? `不支持的事件：${rawType}`
}
