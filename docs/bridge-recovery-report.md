# Bridge 启动、认证与配对恢复修复报告

日期：2026-09-07。仓库：`znnnnnnn-wil/Codex-Companion`。修改已在本地实现并验证，未推送、发布或修改真实部署及现有用户凭据。

## 1. 根因分析

1. `BridgeRelayClient` 发送 `device.hello` 后不等待认证结果，立即发送状态、启动轮询；认证拒绝没有转化成明确的生命周期状态。
2. `doctor` 仅检测凭据文件存在和 WebSocket 握手成功，无法发现 Relay 已遗忘本地设备身份。
3. 模式 A 使用内存存储。Relay 重启后，本地 DPAPI 凭据仍存在，服务端设备和凭据哈希却已经消失。
4. 控制脚本将进程存在/计划任务 Running 等同于 Bridge 可用；后台没有可靠的认证及配对状态来源。
5. 手工恢复和安装脚本各自解析凭据路径，未统一使用有效配置、环境变量覆盖；安装过程还把“文件生成”误作“手机配对完成”。计划任务不继承当前 PowerShell 窗口的环境变量，也可能导致前后台使用不同配置。
6. 模式 A 文档没有显式选择 `.env`，部分 `ps`、更新和日志命令甚至没有选择快速模式 Compose 文件。
7. Relay 原来将凭据不匹配与存储服务故障都返回为认证失败，无法安全决定是否需要重新配对。

## 2–3. 修改文件及各文件职责

| 文件 | 修改内容 |
|---|---|
| `apps/bridge/Program.cs` | 新增 `pair`、`status --runtime` 和安装器使用的只读凭据存在检查；doctor 检查实际凭据并执行无会话认证探测；run 输出启动进度；处理配置、凭据和重复启动错误。原 `status` 的 Desktop JSON 保持不变。 |
| `apps/bridge/Configuration/BridgeConfiguration.cs` | 统一加载配置、环境变量、显式启动参数；将相对凭据路径解析到配置文件目录。配置文件格式不变。 |
| `apps/bridge/Pairing/BridgeCredentialStore.cs` | 区分缺失、格式损坏和 DPAPI 解密失败；暴露统一有效路径；提供唯一名称的备份/重置；保持既有 DPAPI 格式及 entropy，清零临时明文。 |
| `apps/bridge/Relay/RelayHandshake.cs`（新增） | 共用认证确认、短连接探测、配对创建和二维码显示；限制握手消息大小；区分明确拒绝、认证服务故障和旧 Relay 不支持。 |
| `apps/bridge/Relay/BridgeRelayClient.cs` | 已有凭据等待认证确认；首次配对及配对完成状态更新；明确拒绝进入 PairingRequired；网络/服务故障保留凭据并重连；增加 WebSocket 保活超时。 |
| `apps/bridge/Runtime/BridgeRuntime.cs`（新增） | 每个有效凭据路径的进程独占锁、JSON 状态、心跳、PID/启动时间检查、配置匹配检查；Windows 原子替换与短暂文件占用重试。 |
| `scripts/bridge-control.ps1` | Status 读取运行状态并保留进程检测兜底；Start 有限等待并分别报告 Ready、配对、重连、失败；把显式环境变量覆盖传入计划任务参数。保留 UTF-8 BOM。 |
| `scripts/install-bridge.ps1` | 使用 Bridge 自己的有效路径判断，首次安装调用 `pair`，等待真实手机配对完成，不再手工等待 Enter 或凭据文件生成。保留 UTF-8 BOM。 |
| `services/relay/internal/websocket/server.go` | 新增只读认证探测分支；为选择接收确认的 Bridge 返回认证确认；服务故障返回 AUTH_UNAVAILABLE；限制认证处理时间。 |
| `services/relay/internal/pairing/service.go` | 暴露手机是否已经完成配对的查询。 |
| `services/relay/internal/storage/store.go` | 增加 `IsPaired` 存储接口。 |
| `services/relay/internal/storage/memory.go` | 通过设备是否存在 Web 凭据判断是否完成配对。 |
| `services/relay/internal/storage/postgres.go` | 通过既有 `devices.paired_at` 查询配对状态，无需数据库迁移。 |
| `scripts/install-server.sh` | 所有模式显式传入绝对 `.env` 路径；错误提示正确引用含空格的路径。 |
| `deploy/docker-compose.quick.yml` | 注明正确启动命令和内存配对数据的重启恢复方式；不改变模式 A 的 HTTP/WS、服务或网络布局。 |
| `docs/quickstart.md` | 更新状态说明、正式重新配对流程、升级顺序、诊断和全部模式相关 Compose 命令，删除默认凭据文件手工删除流程。 |
| `README.md` | 将可用标准改为 Ready/认证/配对状态，增加模式 A 显式 Compose 参数和配对恢复说明。 |
| `README.zh-CN.md` | 同步中文说明。 |
| `docs/protocol.md` | 记录可选认证确认和无副作用诊断消息、错误语义及旧 Relay 的兼容行为。 |
| `apps/bridge/tests/CodexCompanion.Bridge.Tests/BridgeLifecycleTests.cs`（新增） | 凭据、配置、生命周期、状态并发、回环 WebSocket、pair 命令和 doctor JSON 回归测试。 |
| `services/relay/internal/websocket/server_test.go`（新增） | 使用真实回环 HTTP/WebSocket 服务测试认证、探测不干扰已有会话、服务端失忆与存储故障区分、未完成配对的认证确认。 |
| `services/relay/internal/storage/postgres_integration_test.go` | 扩展 PostgreSQL 配对前后的 `IsPaired` 断言。 |
| `scripts/test-bridge-control.ps1`（新增） | 使用无副作用的 OS 模拟测试真实控制函数和动作分支，覆盖状态、Start 和环境变量传递。 |
| `scripts/test-install-server.sh`（新增） | 模拟 Docker/git/curl，覆盖绝对 env-file、含空格目录、快速构建、镜像和 HTTPS 命令。 |
| `.github/workflows/smoke.yml` | 加入 Bridge 测试、双 PowerShell 控制脚本测试、Relay 竞态测试、安装器命令测试，以及显式 env-file 的 Compose 校验。 |
| `docs/bridge-recovery-report.md`（本报告） | 记录修复内容、验证证据、使用方法和剩余验证范围。 |

