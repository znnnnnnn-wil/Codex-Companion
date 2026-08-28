# Codex Companion

> 让手机成为电脑上 Codex Desktop 的安全协作入口。

Codex Companion 是一个面向个人使用的自托管桥接方案：**Codex 仍然运行在你的 Windows 电脑上，手机只负责连接和交互**。在国内使用手机端 Codex 时，常见问题是登录和网络配置复杂、手机与电脑上的上下文割裂，还经常需要在手机上配置 VPN。这个项目把手机浏览器、一个公网 Relay 和 Windows Bridge 连起来，让你在手机上继续电脑端的真实 Codex thread，不用远程桌面，也不用把 Windows 电脑暴露到公网。

只要 Windows 电脑和手机都能访问你的 VPS，手机端就可以直接打开一个网页完成配对和聊天；Bridge 通过出站 WebSocket 连接 Relay，电脑不需要公网 IP、端口映射或 DDNS。长期使用可以启用 HTTPS/WSS；临时体验也可以用公网 IP 快速模式。

## 它真正解决的问题

### 手机端配置麻烦

手机不需要安装完整开发环境，也不需要把 Codex Desktop 搬到手机上。部署一次服务后，用手机浏览器打开地址，输入 Windows Bridge 显示的一次性配对码即可开始使用；Android APK 是可选封装，不是必需组件。

### 手机和电脑上下文不一致

手机端看到和发送的都是电脑 Codex Desktop 的真实 thread。已有项目、历史消息、生成图片和任务状态都以 Codex thread 为准，不会再产生一份孤立的“手机聊天记录”。你可以在电脑上继续操作，再回到手机查看增量更新。

### 不想在手机上长期依赖 VPN 或远程桌面

手机访问的是你自己部署的 Web/Relay 地址，而不是 Windows 桌面。Bridge 只向 Relay 发起出站连接，因此不需要开放电脑端口；手机也不需要运行 VPN 或远程桌面客户端。网络可达性取决于你选择的 VPS、域名和运营商网络，项目本身不会绕过网络限制。

### 希望远程协作，但不想给出整台电脑权限

它不是远程桌面。协议只开放 thread 列表、读取、创建、发送消息、读取已验证媒体和停止 Codex 等有限操作，没有 shell、任意文件读写、通用鼠标键盘或屏幕控制能力。

## 你会得到什么

- **连续的 Codex 工作流**：按项目浏览真实会话，在手机发起新 thread、发送文本和主动选择的附件，并接收电脑端的流式更新。
- **电脑端保持原样**：Codex Desktop 和本地代码仍在 Windows 上运行，手机只是一个轻量控制面板。
- **出站连接，少改网络**：Windows Bridge 主动连接 Relay，不需要电脑公网 IP、路由器端口转发或 DDNS。
- **浏览器优先**：部署完成后直接用手机浏览器访问；同一份 Web 构建产物也可以打包为 Android APK。
- **可诊断、可控制**：Bridge 提供 `setup`、`doctor`、启动/停止和可选登录自启动，默认不会在 Windows 登录后自动运行。
- **以安全为边界**：配对码一次性且 10 分钟过期，凭据只保存哈希或由系统安全存储保护，Relay 不持久化 prompt、回复或源代码。

## 工作方式

```text
手机浏览器 / Android（React Web UI）
              │ HTTPS/WSS
              ▼
       你的 VPS：Relay + Web
              ▲
              │ Windows 主动出站 WSS
              │
       Windows Bridge
              │
              ▼
        Codex Desktop
```

Codex thread 是唯一聊天记录真相源：Bridge 通过 Codex app-server 读取 thread、历史和状态，并在经过窗口与 thread 校验后使用 Codex Desktop 的 UI Automation 发送消息。Relay 只负责认证、路由和在线状态；PostgreSQL（HTTPS 模式）只保存设备、配对状态和凭据哈希。Web 端仅保留当前页面状态和待确认消息。

## 安全边界（重要）

- Web 到 Bridge 的消息使用显式 allowlist，仅包含 `thread.list.request`、`thread.create.request`、`thread.read.request`、`media.read.request`、`message.send` 和 `codex.stop`。
- 新建 thread 的 `cwd` 必须匹配 Bridge 从真实 thread 读取到的项目路径；不会借此打开任意本机目录。
- Bridge 会校验 Codex Desktop 进程路径和顶层窗口，所有 UI selector 都限制在已验证窗口的后代节点内。
- 配对凭据由 CSPRNG 生成 256-bit token；Relay/PostgreSQL 只保存 SHA-256 hash，Windows 端使用 DPAPI，Android 端使用 Keystore 保护的安全存储。
- Relay 不保存完整 prompt、回复、源码或附件内容；离线命令不会把 prompt 排队写入数据库。
- 生产环境应使用 HTTPS/WSS、限制 `ALLOWED_ORIGINS`、为 PostgreSQL 使用独立随机密码，并只公开反向代理端口。

完整说明见 [安全边界](docs/security.md)。

## 快速开始

### 前置条件

- 一台 Windows 10/11 电脑，已安装并登录 Codex Desktop。
- 已单独安装 Codex CLI（Bridge 需要可执行的 `codex.exe`，不能直接使用 MSIX 包内受保护文件）。
- 一台 Windows 可以主动访问的 Linux VPS，已安装 Docker Engine 和 Docker Compose v2.24+。
- 手机可以访问 VPS 的公网地址。手机和电脑不需要彼此直连。

