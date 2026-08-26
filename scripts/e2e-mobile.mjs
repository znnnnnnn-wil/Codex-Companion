import { readFile } from 'node:fs/promises'
import { basename, delimiter, extname } from 'node:path'

const relayUrl = process.env.E2E_RELAY_URL ?? 'ws://127.0.0.1:18080/ws/web'
const pairingCode = process.env.E2E_PAIRING_CODE
const targetThread = process.env.E2E_THREAD_ID
const prompt = process.env.E2E_PROMPT ?? 'Codex Companion 端到端同步测试：请仅回复“端到端确认”，不要执行命令或修改文件。'
const expectIncrementalUpdate = process.env.E2E_EXPECT_UPDATE === '1'
const expectedReply = process.env.E2E_EXPECT_REPLY
const timeoutMs = Number(process.env.E2E_TIMEOUT_MS ?? 180_000)
const attachmentPaths = (process.env.E2E_ATTACHMENTS ?? '').split(delimiter).filter(Boolean)
const stopAfterConfirm = process.env.E2E_STOP_AFTER_CONFIRM === '1'
const mediaItemId = process.env.E2E_MEDIA_ITEM_ID
const mediaMimeType = process.env.E2E_MEDIA_MIME_TYPE ?? 'image/png'
const attachments = await Promise.all(attachmentPaths.map(async (path) => {
  const data = await readFile(path)
  return {
    name: basename(path),
    mimeType: mimeType(path),
    size: data.length,
    dataBase64: data.toString('base64'),
  }
}))

if (!pairingCode || !targetThread) {
  throw new Error('E2E_PAIRING_CODE and E2E_THREAD_ID are required')
}

const socket = new WebSocket(relayUrl)
let deviceId
let stage = 'pairing'
let beforeAssistantCount = 0
let sawThreadUpdated = false
let verifyTimer
const timeout = setTimeout(() => finish(new Error(`E2E timeout at stage ${stage}`)), timeoutMs)

socket.addEventListener('open', () => send('pairing.claim', undefined, { code: pairingCode }))
socket.addEventListener('error', () => finish(new Error('WebSocket error')))
socket.addEventListener('message', (event) => {
  const envelope = JSON.parse(String(event.data))
  console.log(JSON.stringify({ type: envelope.type, requestId: envelope.requestId, threadId: envelope.threadId }))
  if (envelope.type === 'error' || envelope.type === 'message.failed') {
    finish(new Error(`${envelope.payload?.code}: ${envelope.payload?.message}`))
    return
  }
  if (envelope.type === 'pairing.claimed') {
    deviceId = envelope.payload.deviceId
    stage = 'list'
    send('thread.list.request', undefined, {})
  } else if (envelope.type === 'thread.list.response' && stage === 'list') {
    const threads = envelope.payload.threads ?? []
    if (!threads.some((thread) => thread.threadId === targetThread)) {
      finish(new Error(`target thread not found in ${threads.length} real threads`))
      return
    }
    console.log(JSON.stringify({ realThreadCount: threads.length }))
    stage = 'initial-read'
    send('thread.read.request', targetThread, {})
  } else if (envelope.type === 'thread.read.response' && envelope.threadId === targetThread) {
    const items = envelope.payload.items ?? []
    const assistantCount = items.filter((item) => item.type === 'message' && item.role === 'assistant').length
    if (stage === 'initial-read') {
      beforeAssistantCount = assistantCount
      console.log(JSON.stringify({ realItemCount: items.length, beforeAssistantCount }))
      if (mediaItemId) {
        const mediaItem = items.find((item) => item.id === mediaItemId && item.type === 'image')
          ?? items.flatMap((item) => item.attachments ?? []).find((attachment) => attachment.id === mediaItemId)
        if (!mediaItem) {
          finish(new Error(`media item ${mediaItemId} not found in real history`))
          return
        }
        stage = 'media-read'
        send('media.read.request', targetThread, { itemId: mediaItemId })
      } else {
        stage = 'sending'
        send('message.send', targetThread, { text: prompt, attachments })
      }
    } else if (stage === 'verify') {
      const userConfirmed = items.some((item) =>
        item.type === 'message' && item.role === 'user' && userMatches(item.content))
      const matchingReply = items.some((item) =>
        item.type === 'message' && item.role === 'assistant' && replyMatches(item.content))
      if (userConfirmed && assistantCount > beforeAssistantCount && matchingReply
          && (!expectIncrementalUpdate || sawThreadUpdated)) {
        console.log(JSON.stringify({ e2e: 'success', userConfirmed, assistantReplySynchronized: true, expectedReplyMatched: true, sawThreadUpdated }))
        finish()
      } else {
        verifyTimer = setTimeout(() => send('thread.read.request', targetThread, {}), 1_000)
      }
    }
  } else if (envelope.type === 'message.confirmed') {
    if (stopAfterConfirm) {
      stage = 'stopping'
      send('codex.stop', targetThread, {})
    } else {
      stage = 'verify'
      send('thread.read.request', targetThread, {})
    }
  } else if (envelope.type === 'codex.stop.response' && stage === 'stopping') {
    console.log(JSON.stringify({ e2e: 'success', stopped: true }))
    finish()
  } else if (envelope.type === 'codex.stop.failed') {
    finish(new Error(`${envelope.payload?.code}: ${envelope.payload?.message}`))
  } else if (envelope.type === 'media.read.response' && stage === 'media-read') {
    const media = envelope.payload ?? {}
    const magic = { 'image/png': 'iVBORw0KGgo', 'image/jpeg': '/9j/', 'image/gif': 'R0lGOD', 'image/webp': 'UklGR' }[mediaMimeType]
    if (media.itemId !== mediaItemId || media.mimeType !== mediaMimeType
        || !magic || !String(media.dataBase64 ?? '').startsWith(magic)) {
      finish(new Error('media payload is invalid'))
      return
    }
    console.log(JSON.stringify({
      e2e: 'success', mediaSynchronized: true, mimeType: media.mimeType,
      base64Chars: media.dataBase64.length,
    }))
    finish()
  } else if (envelope.type === 'thread.updated' && envelope.threadId === targetThread) {
    sawThreadUpdated = true
    if (stage === 'verify') send('thread.read.request', targetThread, {})
  }
})

function send(type, threadId, payload) {
  socket.send(JSON.stringify({
    type,
    requestId: crypto.randomUUID(),
    deviceId,
    threadId,
    timestamp: Date.now(),
    payload,
  }))
}

function normalize(value) {
  return String(value ?? '')
    .trim()
    .replace(/\\([!"#$%&'()*+,\-./:;<=>?@[\]\\^_`{|}~])/g, '$1')
}

function replyMatches(value) {
  return expectedReply ? normalize(value) === normalize(expectedReply) : Boolean(normalize(value))
}

function userMatches(value) {
  const candidate = normalize(value)
  const expected = normalize(prompt)
  return candidate === expected || (attachments.length > 0 && candidate.endsWith(expected))
}

function mimeType(path) {
  const types = { '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.txt': 'text/plain', '.pdf': 'application/pdf' }
  return types[extname(path).toLowerCase()] ?? 'application/octet-stream'
}

function finish(error) {
  clearTimeout(timeout)
  clearTimeout(verifyTimer)
  socket.close()
  if (error) {
    console.error(error.stack ?? error.message)
    process.exitCode = 1
  }
}
