# Codex Companion

Codex Companion 是一个连接 Windows Codex Desktop 的个人自托管伴侣。主要入口是手机或电脑浏览器，浏览器通过公网 Relay 连接 Windows Bridge；项目同时保留可选的 Capacitor Android 封装。不显示 Windows 桌面。所有会话都对应真实 Codex `threadId`，Codex thread 是唯一聊天记录真相源。

## 当前已实现

- .NET 10 Bridge：真实 `thread/list` / `thread/read` / `thread/start`、容错 parser、UIA tree inspector、title+workspace 消歧、Desktop 语义化发送、真实历史确认、Relay reconnect、DPAPI 凭据和增量 polling。
- Bridge 提供 `setup` 配置向导、`doctor` / `doctor --json` 诊断，以及可手动控制的 Windows 后台启动、停止和可选登录自启动。
- Go Relay：WebSocket 单体路由、pairing、256-bit credential hash、在线状态、request correlation、PostgreSQL Store、消息类型 allowlist。
- React/Vite 客户端：移动优先聊天、按项目新建会话、侧栏抽屉、Markdown/GFM/code block、自动滚动、pending→confirmed 对账、PC/Codex 状态、错误文案和指数重连；同一份构建产物由 Capacitor 打包进 Android APK。
- 测试：Relay routing/auth/pairing/disconnect/correlation，Bridge parser/UI abstraction/send timeout，Web store/reconnect；另有真实端到端脚本。

## 快速开始

普通用户请先阅读 [快速部署](docs/quickstart.md)；源码开发请阅读 [开发文档](docs/development.md)。先打开并登录 Codex Desktop。

### 源码开发快速开始

下面的命令只适用于贡献者或需要从源码运行项目的开发者。普通用户请使用上面的快速部署文档。

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

浏览器是当前推荐的使用入口。部署完成后，手机或电脑直接打开部署后的 Web 地址即可使用；无需 Android Studio。Android Debug 构建是可选的真机体验版本，需要通过本地环境文件配置 Relay 地址。

```powershell
cd apps/web
npm install
npm test
npm run lint
npm run build
npm run android:build
```

APK 输出到 `apps/web/android/app/build/outputs/apk/debug/app-debug.apk`。

Android Debug 构建前，复制 `apps/web/.env.android-debug.example` 为 `apps/web/.env.android-debug`，并将其中的 Relay 地址替换为你的环境地址。

## 关键文档

- [架构与真相源](docs/architecture.md)
- [WebSocket 协议](docs/protocol.md)
- [安全边界](docs/security.md)
- [本机 Codex 真实探测](docs/codex-research.md)
- [公网部署与验收](docs/deployment.md)
- [快速部署（IP / HTTPS 两种模式）](docs/quickstart.md)
- [UIA tree](docs/codex-ui-tree.txt)

## 产品架构

```text
手机/电脑浏览器（React/Vite Web UI）
        ↓ HTTPS/WSS
Public Relay + PostgreSQL
        ↓ WSS
Windows Bridge
        ↓
Codex Desktop
```

`apps/web` 同时提供浏览器入口和可选 Android 封装。个人使用时优先通过浏览器访问部署好的 Web 页面；Android 只用于需要独立 APK 的场景。

## 非目标

项目没有远程桌面、终端、任意文件操作、通用鼠标键盘、Claude/Gemini、多 Agent、Git UI 或企业权限系统。

## 许可证

本项目采用 [MIT License](LICENSE) 开源。
