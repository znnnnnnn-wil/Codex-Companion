# 本机 Codex 技术探测（2026-08-25）

## 版本与安装

- Codex Desktop MSIX：`OpenAI.Codex 26.818.8289.0`。
- Desktop 主进程：包内 `ChatGPT.exe`；实际 app-server 子进程命令包含 `codex ... app-server --analytics-default-enabled`。
- 包内 `codex.exe` 标记为 `Application Protected`。当前用户可以读取 metadata，但直接启动返回“拒绝访问”，复制会因应用保护加密失败。
- 使用官方 npm 独立 CLI `@openai/codex 0.149.1` 后，`codex app-server --help` 与真实协议探测成功。
- 本机 app-server initialize user agent：`Codex Desktop/0.149.1 (Windows 10.0.26200; x86_64)`。

官方说明见 [Codex App Server](https://learn.chatgpt.com/docs/app-server)：默认 stdio 是 JSONL；wire format 是省略 `jsonrpc` header 的 JSON-RPC 2.0；每个连接必须先 `initialize`，再通知 `initialized`。

## 实际 transport 与方法

启动：

```text
codex app-server --stdio
```

成功调用：

- `thread/list`：`limit=100, sortKey=updated_at, sortDirection=desc, sourceKinds=[cli,vscode,appServer]`
- `thread/read`：`threadId=<real id>, includeTurns=true`

`thread/list` 在一次 scan-and-repair 读取中观察到相同 id 的重复 metadata row；Bridge 按返回顺序保留第一条（最新）并按 id 去重。

## 实际 schema 摘要

Thread 包含：`id`、`name`、`cwd`、`updatedAt`、`status.type`、`threadSource/source`、`turns[]`。

实测 item 类型与关键字段：

- `userMessage`：`id`, `content[]`；文本 part 为 `{type:"text", text}`。
- `agentMessage`：`id`, `text`, `phase`。
- `commandExecution`：`id`, `status`, command/output 等敏感字段。
- `fileChange`：`id`, `status`, changes/diff 等敏感字段。
- `reasoning`：`id`, `summary[]`, `content[]`。
- `webSearch`：`id`, `query`, `results[]`。
- `imageGeneration`：`id`, `status`, `revisedPrompt`, `result`（Base64）, `savedPath`。实测完成图片为 PNG，Base64 长度 3,230,840。

Transport parser 完整转换 user/agent message，并把 `imageGeneration` 转换为不含图片字节的 image item。手机按 itemId 请求时才从同一真实 thread 返回图片。其余类型只保留 id、raw type 与 status，不发送 command、diff、reasoning 或搜索结果。新增类型不会让整个 thread 失败。

## UI Automation 实测

正式 Inspector 输出位于 `docs/codex-ui-tree.txt`，当前捕获 2438 个 UIA 节点。关键结构：

- 顶层：`Window ClassName=Chrome_WidgetWin_1`。
- 文档：`Document Name=Codex AutomationId=RootWebArea`。
- workspace：`Group Name=codexDestop ClassName` 含 `group/cwd`。
- thread：`Button Name=<title>`，ClassName 含 `sidebar-item` 和 `group relative cursor-interaction text-sm`，支持 `InvokePattern`。
- 当前选中项的 class token 额外包含独立的 `bg-primary-ghost-hover`。
- 输入框：`Edit ClassName=ProseMirror`，实测支持 `ValuePattern` 与 `TextPattern`。
- 工作中按钮：`Button Name=停止`，支持 `InvokePattern`；输入内容后 idle 状态出现 `Button Name=发送`。
- 当前会话来源附件按钮：`Button Name=附加文件或连接应用`，下层 `MenuItem Name=添加文件或文件夹`。实测它稳定打开同进程原生选择窗口，并将附件写入该真实 thread，因此作为首选路径。
- 当前输入区附件按钮：`Button Name=添加文件等内容`，下层 `Button Name=文件和文件夹`。两者分别支持 `ExpandCollapsePattern` / `InvokePattern`，在来源面板隐藏的布局中作为回退路径。
- 原生选择窗口：同一 Codex 进程的 `Window Name=选择文件 ClassName=#32770`；`ComboBox AutomationId=1148` 是文件名输入，`Button AutomationId=1` 是打开，均已实测。查找同时约束 `ControlType`，避免与文件列表中重复的数字 AutomationId 混淆。

Electron 会创建多个顶层 Chrome 窗口，`Process.MainWindowHandle` 会漂移。因此 Bridge 按已验证包进程 id 枚举所有 UIA 顶层窗口，再用 `RootWebArea=Codex` 识别主窗口。

UIA 写入会让 Chromium 在真实 user message 末尾增加一个 LF。实测包含下划线等 Markdown 标点的 user message 会在 `thread/read` 中以反斜杠转义形式出现（例如 `PUBLIC\_E2E\_OK`）；确认逻辑因此统一行尾并只消除标准 Markdown 标点转义，再做精确比较。

带附件的真实 user message 会由 Desktop 包装成 `# Files mentioned by the user` 文件清单、`Distinguish instructions...` 安全提示和 `## My request`。Bridge 在只检查本次新增 user item 的前提下，对附件消息用规范化后的 request 尾部匹配确认，不把手机 optimistic item 当作真相源。

该附件包装也是 app-server 当前唯一提供的历史附件关联：没有独立 attachment item。Bridge 因此从真实 message 派生附件 ID，对手机隐藏本机路径，并只把 `My request` 作为聊天正文；原文件仍在电脑时可按需返回图片，旧版本已经删除的临时上传文件不能恢复。

## 实际结果

- `thread/list`：成功。公网 E2E 时 C# Bridge 实测返回 175 个真实 thread（数量随用户历史变化）。
- `thread/read`：成功。当前开发 thread 实测读取 user/agent message、reasoning、command、file change 和 web search item。
- Desktop thread selection：成功。使用 title + cwd 最后一级 workspace 唯一定位；重名未唯一时明确失败。
- Desktop message send：成功。对真实 `D:\python\timeflow` thread 发送 UIA PoC，Desktop 持久化消息并由 Codex 回复。
- Desktop→phone synchronization：成功。公网协议级手机完成 pairing/list/read/send，真实 task `Codex Companion E2E Test` 收到消息并回复 `PUBLIC_E2E_OK_3`，Web 收到 `thread.updated` 后从真实历史精确回读确认。
- Desktop attachment upload：成功。通过 UIA 向专用 E2E task 一次选择 TXT 与 PNG，真实 thread 出现两条文件引用，Codex 读取后精确回复 `ATTACHMENT_E2E_V2_OK`。
- Public multi-image upload：成功。三张图片经公网大 WebSocket frame、Relay、Bridge 和 Desktop 原生选择窗口进入真实 thread；真实历史确认后 Codex 精确回复 `PUBLIC_ATTACHMENT_E2E_V6_OK`，并收到 `thread.updated`。
- Generated image→phone：成功。公网按需读取真实 `imageGeneration` item，手机协议端收到 `image/png`、3,230,840 字符 Base64 并校验 PNG 文件头。
- 2026-08-25 移动端图片/布局修复复测：指定真实 thread `01a0379c-f10a-7951-8598-b4b6abfff19b` 的新生成 PNG（2,606,852 字符 Base64）和仍存在的历史 PNG 附件（225,444 字符 Base64）均通过公网 `media.read.response`；附件系统前言不再作为 Markdown 标题显示。
- Desktop stop：成功。专用 E2E task 启动 60 秒命令后调用 `Button Name=停止`，Desktop 状态恢复 `idle`，预设完成回复未出现。

## 限制

- UI 未暴露 thread id，V0 仍依赖 title + workspace。相同 workspace 下同标题会话返回 `AMBIGUOUS_THREAD`。
- Electron CSS class 与中文 accessible Name 可能随 Desktop 更新变化；升级后应先重跑 Inspector。
- 独立 app-server `thread/read` 不订阅 Desktop app-server 的已加载 thread 通知，因此当前采用 polling，而非事件 stream。
- 未验证通过独立 app-server `turn/start` 写入 Desktop 的 writer conflict/sidebar 行为，所以 V0 严格保持 UIA 写入。
