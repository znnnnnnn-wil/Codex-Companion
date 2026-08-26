# 开发与运行

## 依赖

- Windows 10/11 与正在运行、已登录的 Codex Desktop
- Codex CLI（推荐 `npm install -g @openai/codex`）
- .NET 10 SDK
- Go 1.26+
- Node.js 22+
- JDK 21+（Capacitor 8 Android Gradle 构建要求）
- Docker Desktop

如果机器没有全局 .NET 10 SDK，可在仓库根目录运行 `./scripts/use-local-dotnet.ps1`。脚本只安装到被 Git 忽略的 `.tools/dotnet`，并只修改当前终端环境。

本机探测发现 MSIX 内 `codex.exe` 为 Application Protected，普通 Bridge 进程不能启动。Bridge 会优先使用 `CODEX_EXECUTABLE`，其次 PATH 中的独立 CLI，再查找 npm npx cache。

## 启动

Terminal 1：

```powershell
docker compose up -d postgres
```

Terminal 2：

```powershell
cd services/relay
$env:DATABASE_URL='postgres://codex_companion:codex_companion_dev@127.0.0.1:5432/codex_companion?sslmode=disable'
go run ./cmd/server
```

Terminal 3：

```powershell
cd apps/bridge
$env:CODEX_COMPANION_RELAY_URL='ws://127.0.0.1:8080/ws/bridge'
dotnet run -- run
```

首次运行会显示 8 位配对码。Bridge 凭据默认保存到 `%LOCALAPPDATA%\CodexCompanion\bridge-credential.json`。

Terminal 4：

```powershell
cd apps/web
npm install
npm run dev
```

浏览器是当前推荐的使用方式：如果使用已部署的公网环境，手机直接打开部署后的 Web 地址；如果在本机开发，同一局域网手机可打开 `http://<电脑局域网IP>:5173`。公网 Smoke Test 与部署状态见 [公网部署](deployment.md)。生产环境建议配置域名，并将 Web 和 Bridge 切换到 HTTPS/WSS。

## 可选 Android App

Android App 使用同一份 `apps/web` React/Vite 构建产物，不依赖运行时公网 Web 静态站点。它不是当前个人使用的必需组件，只有需要独立 APK 或进行真机封装测试时才构建。

```powershell
cd apps/web
npm install
npm run build:android:debug
npx cap sync android
android\gradlew.bat -p android assembleDebug
```

Debug 构建通过 `.env.android-debug` 使用当前 Stage A Relay 地址，并将 Capacitor Android origin 设置为 `http://localhost`，仅在 `android/app/src/debug` 中允许 `localhost` 和该 IP 的 cleartext。这只用于当前明文 Stage A Relay，不得用于生产。Release 构建保持 `https://localhost` origin，必须提供 `VITE_RELAY_WS_URL=wss://...` 的生产环境变量；签名 keystore、密码和 `*.jks`/`*.keystore` 不得提交 Git。

```powershell
$env:VITE_RELAY_WS_URL = 'wss://relay.example.com/ws/web'
npm run build:android:release
npx cap sync android
android\gradlew.bat -p android assembleRelease
```

Capacitor Android origin 是 `https://localhost`。部署 Relay 时把 `localhost` 加入 `ALLOWED_ORIGINS`，同时保留实际 Web 调试域名的白名单。

## Bridge 探测命令

```powershell
dotnet run -- threads
dotnet run -- thread <thread-id>
dotnet run -- inspect-ui --output ..\..\docs\codex-ui-tree.txt
dotnet run -- status
dotnet run -- send <thread-id> "hello"
```

## 测试

```powershell
cd services/relay; go test ./...
cd apps/bridge; dotnet test
cd apps/web; npm test; npm run build

## Capacitor 验证

```powershell
cd apps/web
npm test
npm run lint
npm run build
npx cap sync android
android\gradlew.bat -p android assembleDebug
```
```

`scripts/e2e-mobile.mjs` 是不含假数据的协议级手机模拟器，需要实际 Relay、Bridge、配对码和真实 thread id。
