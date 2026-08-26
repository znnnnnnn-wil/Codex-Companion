// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { encodeAttachments, MAX_ATTACHMENT_BYTES, validateAttachments } from './attachments'

describe('attachments', () => {
  it('encodes browser files without changing their metadata', async () => {
    const file = new File(['hello'], 'note.txt', { type: 'text/plain' })

    const [encoded] = await encodeAttachments([file])

    expect(encoded).toEqual({
      name: 'note.txt', mimeType: 'text/plain', size: 5, dataBase64: 'aGVsbG8=',
    })
  })

  it('rejects an oversized attachment before opening the websocket payload', () => {
    const file = new File([new Uint8Array(1)], 'large.bin')
    Object.defineProperty(file, 'size', { value: MAX_ATTACHMENT_BYTES + 1 })

    expect(validateAttachments([file])).toContain('8 MiB')
  })
})