## 4. 新的状态模型

| 状态 | 含义 |
|---|---|
| Starting | Bridge 进程已取得运行锁，正在初始化本机组件。 |
| Connecting | 正在建立 Relay 连接。 |
| Authenticating | WebSocket 已连接，身份尚未确认。 |
| PairingRequired | 凭据缺失/损坏/被拒绝，或者身份已认证但手机尚未完成配对。 |
| Ready | Bridge 已连接、身份得到 Relay 确认且手机配对已完成。 |
| Reconnecting | 网络、认证确认或 Relay 认证服务暂不可用，保留凭据等待重试。 |
| StartupFailed | 本机初始化失败；退出后查询显示 Stopped，并保留最后错误。 |
| Stopped | 原运行进程已退出或 PID 已被复用，清除连接及认证标记。 |
| Unavailable | 状态不可读、过期或与当前 Relay 配置不匹配，不报告 Ready。 |

机器状态包含 `processRunning`、`relayReachable`（可为空）、`connected`、`authenticated`、`pairingRequired`、`ready`、`state`、`lastError`、`lastConnectedAt`、PID、进程启动时间及状态更新时间。控制脚本另外给出 Installed、ProcessIds、TaskState 和 Autostart。

`authenticated=true` 与 `pairingRequired=true` 可以同时成立：Relay 接受设备身份，但手机尚未完成配对。只有 `ready=true` 才代表 Bridge 会话可用。它不保证当前 Desktop 已登录或每条消息都能成功发送，后者仍由 Desktop 状态和实际请求验证。

运行状态保存在实际凭据旁的 `.runtime.json`；`.runtime.json.lock` 的独占文件句柄用于防止同一路径的 run/pair 并发写入。句柄在异常退出后由操作系统释放。心跳间隔 3 秒；读取时检查 PID、进程启动时间、15 秒新鲜度及 Relay 配置指纹。文件采用原子替换，针对 Windows 短暂占用进行有限重试。状态文件不含 token、credential 或配对码。

## 5. stale credential 的处理

- 实际 `device.hello` 收到明确的 UNAUTHORIZED / UNKNOWN_DEVICE / INVALID_CREDENTIAL / PAIRING_REQUIRED：关闭当前连接，进入 PairingRequired，打印 Stop → pair 提示，保留旧凭据。后台进程继续提供可查询状态，不再反复提交被拒凭据。
- 网络中断、超时或 AUTH_UNAVAILABLE：进入 Reconnecting，指数退避重试，不删除、不备份、不自动更换凭据。
- 不自动创建新设备身份。重新配对会改变设备和手机凭据，因此由用户显式调用 `pair`。
- 模式 A 仍采用内存存储；此次修复使丢失配对状态可被识别和恢复，不把它改为持久化模式。

## 6. 新配对 CLI 用法

