import { describe, expect, it } from 'vitest'
import type { Envelope, ThreadItem } from '../../protocol/types'
import { ThreadStore } from './ThreadStore'

function envelope(type: string, payload: unknown, requestId?: string, threadId?: string): Envelope {
  return { type, payload, requestId, threadId, timestamp: Date.now() }
}

describe('ThreadStore', () => {
  it('stores only Relay-provided real thread summaries', () => {
    const store = new ThreadStore()
    store.apply(envelope('thread.list.response', {
      threads: [{ threadId: 'real-id', title: 'Real', cwd: 'C:\\repo', updatedAt: 1, status: 'idle', source: 'vscode' }],
    }))

    expect(store.getSnapshot().threads).toHaveLength(1)
    expect(store.getSnapshot().threads[0].threadId).toBe('real-id')
  })

  it('adds a newly created real thread to the list', () => {
    const store = new ThreadStore()
    const created = {
      threadId: 'created', title: '新会话 10:49:00', cwd: 'C:\\repo', updatedAt: 2, status: 'notLoaded', source: 'appServer',
    }

    store.apply(envelope('thread.create.response', { thread: created }, 'create', 'created'))

    expect(store.getSnapshot().threads).toEqual([created])
  })

  it('keeps the newly materialized thread selected after the first message', () => {
    const store = new ThreadStore()
    store.selectDraft()
    store.addPending('request', 'draft:C:\\repo', '你好')
    const created = {
      threadId: 'materialized', title: '未命名会话', cwd: 'C:\\repo', updatedAt: 3, status: 'notLoaded', source: 'vscode',
    }

    store.apply(envelope('message.confirmed', { thread: created }, 'request', created.threadId))
    store.activateThread(created)

    expect(store.getSnapshot().activeThreadId).toBe('materialized')
    expect(store.getSnapshot().threads[0]).toEqual(created)
    expect(store.getSnapshot().pending[0].threadId).toBe('materialized')
  })

  it('keeps optimistic message through accepted and confirmed until thread.read reconciliation', () => {
    const store = new ThreadStore()
    store.selectThread('thread')
    store.addPending('request', 'thread', 'hello')
    store.apply(envelope('message.accepted', {}, 'request', 'thread'))
    store.apply(envelope('message.confirmed', {}, 'request', 'thread'))

    expect(store.getSnapshot().pending[0].status).toBe('confirmed')

    const realMessage: ThreadItem = {
      id: 'real-message', type: 'message', rawType: 'userMessage', role: 'user', content: 'hello\n', turnId: 'turn',
    }
    store.apply(envelope('thread.read.response', {
      threadId: 'thread', title: 'T', cwd: 'C:\\repo', updatedAt: 2, status: 'idle', items: [realMessage],
    }, 'read', 'thread'))

    expect(store.getSnapshot().pending).toHaveLength(0)
    expect(store.getSnapshot().items[0].id).toBe('real-message')
  })

  it('stores attachment names in optimistic messages and surfaces stop failures', () => {
    const store = new ThreadStore()
    store.selectThread('thread')
    store.addPending('request', 'thread', '', ['photo.jpg'])
    store.apply(envelope('codex.stop.failed', { code: 'CODEX_NOT_WORKING' }, 'stop', 'thread'))

    expect(store.getSnapshot().pending[0].attachments).toEqual(['photo.jpg'])
    expect(store.getSnapshot().error).toContain('没有正在执行')
  })

  it('stores generated media only for the active real thread', () => {
    const store = new ThreadStore()
    store.selectThread('thread')
    store.setMediaLoading('image-1')
    store.apply(envelope('media.read.response', {
      itemId: 'image-1', mimeType: 'image/png', dataBase64: 'iVBORw0KGgo',
    }, 'media', 'thread'))

    expect(store.getSnapshot().media['image-1']).toEqual({
      status: 'loaded', dataUrl: 'data:image/png;base64,iVBORw0KGgo',
    })

    store.apply(envelope('media.read.response', {
      itemId: 'image-2', mimeType: 'text/html', dataBase64: 'bad',
    }, 'media-2', 'thread'))
    expect(store.getSnapshot().media['image-2']).toBeUndefined()
  })
})
