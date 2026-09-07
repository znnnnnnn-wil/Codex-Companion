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
docker compose --env-file .env -f deploy/docker-compose.quick.yml up -d --build
docker compose --env-file .env -f deploy/docker-compose.quick.yml ps
docker compose --env-file .env -f deploy/docker-compose.quick.yml logs -f relay
# 查看日志后按 Ctrl+C 返回，再检查健康接口：
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
5. 在手机打开 `http://你的VPS公网IP`，输入配对码；收到配对完成确认后命令自动退出。安装完成后 Bridge 默认保持停止，由你手动启动。

安装脚本会注册一个“按需运行”的后台任务，但不会默认加入 Windows 登录启动。下面的命令都可以在任意 PowerShell 窗口执行。

先设置控制脚本路径（当前 PowerShell 窗口后续命令会使用这个变量）：

```powershell
$bridgeControl = "$env:LOCALAPPDATA\CodexCompanion\Bridge\bridge-control.ps1"
```

启动 Bridge（后台运行，不显示黑窗口）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bridgeControl -Action Start
```

停止 Bridge：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bridgeControl -Action Stop
```

查看 Bridge 当前状态：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bridgeControl -Action Status
```

需要开机自动启动时，主动启用：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bridgeControl -Action EnableAutostart
```

关闭开机自动启动（不会卸载 Bridge，仍然可以手动启动）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bridgeControl -Action DisableAutostart
```

### 运行状态与重新配对

`ProcessRunning=True` 仅表示进程存在。确认 `Connected=True`、`Authenticated=True`、`PairingRequired=False`、`Ready=True` 后再使用手机网页。`RelayReachable` 为空表示尚未确认网络，`Unavailable` 表示没有可信的最新运行状态。`Ready` 表示 Bridge 会话已认证并完成手机配对；Codex Desktop 是否已登录、能否发送消息仍需检查 Desktop/doctor。

```powershell
$bridge = "$env:LOCALAPPDATA\CodexCompanion\Bridge\CodexCompanion.Bridge.exe"
& $bridge status --runtime
& $bridge doctor
```

`status --runtime` 输出机器可读 JSON；原来的 `status` 仍输出 Codex Desktop 状态。显式的配置环境变量覆盖会传入计划任务启动参数；自动启动会沿用最近注册/启动任务时的覆盖值，修改覆盖后请 Stop → Start。`Start` 最多等待约 15 秒确认运行状态，分别报告 Ready、需要配对、重连中或启动失败；临时网络失败不会杀死 Bridge。

模式 A Relay 重启会丢失内存中的设备身份。此时旧凭据会被拒绝，Bridge 进入 `PairingRequired` 并保留原文件，不会无限提交失效凭据，也不会自动更换设备身份。需要重新配对时执行：

```powershell
$bridgeControl = "$env:LOCALAPPDATA\CodexCompanion\Bridge\bridge-control.ps1"
$bridge = "$env:LOCALAPPDATA\CodexCompanion\Bridge\CodexCompanion.Bridge.exe"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bridgeControl -Action Stop
& $bridge pair
```

`pair` 自动加载有效配置和 `CODEX_COMPANION_CREDENTIAL_PATH` 覆盖，定位实际凭据（相对路径以配置文件所在目录为基准）。它先取得独占锁并连接 Relay，然后将旧的 DPAPI 加密凭据移动为同目录的唯一 `.backup-*` 文件，再生成新的 8 位配对码、URL 和二维码。无需手工查找或删除 JSON；旧备份不会自动删除。连接失败前不会移动旧凭据。已有前台或后台 `run/pair` 时，会提示先停止。

在手机打开配对 URL，或输入配对码；收到 Relay 的 `pairing.completed` 确认后，`pair` 自动退出。按 Ctrl+C 可以取消，过期或中断后可重试 `pair`。重新配对会创建新的设备身份，旧的手机凭据需要用新配对码替换。完成后启动后台 Bridge：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bridgeControl -Action Start
```

首次 `run` 没有凭据时也会显示配对码并等待手机配对；`run` 是长期前台进程，按 Ctrl+C 停止。后台首次启动时，控制脚本会提示需要配对，使用上述 `Stop` → `pair` → `Start` 完成恢复。

升级时请先更新 Relay 再更新 Bridge。认证确认和无副作用探测是可选扩展，旧 Bridge/Web 仍可连接新 Relay。新 Bridge 遇到没有认证确认/探测能力的旧 Relay 时会报告认证未知并要求升级，绝不会把握手成功当成认证成功或据此删除凭据。

GUI 安装器会在开始菜单创建“启动 Bridge”“停止 Bridge”“Bridge 状态”“Bridge 配置”和“Bridge 诊断”快捷方式；“登录 Windows 后自动启动”和“安装完成后启动”默认不勾选。