```powershell
$bridgeControl = "$env:LOCALAPPDATA\CodexCompanion\Bridge\bridge-control.ps1"
$bridge = "$env:LOCALAPPDATA\CodexCompanion\Bridge\CodexCompanion.Bridge.exe"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bridgeControl -Action Stop
& $bridge pair
# 手机扫描二维码、打开配对 URL 或输入 8 位配对码。
# Relay 确认手机配对成功后，pair 自动退出。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bridgeControl -Action Start
```

`pair` 加载有效配置，取得同凭据路径的独占锁，先连接 Relay，再把旧文件移动为同目录唯一的 `.backup-*` 文件，然后创建新配对。连接失败前不移动旧文件。备份保留原 DPAPI 加密内容，不自动清理。

`pair` 不启动 Codex app-server，也不成为长期 Bridge。Ctrl+C 可取消；配对等待有约 10 分钟上限。出现已有进程时返回 `BRIDGE_ALREADY_RUNNING` 并明确提示停止。新 `pair` 与 `run` 的同路径并发由相同锁保护。

## 7. doctor 新检查

- 凭据路径统一来自有效配置；区分 CREDENTIAL_MISSING、CREDENTIAL_INVALID（JSON/字段/Base64）、CREDENTIAL_DECRYPT_FAILED、CREDENTIAL_UNREADABLE。
- 单独报告 WebSocket 网络可达；该 PASS 不再代表身份已通过。
- 使用 `device.auth.check` → `device.auth.result` 验证当前 DeviceId + credential。
- 明确报告 credential 被拒、认证服务暂不可用、认证成功及手机未完成配对。
- 旧 Relay 不支持探测、无确认、超时和连接中断均报告认证未知/失败，不伪造认证 PASS，也不改变凭据。
- 探测分支在 Hub 注册前结束，不替换活跃 Bridge，不创建长期会话，不更改服务端设备和配对数据，不广播上下线事件。
- `doctor --json` 保留机器输出，并使用非零退出码报告失败。

## 8. bridge-control.ps1 的变化

Status 分开显示进程、网络、连接、认证、配对和 Ready；无法取得可信状态时显示 Unavailable，认证等字段保持未知，不退回到 Running=True 的成功暗示。

Start 在启动/发现进程后进行约 15 秒的有限状态等待。Ready 时报告认证确认；需要配对时提供 Stop/pair 指令；超时仍处于初始化、重连或未知状态时给出说明和 doctor 指令，保持进程运行；进程启动失败或退出时报错。

显式环境变量覆盖被转换为计划任务的启动参数，避免当前 PowerShell 与后台进程读取不同配置。自动启动使用最近注册/启动任务时保存的覆盖参数；修改覆盖后应 Stop → Start。长期配置优先保存在 config.json。

## 9. 模式 A Compose 修复

README、中文 README、quickstart 中的快速模式启动、状态、日志、更新及镜像命令都显式指定环境文件与快速模式文件。以下命令从仓库根目录执行：

```bash
cd /opt/codex-companion
docker compose --env-file .env -f deploy/docker-compose.quick.yml up -d --build
docker compose --env-file .env -f deploy/docker-compose.quick.yml ps
docker compose --env-file .env -f deploy/docker-compose.quick.yml logs -f relay
```

服务器安装脚本在进入安装目录后，将绝对 `$PWD/.env` 加入参数数组。模式 B 和镜像覆盖保留正确的文件组合。

## 10. 新增/修改测试覆盖

- A：已有凭据由 Relay 接受，进入 Ready，不发送 pairing.create。
- B：凭据缺失，发送 pairing.create，保存 DPAPI 凭据，等待配对确认后 Ready。
- C：已保存凭据收到明确拒绝，变为 PairingRequired，提示恢复，文件字节保持不变。
- D：连接失败或认证存储故障，Reconnecting，不重置；存储故障保留网络已达信息。
- E：损坏 JSON、缺少字段、非法 Base64 和 DPAPI 解密失败；doctor JSON 提供明确错误且不出现 INTERNAL_ERROR。
- F：配置相对自定义路径、环境变量覆盖、pair 实际备份路径、doctor 实际读取路径。
- G：已认证、需配对、重连、停止、PID 复用、心跳过期、配置改变和状态并发读写。
- 正式 pair 命令展示 8 位码及 URL，等待 pairing.completed 自动退出；输出不含凭据。
- 诊断探测不替换已有 Bridge，不改变 Web 上下线事件；旧 Relay 通用拒绝不能被误判为 stale credential。
- 双 PowerShell 控制脚本、带 BOM 的打包脚本及服务器安装命令构造。

## 11. 实际执行的验证命令

