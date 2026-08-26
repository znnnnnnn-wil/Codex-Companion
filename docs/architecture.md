# Codex Companion 架构

## 唯一真相源

Codex thread 是唯一聊天记录真相源。Web 的 store 只保存当前页面状态与 optimistic pending message；Relay 不保存聊天正文；PostgreSQL 只保存设备、凭据哈希和配对状态。

```text
Codex thread (~/.codex)
        ↑ 读取：codex app-server thread/list、thread/read
        │
Windows Bridge ──新建：app-server thread/start
        │         写入：Codex Desktop UI Automation
        │ WSS（主动出站）
        ▼
Relay 单体服务 ── PostgreSQL（仅产品元数据）
        ▲
        │ WSS
手机/电脑浏览器（React/Vite UI、UI cache / optimistic pending）
```

## 组件边界

- `apps/bridge`：启动独立 app-server 读取及新建真实 thread；仅在验证过的 Codex 窗口内执行 UIA 写入；主动连接 Relay；polling 活动 thread 并推送 diff。
- `services/relay`：认证、在线 Hub、WebSocket routing、request correlation、配对和 PostgreSQL storage。它不运行 Codex，不读取用户代码。
- `apps/web`：React/Vite 移动端聊天 UI、浏览器入口、可选 Capacitor Android 壳、Markdown、pending 状态和 WebSocket reconnect。它不创建本地 conversation 数据库；浏览器是当前推荐的个人使用入口。

Android WebView 使用 Capacitor 默认 `https://localhost` origin。Relay 的 `ALLOWED_ORIGINS` 必须显式包含 `localhost`（以及实际 Web 调试域名），不能使用 `*`。

## 读取与写入

读取使用当前 CLI `codex app-server --stdio`，完成 `initialize` / `initialized` 后调用稳定方法。手机在已有项目中新建会话时调用 `thread/start(cwd)`，并通过 `thread/name/set` 设置可定位的唯一标题。消息写入不调用 `turn/start`，而是按 `threadId → title + cwd → sidebar item` 映射，在 Codex Desktop 中切换 thread、通过 `ValuePattern` 写入编辑框、通过 `InvokePattern` 发送。

同标题候选先按 workspace 过滤；仍然超过一个候选时返回 `AMBIGUOUS_THREAD`，绝不选择第一个。

## 实时同步

独立 app-server 的 `thread/read` 不会订阅 Desktop 已加载 thread 的事件，所以 V0 使用 polling：Codex working 时 750ms，idle 时 3s。Tracker 保存 item id 与内容指纹；新增 item 或相同 id 的 streaming 内容变化才产生 `thread.updated`。

## 部署边界

Bridge 只需要向公网 Relay 建立 WSS 出站连接，不需要公网 IP、端口映射或 DDNS。生产部署必须在 Relay 前终止 TLS，并让 PWA 通过 HTTPS 加载。