Bridge 发布包中的安装脚本使用带 BOM 的 UTF-8 编码，同时支持 Windows PowerShell 5.1 和 PowerShell 7。`v0.1.1` 的 ZIP 安装脚本缺少 BOM，在 Windows PowerShell 5.1 中可能报告 `TerminatorExpectedAtEndOfString`。遇到此错误时请升级到修复后的版本；升级前也可以在解压目录直接完成配置和配对：

```powershell
./CodexCompanion.Bridge.exe setup
./CodexCompanion.Bridge.exe run
```

如果仓库还没有可用的 Release，开发者可以在源码目录执行 `./scripts/publish-bridge.ps1 -Version dev` 生成同样的 ZIP 包。

VPS 防火墙只需要放行 TCP 80。PostgreSQL 和 Relay 不直接暴露公网端口。

快速模式可以使用 GitHub Container Registry 的预构建镜像（仍然不需要 PostgreSQL）：

```bash
docker compose --env-file .env -f deploy/docker-compose.quick.yml -f deploy/docker-compose.images.yml pull
docker compose --env-file .env -f deploy/docker-compose.quick.yml -f deploy/docker-compose.images.yml up -d
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
docker compose --env-file .env -f compose.yml -f deploy/docker-compose.https.yml up -d --build
docker compose --env-file .env -f compose.yml -f deploy/docker-compose.https.yml ps
curl https://companion.example.com/healthz
```

也可以使用一键脚本完成配置和启动：

```bash
bash scripts/install-server.sh --domain companion.example.com
```

使用 GHCR 预构建镜像时，增加镜像覆盖文件并先拉取：

```bash
docker compose --env-file .env -f compose.yml -f deploy/docker-compose.images.yml -f deploy/docker-compose.https.yml pull
docker compose --env-file .env -f compose.yml -f deploy/docker-compose.images.yml -f deploy/docker-compose.https.yml up -d
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
docker compose --env-file .env -f deploy/docker-compose.quick.yml up -d --build
docker compose --env-file .env -f deploy/docker-compose.quick.yml logs -f relay
```

HTTPS 模式把启动命令替换为：

```bash
cd /opt/codex-companion
docker compose --env-file .env -f compose.yml -f deploy/docker-compose.https.yml up -d --build
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

`--json` 适合安装器、自动化脚本和提交诊断信息；输出不会包含 Bridge 凭据内容。doctor 分别检查凭据不存在、JSON/Base64 损坏、DPAPI 解密失败、网络不可达、Relay 拒绝凭据以及认证成功。探测不会注册 Bridge 会话或影响现有连接；存储故障、超时及旧 Relay 不支持探测都不会被归类为失效凭据。

运行状态保存在实际凭据旁的 `.runtime.json`，由 `run/pair` 独占 `.runtime.json.lock`。状态每 3 秒更新，读取时验证 PID、进程启动时间和 15 秒时效；进程已退出或 PID 被复用时不会返回旧的已认证状态。状态文件不包含 token、credential 或配对码。

服务器上在 `/opt/codex-companion` 目录执行（模式 A）：

```bash
docker compose --env-file .env -f deploy/docker-compose.quick.yml ps
docker compose --env-file .env -f deploy/docker-compose.quick.yml logs --tail=100 relay
docker compose --env-file .env -f deploy/docker-compose.quick.yml logs --tail=100 web
```

## 干净环境验收

发布新版本后，建议在一台没有项目缓存的机器上按下面顺序验收：

1. VPS 使用 `bash scripts/install-server.sh`（IP 快速模式）或 `--domain <域名> --https`（HTTPS 模式）初始化，并确认 `/healthz` 返回 `200`。
2. Windows 下载 Release 中的 Bridge ZIP 或 GUI 安装器，确认无需安装 .NET SDK 即可启动；Codex CLI 仍需单独安装。
3. 运行 `setup`，确认 Relay 地址、Codex CLI 路径和诊断结果均正确；随后运行 `install-bridge.ps1`，确认 Bridge 默认处于停止状态。
4. 在手机打开网页，扫描 Bridge 终端二维码（或手动输入 8 位配对码），确认自动进入项目列表。
5. 手动执行 Bridge 启动命令，确认手机可连接；如果启用了自启动，再重启 Windows 验证它自动启动并能在 Relay 重启后自动重连。
6. 快速模式重启 Relay 后应重新配对；HTTPS 模式重启 Relay 后应保留 PostgreSQL 中的设备凭据。

HTTPS 模式的诊断命令应使用 `docker compose --env-file .env -f compose.yml -f deploy/docker-compose.https.yml ps` 和相同文件组合的 `logs`。所有 Compose 命令均从仓库根目录执行，显式加载 `.env`；不要省略模式对应的 `-f` 参数。
