import { describe, expect, it } from 'vitest'
import type { ThreadSummary } from '../protocol/types'
import { groupThreadsByWorkspace } from './threadGrouping'

const thread = (threadId: string, cwd: string, updatedAt: number): ThreadSummary => ({
  threadId,
  title: threadId,
  cwd,
  updatedAt,
  status: 'idle',
  source: 'appServer',
})

describe('groupThreadsByWorkspace', () => {
  it('groups Windows paths case-insensitively and sorts projects and tasks by recency', () => {
    const groups = groupThreadsByWorkspace([
      thread('old-sun', 'E:\\project\\sun', 10),
      thread('codex', 'E:\\codexDestop', 30),
      thread('new-sun', 'e:\\PROJECT\\sun\\', 40),
    ])

    expect(groups.map((group) => group.name)).toEqual(['sun', 'codexDestop'])
    expect(groups[0].threads.map((item) => item.threadId)).toEqual(['new-sun', 'old-sun'])
  })

  it('places threads without cwd in an other group', () => {
    const [group] = groupThreadsByWorkspace([thread('projectless', '', 1)])

    expect(group.name).toBe('其他会话')
  })
})
