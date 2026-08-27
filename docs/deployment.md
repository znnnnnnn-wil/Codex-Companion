# 公网部署与验收

从零部署请先阅读 [快速部署](quickstart.md)。本页保留 Stage A 验收环境和生产边界，便于排查已部署实例。

## Stage A 示例环境

- 服务器：Ubuntu 24.04 LTS。
- 部署目录：`/opt/codex-companion`。
- 公网 Web：`http://<server-host>/`。
- WebSocket：`ws://<server-host>/ws/web` 与 `ws://<server-host>/ws/bridge`。
- 健康检查：`http://<server-host>/healthz`。

Compose 当前运行 PostgreSQL、Relay 和 Nginx Web 三个容器，均设置 `restart: unless-stopped`。Nginx Web 是浏览器入口；Android App 是可选封装，不是当前使用的必需组件。只有 80 端口由容器发布；PostgreSQL 5432 和 Relay 8080 仅位于私有 Docker 网络。服务器防火墙只允许 22、80、443。

生产配置位于服务器 `.env`，权限为仅 root 可读写；数据库使用服务器生成的随机密码。项目不需要也没有虚构 JWT 或 pairing secret 配置项。

## Windows Bridge

Bridge 通过环境变量连接公网，URL 不写入源码：

```powershell
$env:CODEX_COMPANION_RELAY_URL='ws://<server-host>/ws/bridge'
dotnet run --project apps/bridge -- run
```

Bridge credential 由 Windows DPAPI 保护。首次运行生成一次性、十分钟有效的配对码；Web claim 后，Relay 只在 PostgreSQL 中保存 256-bit credential 的 hash。

## 2026-08-25 真实验收

- Web 公网访问和 Relay health：成功。
- Bridge 公网连接、pairing、认证：成功。
- 真实 `thread/list` / `thread/read`：成功，共读取 175 个真实 task。
- Desktop task 选择和 UI Automation 发送：成功。
- 真实 thread 写入确认：成功。
- Codex 精确回复 `PUBLIC_E2E_OK_3`：成功。
- `thread.updated` 增量与最终真实历史回读：成功。
- TXT/PNG 附件经 Desktop 原生选择窗口上传并由真实 Codex 读取：成功。
- 三张真实图片经公网手机协议上传、真实 thread 确认并由 Codex 回复 `PUBLIC_ATTACHMENT_E2E_V6_OK`：成功。
- 电脑 Codex 已生成图片按需同步到手机：成功，公网实测 PNG Base64 3,230,840 字符。
- 运行中任务从 Companion 触发 Desktop“停止”并恢复 idle：成功。
- Relay 重启后 Bridge 使用原凭据自动重连：成功，退避从 1 秒开始。
- 500×844 移动浏览器视口：成功，无横向溢出。

## 浏览器使用方式

当前个人测试无需数据线、Android Studio、域名或 VPN。启动 Bridge 后，在手机浏览器打开：

```text
http://<server-host>
```

输入 Bridge 显示的配对码即可。浏览器使用的网页 Origin 必须加入 Relay 白名单。

## HTTPS/WSS

当前没有域名，Stage A 使用 HTTP/WS。Android Debug APK 只对白名单 IP 开放 cleartext；Release 必须配置域名和 WSS。Capacitor WebView 的 Origin 是 `https://localhost`，Relay 的 `ALLOWED_ORIGINS` 必须显式加入 `localhost`，不能配置 `*`。提供解析到服务器的域名后，再配置 TLS 并把 WebSocket 切换到 WSS。
