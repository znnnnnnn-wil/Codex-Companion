export type ConnectionState = 'connecting' | 'connected' | 'disconnected'

export interface Envelope<T = unknown> {
  type: string
  requestId?: string
  deviceId?: string
  threadId?: string
  timestamp: number
  payload: T
}

export interface DeviceCredential {
  deviceId: string
  credential: string
}

export interface ThreadSummary {
  threadId: string
  title: string
  cwd: string
  updatedAt: number
  status: string
  source: string
}

export interface ThreadItem {
  id: string
  type: 'message' | 'image' | 'unsupported'
  rawType: string
  role?: 'user' | 'assistant'
  content?: string
  status?: string
  turnId: string
  attachments?: ThreadAttachment[]
}

export interface ThreadAttachment {
  id: string
  name: string
  mimeType: string
  available: boolean
}

export interface MediaContent {
  itemId: string
  mimeType: string
  dataBase64: string
}

export interface MediaState {
  status: 'loading' | 'loaded' | 'failed'
  dataUrl?: string
}

export interface ThreadHistory {
  threadId: string
  title: string
  cwd: string
  updatedAt: number
  status: string
  items: ThreadItem[]
}

export interface PendingMessage {
  requestId: string
  threadId: string
  text: string
  attachments: string[]
  status: 'pending' | 'accepted' | 'confirmed' | 'failed'
  error?: string
}

export interface MessageAttachment {
  name: string
  mimeType: string
  size: number
  dataBase64: string
}
