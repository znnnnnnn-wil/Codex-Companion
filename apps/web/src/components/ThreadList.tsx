import { useMemo, useState } from 'react'
import type { ThreadSummary } from '../protocol/types'
import { groupThreadsByWorkspace } from './threadGrouping'

interface Props {
  threads: ThreadSummary[]
  activeThreadId?: string
  onSelect: (threadId: string) => void
  onCreate: (cwd: string) => void
  creatingCwd?: string
  canCreate?: boolean
}

export function ThreadList({
  threads,
  activeThreadId,
  onSelect,
  onCreate,
  creatingCwd,
  canCreate = true,
}: Props) {
  const groups = useMemo(() => groupThreadsByWorkspace(threads), [threads])
  const activeGroupKey = useMemo(
    () => groups.find((group) => group.threads.some((thread) => thread.threadId === activeThreadId))?.key,
    [activeThreadId, groups],
  )
  const [groupExpansion, setGroupExpansion] = useState<Map<string, boolean>>(() => new Map())

  function toggleGroup(key: string, expanded: boolean) {
    setGroupExpansion((current) => {
      const next = new Map(current)
      next.set(key, !expanded)
      return next
    })
  }

  return (
    <nav className="thread-list" aria-label="Codex 会话">
      {threads.length === 0 && <p className="empty-list">暂无可见的 Codex 会话</p>}
      {groups.length > 0 && <p className="thread-section-label">项目</p>}
      {groups.map((group) => {
        const expanded = groupExpansion.get(group.key) ?? group.key === activeGroupKey
        const creating = creatingCwd === group.cwd
        return (
          <section className="thread-group" key={group.key}>
            <div className={`thread-group-header ${group.key === activeGroupKey ? 'contains-active' : ''}`}>
              <button
                type="button"
                className="thread-group-toggle"
                aria-expanded={expanded}
                title={group.cwd}
                onClick={() => toggleGroup(group.key, expanded)}
              >
                <FolderIcon open={expanded} />
                <span>{group.name}</span>
                <ChevronIcon open={expanded} />
              </button>
              <button
                type="button"
                className={`thread-create-button ${creating ? 'creating' : ''}`}
                aria-label={`在 ${group.name} 中新建会话`}
                title={`在 ${group.name} 中新建会话`}
                disabled={!canCreate || Boolean(creatingCwd)}
                onClick={() => onCreate(group.cwd)}
              >
                {creating ? <span aria-hidden="true">…</span> : <NewThreadIcon />}
              </button>
            </div>
            {expanded && (
              <div className="thread-group-items">
                {group.threads.map((thread) => (
                  <button
                    type="button"
                    key={thread.threadId}
                    className={`thread-row ${thread.threadId === activeThreadId ? 'active' : ''}`}
                    title={thread.title}
                    onClick={() => onSelect(thread.threadId)}
                  >
                    <span className="thread-title">{thread.title}</span>
                    <time>{relativeTime(thread.updatedAt)}</time>
                  </button>
                ))}
              </div>
            )}
          </section>
        )
      })}
    </nav>
  )
}

function relativeTime(timestamp: number): string {
  if (!timestamp) return ''
  const seconds = Math.max(0, Math.floor(Date.now() / 1000 - timestamp))
  if (seconds < 60) return '刚刚'
  if (seconds < 3_600) return `${Math.floor(seconds / 60)} 分钟前`
  if (seconds < 86_400) return `${Math.floor(seconds / 3_600)} 小时前`
  return new Date(timestamp * 1_000).toLocaleDateString('zh-CN', { month: 'numeric', day: 'numeric' })
}

function FolderIcon({ open }: { open: boolean }) {
  return (
    <svg className="folder-icon" viewBox="0 0 20 20" aria-hidden="true">
      <path d={open
        ? 'M2.5 6.5h6l1.5 1.8h7.5l-1.4 7.2H3.4L2.5 6.5Zm.8-2h4.3l1.5 1.8h7.4v2'
        : 'M2.5 5.2c0-.8.6-1.4 1.4-1.4h3.8l1.6 1.8h6.8c.8 0 1.4.6 1.4 1.4v7.7c0 .8-.6 1.4-1.4 1.4H3.9c-.8 0-1.4-.6-1.4-1.4V5.2Z'} />
    </svg>
  )
}

function ChevronIcon({ open }: { open: boolean }) {
  return (
    <svg className={`group-chevron ${open ? 'open' : ''}`} viewBox="0 0 16 16" aria-hidden="true">
      <path d="m6 3 5 5-5 5" />
    </svg>
  )
}

function NewThreadIcon() {
  return (
    <svg className="new-thread-icon" viewBox="0 0 20 20" aria-hidden="true">
      <path d="M11.5 3.5H5.2c-.9 0-1.7.8-1.7 1.7v9.6c0 .9.8 1.7 1.7 1.7h9.6c.9 0 1.7-.8 1.7-1.7V8.5" />
      <path d="m9.2 11.1.4-2.2 5.8-5.8c.5-.5 1.3-.5 1.8 0s.5 1.3 0 1.8l-5.8 5.8-2.2.4Z" />
    </svg>
  )
}