### 个人首次体验：公网 IP 快速模式

此模式不需要域名，适合验证功能或个人临时使用；连接为 HTTP/WS，不建议公开或长期运行。

在 VPS 执行：

```bash
git clone https://github.com/znnnnnnn-wil/Codex-Companion.git /opt/codex-companion
cd /opt/codex-companion
cp .env.example .env
sed -i 's/^ALLOWED_ORIGINS=.*/ALLOWED_ORIGINS=你的VPS公网IP/' .env
docker compose -f deploy/docker-compose.quick.yml up -d --build
curl http://你的VPS公网IP/healthz
```

然后在 Windows 下载 GitHub Releases 中的 Bridge ZIP，运行安装脚本并填写：

```text
ws://你的VPS公网IP/ws/bridge
```

Bridge 会显示一次性配对码。手机浏览器打开：

```text
http://你的VPS公网IP
```

输入配对码即可完成连接。配对完成后回到 Windows PowerShell，安装器会结束临时配对进程；**此时还需要手动启动 Bridge**：

```powershell
$bridgeControl = "$env:LOCALAPPDATA\CodexCompanion\Bridge\bridge-control.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bridgeControl -Action Start
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bridgeControl -Action Status
```

确认状态输出中的 `Running` 为 `True` 后，重新打开或刷新手机浏览器即可连接。日后暂时不用时可以停止：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bridgeControl -Action Stop
```

安装后 Bridge 默认保持停止，由你决定何时启动；控制命令、登录自启动和故障诊断见 [快速部署](docs/quickstart.md)。

### 长期使用：域名 HTTPS 模式

准备一个解析到 VPS 的域名，放行 TCP 80/443，然后在 `.env` 中设置：

```env
POSTGRES_PASSWORD=生成一个随机长密码
PUBLIC_HOST=companion.example.com
ALLOWED_ORIGINS=companion.example.com,localhost
```

启动：

```bash
docker compose -f compose.yml -f deploy/docker-compose.https.yml up -d --build
```

Windows Bridge 使用 `wss://companion.example.com/ws/bridge` 完成 `setup` 和首次配对；配对完成后，在 PowerShell 执行上面的 `bridge-control.ps1 -Action Start` 启动后台 Bridge，再用手机访问 `https://companion.example.com`。Caddy 会自动申请和续期证书。完整的 IP、HTTPS、预构建镜像和更新流程见 [快速部署](docs/quickstart.md) 与 [公网部署与验收](docs/deployment.md)。

## Bridge 常用命令

```powershell
# 首次配置 Relay 地址、Codex CLI 路径并完成配对
CodexCompanion.Bridge.exe setup

# 诊断环境；--json 便于自动化收集，不输出凭据
CodexCompanion.Bridge.exe doctor
CodexCompanion.Bridge.exe doctor --json

# 运行 Bridge
CodexCompanion.Bridge.exe run
```

安装器也会创建启动、停止、状态、配置和诊断快捷方式。登录自启动需要显式启用，可随时关闭。

## 当前能力

- .NET 10 Windows Bridge：真实 `thread/list`、`thread/read`、`thread/start`，项目路径和标题消歧，Desktop 语义化发送，真实历史确认，增量 polling 和 Relay 自动重连。
- Go Relay：WebSocket 路由、pairing、在线状态、request correlation、PostgreSQL Store 和消息类型 allowlist。
- React/Vite Web 客户端：移动优先聊天、按项目新建会话、侧栏抽屉、Markdown/GFM/code block、自动滚动、pending→confirmed 对账、PC/Codex 状态、附件和错误重连提示。
- 可选 Android：由 Capacitor 打包同一份 Web 构建产物；Android 凭据使用 Keystore 保护的安全存储。
- 测试与验收：Relay 路由/认证/配对/断线/关联、Bridge parser/UI abstraction/send timeout、Web store/reconnect，以及真实端到端脚本。

## 非目标

项目不提供远程桌面、终端、任意文件操作、通用鼠标键盘控制、Claude/Gemini、多 Agent、Git UI 或企业级权限系统。它专注于一件事：**把电脑上真实的 Codex 工作流安全地带到手机浏览器**。

## 开发者

源码开发、测试、Android Debug 构建和架构细节见：

- [开发文档](docs/development.md)
- [架构与真相源](docs/architecture.md)
- [WebSocket 协议](docs/protocol.md)
- [本机 Codex 真实探测](docs/codex-research.md)

以下服务请分别在独立终端运行：

```powershell
docker compose up -d postgres
```

```powershell
cd services/relay
$env:DATABASE_URL='postgres://codex_companion:codex_companion_dev@127.0.0.1:5432/codex_companion?sslmode=disable'
go run ./cmd/server
```

```powershell
cd apps/bridge
$env:CODEX_COMPANION_RELAY_URL='ws://127.0.0.1:8080/ws/bridge'
dotnet run -- run
```

```powershell
cd apps/web
npm install
npm run dev
```

常用检查：

```powershell
cd apps/web
npm test
npm run lint
npm run build
```

## 许可证

本项目采用 [MIT License](LICENSE) 开源。