在仓库根目录使用本地已安装的 SDK（系统 PATH 中的 dotnet 只有运行时，没有 SDK）：

```powershell
.\.tools\dotnet\dotnet.exe test apps/bridge/tests/CodexCompanion.Bridge.Tests --no-restore --verbosity quiet
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test-bridge-control.ps1
pwsh -NoProfile -File scripts/test-bridge-control.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test-bridge-package.ps1 -PackageDirectory .tmp-verification/package
pwsh -NoProfile -File scripts/test-bridge-package.ps1 -PackageDirectory .tmp-verification/package
```

`.tmp-verification/package` 是按发布脚本的 UTF-8 BOM 转换步骤制作的三个控制/安装脚本副本，用于脚本语法与编码验收；不是完整 Release 包。

Relay 在 `services/relay` 下执行：

```powershell
$env:GOCACHE='E:\codexDestop\.tools\go-build'
go test -v ./...
go vet ./...
go test -race ./...
```

服务器脚本在本机 Git Bash 中执行（为 Git Bash 设置 `/usr/bin:/bin` 工具路径）：

```bash
bash -n scripts/install-server.sh
bash scripts/test-install-server.sh
```

Compose 使用隔离的测试环境文件，不读取真实部署 `.env`：

```bash
docker compose --env-file .tmp-verification/compose.env -f deploy/docker-compose.quick.yml config --quiet
docker compose --env-file .tmp-verification/compose.env -f deploy/docker-compose.quick.yml -f deploy/docker-compose.images.yml config --quiet
docker compose --env-file .tmp-verification/compose.env -f compose.yml -f deploy/docker-compose.https.yml config --quiet
```

另外执行 `git diff --check`。开发过程中针对状态文件替换竞态运行了定向回归，修复后再执行完整测试套件。

## 12. 测试结果

| 验证 | 最终结果 |
|---|---|
| Bridge .NET 测试 | 53/53 通过；原有 27 项保持通过，本次增加 26 个测试用例。 |
| Relay Go 测试 | 13 个顶层测试通过（其中包含存储故障/Relay 重启两个子场景）；1 项 PostgreSQL 集成测试跳过。 |
| Go vet | 通过。 |
| Windows PowerShell 5.1 控制脚本测试 | 通过。 |
| PowerShell 7 控制脚本测试 | 通过。 |
| 双 PowerShell 打包脚本语法/BOM 检查 | 通过。 |
| 安装脚本 Bash 语法及模拟集成测试 | 通过；模拟测试不会启动容器或连接真实服务。 |
| 模式 A、A 镜像版、B Compose 配置 | 全部通过。 |
| Git diff 空白检查 | 通过。 |
| 本机 Go race | 未执行成功：当前 Go 配置没有 CGO；已加入 Linux CI。 |
| 真实 Docker/PostgreSQL 集成 | 未验证：Docker 引擎管道访问被拒绝，且未设置 TEST_DATABASE_URL。 |

## 13. 兼容性、升级顺序和剩余验证

1. **先升级 Relay，再升级 Bridge。** 确认协议是可选扩展，旧 Bridge/Web 可继续连接新 Relay；新 Bridge 连接没有确认能力的旧 Relay 时不能标记已认证，需要升级服务端。
2. 不改变已有 credential JSON、DPAPI entropy 或作用域，不因为升级自动重置；模式 A 继续 ws://，模式 B 继续 wss://。没有数据库迁移。
3. 原 `status` 保留 Desktop JSON，新的机器运行状态使用 `status --runtime`。控制脚本的旧 Running 字段改为 ProcessRunning 等明确字段，依赖旧文本字段的外部脚本需要调整。
4. 相对 CredentialPath 现在以配置文件目录为基准，避免前后台工作目录差异。已有使用相对路径的用户应核对原路径，或在 config.json 中改为原文件的绝对路径；绝对自定义路径无需迁移。
5. 模式 A 的服务器重启仍需要重新配对。此修复改善识别与恢复，不提供模式 A 配对状态持久化。
6. 本地文件锁保护同一实际凭据路径的新版本 run/pair。升级前应停止旧版 Bridge；旧版进程不具备新运行状态/锁协议。后台状态缺失时不会假定其已经认证。
7. 当前验证未控制真实 Codex Desktop、未进行手机实机发送、未在 Windows 10/11 各自全新系统安装，也未构建完整 GUI 安装器/Release ZIP。CI 测试已配置，但尚未在远程 CI 执行。
8. PostgreSQL 配对状态查询已经编译并加入集成断言；由于本机数据库不可用，没有把它报告为实测通过。真实 Docker 部署与端到端手机验收仍需在可访问服务环境中执行。
