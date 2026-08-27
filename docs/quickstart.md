# 快速部署

Codex Companion 有两种部署方式。个人首次体验建议使用 IP 快速模式；长期使用建议使用域名 HTTPS 模式。

## 前置条件

两种模式都需要：

- 一台 Windows 10/11 电脑
- Windows PowerShell 5.1 或 PowerShell 7
- 已安装并登录 Codex Desktop
- 已安装 Codex CLI（Bridge 需要独立的 `codex.exe`，不能直接使用 MSIX 包内的受保护文件）
- 一台可以被 Windows 电脑主动访问的 Linux VPS
- VPS 已安装 Docker Engine 和 Docker Compose v2.24+

服务器端不需要安装 Go、Node.js、.NET 或 JDK。它们只用于从源码开发和构建。

## 模式 A：公网 IP 快速模式

此模式不需要域名，适合个人使用。连接使用 HTTP/WS，不建议公开或长期使用。

在 VPS 执行：

```bash
git clone https://github.com/znnnnnnn-wil/Codex-Companion.git /opt/codex-companion
cd /opt/codex-companion
cp .env.example .env
```

编辑 `.env`，快速模式只需要设置：

```env
ALLOWED_ORIGINS=你的VPS公网IP
```

启动服务：

```bash
docker compose -f deploy/docker-compose.quick.yml up -d --build
docker compose ps
curl http://你的VPS公网IP/healthz
```

此模式使用 Relay 内存存储，不需要 PostgreSQL；Relay 重启后需要重新配对。也可以在仓库目录直接运行一键脚本：

```bash
bash scripts/install-server.sh --host 你的VPS公网IP
```

如果服务器能正确返回公网地址，也可以省略 `--host`；脚本会尝试自动探测。

在 Windows 上安装 Bridge：

1. 从 GitHub Releases 下载最新的 `CodexCompanion-Bridge-win-x64-*.zip` 并解压。
2. 在解压目录打开 PowerShell，执行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./install-bridge.ps1`。也可以在 PowerShell 7 中执行 `pwsh -File ./install-bridge.ps1`。
3. 按提示填写 `ws://你的VPS公网IP/ws/bridge`。
4. 首次安装脚本会在当前窗口运行 Bridge 并显示配对码。
5. 在手机打开 `http://你的VPS公网IP`，输入配对码；完成后回到 PowerShell 按 Enter。安装完成后 Bridge 默认保持停止，由你手动启动。

安装脚本会注册一个“按需运行”的后台任务，但不会默认加入 Windows 登录启动。日常控制命令如下（在任意 PowerShell 窗口执行）：

```powershell
$bridgeControl = "$env:LOCALAPPDATA\CodexCompanion\Bridge\bridge-control.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bridgeControl -Action Start
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bridgeControl -Action Stop
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bridgeControl -Action Status
```

