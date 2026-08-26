import type { ThreadSummary } from '../protocol/types'

export interface ThreadGroup {
  key: string
  name: string
  cwd: string
  updatedAt: number
  threads: ThreadSummary[]
}

export function groupThreadsByWorkspace(threads: ThreadSummary[]): ThreadGroup[] {
  const groups = new Map<string, ThreadGroup>()
  for (const thread of threads) {
    const key = normalizeWorkspace(thread.cwd)
    const current = groups.get(key)
    if (current) {
      current.threads.push(thread)
      current.updatedAt = Math.max(current.updatedAt, thread.updatedAt)
    } else {
      groups.set(key, {
        key,
        name: workspaceName(thread.cwd),
        cwd: thread.cwd,
        updatedAt: thread.updatedAt,
        threads: [thread],
      })
    }
  }

  return Array.from(groups.values())
    .map((group) => ({
      ...group,
      threads: group.threads.toSorted((left, right) => right.updatedAt - left.updatedAt),
    }))
    .toSorted((left, right) => right.updatedAt - left.updatedAt || left.name.localeCompare(right.name, 'zh-CN'))
}

function normalizeWorkspace(cwd: string): string {
  const normalized = cwd.trim().replace(/[\\/]+$/, '').replace(/\\/g, '/')
  return normalized ? normalized.toLocaleLowerCase('en-US') : '__other__'
}

function workspaceName(cwd: string): string {
  return cwd.trim().split(/[\\/]/).filter(Boolean).at(-1) ?? '其他会话'
}
