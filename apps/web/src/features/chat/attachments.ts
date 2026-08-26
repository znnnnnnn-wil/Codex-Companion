import type { MessageAttachment } from '../../protocol/types'

export const MAX_ATTACHMENT_FILES = 4
export const MAX_ATTACHMENT_BYTES = 8 * 1024 * 1024
export const MAX_ATTACHMENT_TOTAL_BYTES = 12 * 1024 * 1024

export function validateAttachments(files: readonly File[]): string | undefined {
  if (files.length > MAX_ATTACHMENT_FILES) return `每次最多选择 ${MAX_ATTACHMENT_FILES} 个附件。`
  if (files.some((file) => file.size > MAX_ATTACHMENT_BYTES)) return '单个附件不能超过 8 MiB。'
  if (files.reduce((sum, file) => sum + file.size, 0) > MAX_ATTACHMENT_TOTAL_BYTES) {
    return '附件总大小不能超过 12 MiB。'
  }
  return undefined
}

export async function encodeAttachments(files: readonly File[]): Promise<MessageAttachment[]> {
  const error = validateAttachments(files)
  if (error) throw new Error(error)
  return await Promise.all(files.map(async (file) => ({
    name: file.name,
    mimeType: file.type || 'application/octet-stream',
    size: file.size,
    dataBase64: await readBase64(file),
  })))
}

function readBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => {
      const value = String(reader.result ?? '')
      const separator = value.indexOf(',')
      if (separator < 0) reject(new Error('附件读取失败。'))
      else resolve(value.slice(separator + 1))
    }
    reader.onerror = () => reject(new Error('附件读取失败。'))
    reader.readAsDataURL(file)
  })
}