需要开机自动启动时，再主动启用；不需要时可随时关闭：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bridgeControl -Action EnableAutostart
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bridgeControl -Action DisableAutostart
```

GUI 安装器会在开始菜单创建“启动 Bridge”“停止 Bridge”“Bridge 状态”“Bridge 配置”和“Bridge 诊断”快捷方式；“登录 Windows 后自动启动”和“安装完成后启动”默认不勾选。

Bridge 发布包中的安装脚本使用带 BOM 的 UTF-8 编码，同时支持 Windows PowerShell 5.1 和 PowerShell 7。`v0.1.1` 的 ZIP 安装脚本缺少 BOM，在 Windows PowerShell 5.1 中可能报告 `TerminatorExpectedAtEndOfString`。遇到此错误时请升级到修复后的版本；升级前也可以在解压目录直接完成配置和配对：

```powershell
./CodexCompanion.Bridge.exe setup
./CodexCompanion.Bridge.exe run
```

如果仓库还没有可用的 Release，开发者可以在源码目录执行 `./scripts/publish-bridge.ps1 -Version dev` 生成同样的 ZIP 包。

VPS 防火墙只需要放行 TCP 80。PostgreSQL 和 Relay 不直接暴露公网端口。

如果希望使用 GitHub Container Registry 的预构建镜像，可将启动命令替换为：

```bash
docker compose -f compose.yml -f deploy/docker-compose.images.yml up -d
```

快速模式也可以直接使用预构建镜像（仍然不需要 PostgreSQL）：

```bash
docker compose -f deploy/docker-compose.quick.yml -f deploy/docker-compose.images.yml pull
docker compose -f deploy/docker-compose.quick.yml -f deploy/docker-compose.images.yml up -d
```

首次使用 GHCR 前，请在仓库的 **Packages** 页面将 `codex-companion-relay` 和
`codex-companion-web` 设置为 **Public**；否则 VPS 拉取镜像时需要配置 GitHub Container Registry 登录凭据。

首次发布前或需要使用本地代码时，继续使用前面的 `--build` 命令。

## 模式 B：域名 HTTPS 模式

此模式适合长期运行。需要一个解析到 VPS 的域名，并确保 TCP 80/443 可以从公网访问。Caddy 会自动申请和续期证书，并代理 WebSocket。

在 VPS 执行：

```bash
cd /opt/codex-companion
cp .env.example .env
```

编辑 `.env`：

```env
POSTGRES_PASSWORD=生成一个随机长密码
PUBLIC_HOST=companion.example.com
ALLOWED_ORIGINS=companion.example.com,localhost
```

启动 HTTPS 版本：

```bash
docker compose -f compose.yml -f deploy/docker-compose.https.yml up -d --build
docker compose ps
curl https://companion.example.com/healthz
```

也可以使用一键脚本完成配置和启动：

```bash
bash scripts/install-server.sh --domain companion.example.com
```

使用 GHCR 预构建镜像时，增加镜像覆盖文件并先拉取：

```bash
docker compose -f compose.yml -f deploy/docker-compose.images.yml -f deploy/docker-compose.https.yml pull
docker compose -f compose.yml -f deploy/docker-compose.images.yml -f deploy/docker-compose.https.yml up -d
```

Windows Bridge 使用：

```powershell
CodexCompanion.Bridge.exe setup
# Relay 地址填写：wss://companion.example.com/ws/bridge
```

手机访问 `https://companion.example.com`。生产环境不要把 `wss://` 改回 `ws://`。

## 更新与日志

IP 快速模式更新：

```bash
git -C /opt/codex-companion pull --ff-only
cd /opt/codex-companion
docker compose up -d --build
docker compose logs -f relay
```

HTTPS 模式把启动命令替换为：

```bash
cd /opt/codex-companion
docker compose -f compose.yml -f deploy/docker-compose.https.yml up -d --build
```

使用预构建镜像时，脚本命令为：

```bash
bash scripts/install-server.sh --domain companion.example.com --images
```

更新前先备份 PostgreSQL 数据卷。不要删除 `codex-companion-postgres`，否则会丢失设备配对信息。

## 常见诊断

Windows 上执行：

```powershell
CodexCompanion.Bridge.exe doctor
CodexCompanion.Bridge.exe doctor --json
```

`--json` 适合安装器、自动化脚本和提交诊断信息；输出不会包含 Bridge 凭据内容。

服务器上执行：

```bash
docker compose ps
docker compose logs --tail=100 relay
docker compose logs --tail=100 web
```

## 干净环境验收

发布新版本后，建议在一台没有项目缓存的机器上按下面顺序验收：

1. VPS 使用 `bash scripts/install-server.sh`（IP 快速模式）或 `--domain <域名> --https`（HTTPS 模式）初始化，并确认 `/healthz` 返回 `200`。
2. Windows 下载 Release 中的 Bridge ZIP 或 GUI 安装器，确认无需安装 .NET SDK 即可启动；Codex CLI 仍需单独安装。
3. 运行 `setup`，确认 Relay 地址、Codex CLI 路径和诊断结果均正确；随后运行 `install-bridge.ps1`，确认 Bridge 默认处于停止状态。
4. 在手机打开网页，扫描 Bridge 终端二维码（或手动输入 8 位配对码），确认自动进入项目列表。
5. 手动执行 Bridge 启动命令，确认手机可连接；如果启用了自启动，再重启 Windows 验证它自动启动并能在 Relay 重启后自动重连。
6. 快速模式重启 Relay 后应重新配对；HTTPS 模式重启 Relay 后应保留 PostgreSQL 中的设备凭据。
