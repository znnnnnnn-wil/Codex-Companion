# 安全边界

## 能力最小化

允许从 Web 发往 Bridge 的消息只有：

- `thread.list.request`
- `thread.create.request`
- `thread.read.request`
- `media.read.request`
- `message.send`
- `codex.stop`

`thread.create.request` 的 `cwd` 必须精确匹配 Bridge 从现有真实 thread 读取到的项目路径，不能用它打开任意本机目录。`message.send` 可以携带用户在手机上主动选择的附件，但没有按路径读取/写入电脑文件的 API。`media.read.request` 只接受真实 thread 中的生成图或由真实 user message 派生的附件 itemId，Bridge 不接受来自手机的本机路径。协议仍没有 `shell.exec`、通用文件操作、通用鼠标键盘或远程桌面能力。`codex.stop` 只能在唯一定位真实 thread 后调用 Codex Desktop 自己的停止按钮。Relay Hub 对 Web 与 Bridge 分别使用显式 allowlist。

UI Automation 驱动会先验证进程路径属于 `OpenAI.Codex` MSIX 包，再验证顶层窗口包含 `Document(Name=Codex, AutomationId=RootWebArea)`。所有 selector 都限制在这个窗口的后代节点内。

## 凭据

- 配对凭据来自 32 字节 CSPRNG（256 bit），只在 WSS 配对响应中返回一次。
- Relay/PostgreSQL 只存 SHA-256 hash，不存明文 token。
- Bridge 凭据使用 Windows DPAPI `CurrentUser` 加密后写入 LocalAppData。
- Android App 凭据通过 `@aparajita/capacitor-secure-storage` 存入 Android Keystore 保护的加密存储；业务代码只依赖适配层。Web/Vite 开发环境继续使用 localStorage。升级时若原生存储为空，会尝试把旧 localStorage 凭据迁移后删除；不同 WebView storage 不共享时要求重新 pairing。
- 配对码为 8 位、10 分钟过期且只能 claim 一次。

## 数据最小化

- Relay 不持久化完整 prompt、回复或源代码。
- `pending_commands` 只允许保存 request metadata；当前 V0 离线时直接返回 `DEVICE_OFFLINE`，不会排队保存 prompt。
- 日志不记录 credential、token、完整 prompt 或源码。app-server stderr 只记录长度。
- 未知 app-server item 被转换成 `[eventType]` 与可选 status，不透传未知 payload。
- 附件不写入 PostgreSQL，也不记录 Base64、文件内容或完整文件名日志。发送失败立即清理；发送成功后在 `%LOCALAPPDATA%\CodexCompanion\Uploads` 的随机请求目录最多保留 7 天，供同一个真实 thread 在手机回看，Bridge 启动或再次上传时清理过期目录。
- 附件限制为最多 4 个、单个 8 MiB、总计 12 MiB；Relay 在认证前只接受 64 KiB 握手帧，认证后最大 18 MiB。
- 生成图片只在手机实际显示时按 item 请求，Relay 仅内存转发；Bridge 校验真实 thread、item 类型、完成状态、图片文件头与 16 MiB Base64 上限。
- 历史附件图片同样按 item 请求；Bridge 重新读取真实 message、验证派生 ID、文件仍存在、12 MiB 上限和图片魔数，DTO 不暴露 Windows 路径。

## 生产要求

- Relay 使用 HTTPS/WSS，关闭公网明文 `ws://`。
- `ALLOWED_ORIGINS` 只列出实际域名和 Capacitor `localhost` origin，不使用 `*`。
- PostgreSQL 使用独立账号、私网访问、备份和密码轮换。
- 在反向代理限制 WebSocket 连接速率；应用内认证后 frame 上限为 18 MiB。
- 不把开发用数据库密码用于生产。
