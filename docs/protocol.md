# Codex Companion WebSocket Protocol V0

Relay 端点：

- Bridge：`/ws/bridge`
- Web：`/ws/web`

生产环境只使用 WSS。每个 text frame 包含一个 JSON envelope：

```json
{
  "type": "thread.read.request",
  "requestId": "uuid",
  "deviceId": "uuid",
  "threadId": "real-codex-thread-id",
  "timestamp": 1787620000000,
  "payload": {}
}
```

`requestId` 由请求方生成；Relay 记录内存 correlation，并确保 response 只返回给发起请求的 Web peer。`deviceId` 在 Relay 中会被认证上下文覆盖，不能由客户端伪造。`threadId` 始终是 Codex app-server 返回的真实 id。

## 握手与配对

未配对 Bridge 的第一帧：

```json
{"type":"pairing.create","requestId":"uuid","timestamp":0,"payload":{"deviceName":"PC"}}
```

Relay 返回 `pairing.created`，payload 含 `deviceId`、8 位 `code`、一次性明文 `bridgeCredential` 和 `expiresAt`。已配对 Bridge 与 Web 的第一帧为：

```json
{"type":"device.hello","requestId":"uuid","timestamp":0,"payload":{"deviceId":"uuid","credential":"256-bit-token"}}
```

手机首次配对的第一帧为 `pairing.claim`；成功返回 `pairing.claimed` 和一次性 `webCredential`，同时 Bridge 收到 `pairing.completed`。

## 消息类型

| type | 方向 | terminal | 用途 |
|---|---|---:|---|
| `device.hello` | client→Relay | - | 凭据握手 |
| `device.online` / `device.offline` | Relay→Web | - | PC 在线状态 |
| `thread.list.request` | Web→Bridge | 否 | 请求真实 thread 列表 |
| `thread.list.response` | Bridge→Web | 是 | `{threads:[...]}` |
| `thread.create.request` | Web→Bridge | 否 | `{cwd}`；在已有项目路径中创建真实 thread |
| `thread.create.response` | Bridge→Web | 是 | `{thread}`；返回新建的真实 thread summary |
| `thread.create.failed` | Bridge→Web | 是 | 新建失败及统一错误码 |
| `thread.read.request` | Web→Bridge | 否 | 请求真实 thread 历史 |
| `thread.read.response` | Bridge→Web | 是 | 统一 history DTO |
| `thread.updated` | Bridge→Web | - | item/status 增量 |
| `media.read.request` | Web→Bridge | 否 | 用真实 `threadId + itemId` 按需读取生成图或历史附件图 |
| `media.read.response` | Bridge→Web | 是 | `{itemId,mimeType,dataBase64}`，仅瞬时路由 |
| `message.send` | Web→Bridge | 否 | `{text,attachments[]}`；附件仅随请求瞬时路由 |
| `message.accepted` | Bridge→Web | 否 | Bridge 已接收，仍未确认真实历史 |
| `message.confirmed` | Bridge→Web | 是 | `thread/read` 已发现真实 user message |
| `message.failed` | Bridge→Web | 是 | 发送或确认失败 |
| `bridge.status` | Bridge→Web | - | Bridge 版本/在线状态 |
| `codex.status` | Bridge→Web | - | `offline` / `idle` / `working` |
| `codex.stop` | Web→Bridge | 否 | 请求中止指定真实 thread 当前任务 |
| `codex.stop.response` | Bridge→Web | 是 | 已调用目标会话的 Desktop“停止”按钮 |
| `codex.stop.failed` | Bridge→Web | 是 | 中止失败及统一错误码 |
| `error` | 双向 | 是 | 统一错误 |

附件 DTO：

```json
{
  "name": "photo.jpg",
  "mimeType": "image/jpeg",
  "size": 123456,
  "dataBase64": "..."
}
```

每次最多 4 个、单个最多 8 MiB、合计最多 12 MiB。Relay 只在内存中转发当前 WebSocket frame，不持久化附件；Bridge 校验声明大小与 Base64 实际长度后写入受控临时目录，通过 Codex Desktop 原生附件入口提交，确认后清理。协议没有通用文件读取或写入命令。

## DTO

Thread summary：

```json
{
  "threadId": "01a...",
  "title": "Fix login bug",
  "cwd": "D:\\repo",
  "updatedAt": 1787620000,
  "status": "notLoaded",
  "source": "vscode"
}
```

History item：

```json
{"id":"...","type":"message","rawType":"userMessage","role":"user","content":"hello","turnId":"..."}
```

生成图片 item 只在历史里携带元数据，不内联大图片：

```json
{"id":"exec-...","type":"image","rawType":"imageGeneration","role":"assistant","content":"revised prompt","status":"completed","turnId":"..."}
```

Web 随后按需请求该 item，Bridge 只允许读取同一真实 thread 中 `type=imageGeneration` 且已完成的结果；不接受文件路径。支持 PNG/JPEG/GIF/WebP，单个 Base64 最大 16 MiB。Relay 与 PostgreSQL 不保存图片。

Desktop 历史附件位于真实 `userMessage` 的 `Files mentioned by the user` 前言。Bridge 将它转换为不含路径的 `attachments:[{id,name,mimeType,available}]`，消息正文只保留 `My request`。历史附件图片也只通过真实 `threadId +` 由消息 ID 派生的附件 `itemId` 读取，Web 不能提交本机路径。

未知 app-server item：

```json
{"id":"...","type":"unsupported","rawType":"futureEvent","content":"[futureEvent]","status":null,"turnId":"..."}
```

未知 payload 不透传，避免新增事件泄露内容或破坏 parser。

## 错误码

`DEVICE_OFFLINE`、`CODEX_NOT_RUNNING`、`CODEX_APP_SERVER_UNAVAILABLE`、`THREAD_CREATE_FAILED`、`THREAD_NOT_FOUND`、`AMBIGUOUS_THREAD`、`MEDIA_NOT_FOUND`、`CODEX_INPUT_NOT_FOUND`、`CODEX_ATTACHMENT_FAILED`、`ATTACHMENT_TOO_LARGE`、`CODEX_SEND_FAILED`、`CODEX_NOT_WORKING`、`CODEX_STOP_FAILED`、`THREAD_CONFIRM_TIMEOUT`、`UNAUTHORIZED`。

Relay 还可返回协议错误：`INVALID_REQUEST`、`DUPLICATE_REQUEST_ID`。

## 重连

Web 与 Bridge 都使用 1s、2s、4s、8s，最高 30s 的指数退避。重连后先发 `device.hello`；Web 随后重新请求 `thread.list`，如果已有 active thread 再发 `thread.read.request`。不恢复旧 transient event。
