// @vitest-environment jsdom

import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ThreadList } from './ThreadList'

afterEach(cleanup)

describe('ThreadList project actions', () => {
  it('creates a conversation in the selected project without toggling the group', () => {
    const onCreate = vi.fn()
    render(<ThreadList
      threads={[{
        threadId: 'thread', title: 'Existing', cwd: 'C:\\repo', updatedAt: 1, status: 'idle', source: 'appServer',
      }]}
      onSelect={() => {}}
      onCreate={onCreate}
    />)

    fireEvent.click(screen.getByRole('button', { name: '在 repo 中新建会话' }))

    expect(onCreate).toHaveBeenCalledWith('C:\\repo')
    expect(screen.getByRole('button', { name: 'repo' }).getAttribute('aria-expanded')).toBe('false')
  })

  it('disables every project create action while one conversation is being created', () => {
    render(<ThreadList
      threads={[{
        threadId: 'thread', title: 'Existing', cwd: 'C:\\repo', updatedAt: 1, status: 'idle', source: 'appServer',
      }]}
      onSelect={() => {}}
      onCreate={() => {}}
      creatingCwd="C:\\repo"
    />)

    expect((screen.getByRole('button', { name: '在 repo 中新建会话' }) as HTMLButtonElement).disabled).toBe(true)
  })
})
