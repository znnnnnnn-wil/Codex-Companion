# Codex Companion

English | [简体中文](./README.zh-CN.md)

**Remote Codex, not your computer.**

Continue your real Windows Codex Desktop threads from any phone browser.

`Self-hosted` · `Outbound-only` · `No phone VPN` · `Minimal permissions`

[![Latest Release](https://img.shields.io/github/v/release/znnnnnnn-wil/Codex-Companion?display_name=tag&sort=semver)](https://github.com/znnnnnnn-wil/Codex-Companion/releases) [![License](https://img.shields.io/github/license/znnnnnnn-wil/Codex-Companion)](LICENSE)

[![Codex Companion demo showing Windows Codex Desktop and a phone browser continuing the same thread](docs/assets/codex-companion-demo.png)](https://znnnnnnn-wil.github.io/Codex-Companion/demo/)

[▶ Try the 15-second interactive demo](https://znnnnnnn-wil.github.io/Codex-Companion/demo/)

Codex Companion connects a mobile browser to the Codex Desktop instance already running on your Windows PC. The phone can continue the same real Codex thread through your self-hosted Relay, while the Windows Bridge keeps the computer-side connection outbound. It does not expose a remote desktop, arbitrary shell, or general-purpose file API.

## Why Codex Companion?

### Continue the real Codex thread

The phone reads and continues the thread that already exists in Codex on your Windows PC. Codex history remains the source of truth; the Web client does not create a separate conversation database.

### Remote Codex, not the whole computer

The protocol is limited to supported Codex operations. It does not provide the phone with the Windows desktop, arbitrary shell commands, arbitrary file access, or generic keyboard and mouse control.

### Outbound-only Windows connection

The Windows Bridge initiates the WebSocket connection to the Relay. Your Windows PC does not need a public IP, inbound port forwarding, or DDNS. Production deployments should use HTTPS and outbound WSS; the quick evaluation mode uses HTTP and WS.

### Self-hosted

You choose where the Relay and Web client run. The Relay authenticates devices and routes supported messages. With PostgreSQL enabled, it stores device, pairing, and credential-hash metadata—not complete prompts, responses, source code, or attachment bodies.

## Is this for me?

Codex Companion may be a good fit if you:

- use Codex Desktop on Windows;
- want to check or continue a running Codex thread from your phone;
- prefer a normal mobile browser;
- want a self-hosted Relay;
- do not want to expose your full desktop or shell; or
- do not want to configure a VPN on the phone.

It may not be the best fit if you need full remote desktop access, arbitrary shell or filesystem access from your phone, a non-Windows Codex host, or if the official Codex remote experience already fully satisfies your workflow.

## Quick Start

### Prerequisites

- Windows 10/11 with Codex Desktop installed and signed in.
- A separately installed [Codex CLI](https://github.com/openai/codex). The Bridge cannot start the application-protected executable inside the Desktop MSIX package.
- A Linux VPS reachable by the Windows PC and phone, with Git, curl, Docker Engine, and Docker Compose v2.24+.

### 1. Deploy the Relay

For a first evaluation using a public IP:

```bash
git clone https://github.com/znnnnnnn-wil/Codex-Companion.git /opt/codex-companion
cd /opt/codex-companion
bash scripts/install-server.sh --host YOUR_VPS_IP
```

This quick mode uses HTTP/WS and in-memory pairing data. Use it for evaluation, not an exposed long-running production deployment.

### 2. Set up the Windows Bridge

Download and extract the latest Windows Bridge ZIP from [Releases](https://github.com/znnnnnnn-wil/Codex-Companion/releases/latest), then run this inside the extracted directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install-bridge.ps1
```

When prompted, enter:

```text
ws://YOUR_VPS_IP/ws/bridge
```

The installer starts a temporary foreground Bridge and displays an eight-character pairing code.

### 3. Pair your phone

Open `http://YOUR_VPS_IP` in the phone browser and enter the pairing code. The code expires after ten minutes and can only be claimed once.

### 4. Start the Bridge

The installer leaves the background Bridge stopped by default. Start it explicitly:

```powershell
$bridgeControl = "$env:LOCALAPPDATA\CodexCompanion\Bridge\bridge-control.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bridgeControl -Action Start
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bridgeControl -Action Status
```

Refresh the phone browser after `Running` becomes `True`.

For a long-running deployment with a domain, TLS, WSS, and persistent PostgreSQL pairing data, follow the [complete deployment guide](docs/quickstart.md).

## How it works

```text
Phone Browser / optional Android app
                │
             HTTPS/WSS
                │
        Self-hosted Relay + Web
                │
       outbound WebSocket / WSS
                │
          Windows Bridge
                │
          Codex Desktop
```

1. The Windows Bridge initiates the outbound connection to the Relay.
2. The phone browser connects to the Relay and requests supported Codex operations.
3. The Relay authenticates and routes allowlisted messages; it does not run Codex or read the project source tree.
4. The Bridge reads real threads through `codex app-server` and sends messages through validated Codex Desktop UI Automation.
5. The real Codex thread remains the conversation source of truth. The Bridge polls active history and streams confirmed changes back to the phone.

See [Architecture](docs/architecture.md) and the [WebSocket protocol](docs/protocol.md) for implementation details.

## How is it different?

| | Codex Companion | Remote Desktop | VPN + local service |
| --- | --- | --- | --- |
| Primary scope | Supported Codex operations | Full desktop session | Whatever the service exposes |
| Continue a real Codex thread | Yes | Indirectly, through the desktop | Depends |
| Full desktop access required | No | Yes | No |
| Phone VPN required | No | Depends on the product and setup | Yes |
| Public inbound port on Windows | No | Depends on the setup | No |
| Arbitrary shell or file access | No | Desktop-level access | Depends |
| Self-hosted Relay | Yes | Depends | Not applicable |

Codex Companion is not intended to replace OpenAI's official remote experience. It is for users who specifically want a self-hosted, browser-first, outbound-relay architecture with a deliberately narrow permission boundary.

## Security model

### What Codex Companion allows

The Web-to-Bridge allowlist supports:

- listing and reading real Codex threads;
- creating a thread only within a project path already observed from real threads;
- sending text and user-selected attachments;
- reading validated generated images and available image attachments from a real thread; and
- stopping work in a uniquely identified Codex Desktop thread.

### What it intentionally does not expose

- arbitrary shell execution;
- arbitrary filesystem reads or writes;
- a full remote desktop or screen stream;
- generic keyboard and mouse control; or
- unrestricted UI Automation outside a validated Codex Desktop window.

### Connection and data model

The Bridge connects outward to the Relay. The Relay keeps request correlation and message routing in memory. PostgreSQL, when configured, stores devices, pairing state, and SHA-256 credential hashes. Offline prompts are not queued, and complete prompts, responses, source code, and attachment bodies are not persisted by the Relay.

### Pairing and credentials

Pairing uses an eight-character, single-use code with a ten-minute lifetime. Bridge and Web credentials are independently generated 256-bit tokens; the Relay stores only their hashes. Windows protects the Bridge credential with DPAPI. The optional Android app uses Keystore-backed secure storage; a normal browser uses browser local storage.

These controls reduce the exposed capability surface; they do not remove the need to secure your VPS, TLS configuration, database, browser, or Windows account. Read the complete [Security model](docs/security.md) before exposing a deployment to the internet.

## Current capabilities

- **Windows Bridge (.NET 10):** real `thread/list`, `thread/read`, and `thread/start`; validated Desktop message sending; history confirmation; status polling; stop control; attachment staging; Relay reconnect.
- **Relay (Go):** pairing, credential authentication, WebSocket routing, request correlation, connection status, explicit message allowlists, in-memory or PostgreSQL metadata storage.
- **Web client (React/Vite):** mobile-first thread list and chat, new threads for known project paths, Markdown/GFM, streaming updates, pending-to-confirmed reconciliation, attachments, generated images, stop control, and reconnect state.
- **Optional Android app (Capacitor):** the same Web client in a native wrapper with Keystore-backed credential storage.

Codex Companion currently targets Windows Codex Desktop. It does not provide provider switching, Git UI, multi-agent orchestration, enterprise authorization, or a standalone cloud AI backend. The model and account remain those used by the real Codex Desktop thread.

## Web and Android

The mobile browser is the recommended client and requires no app installation. The Android package is optional and is built from the same Web source. Android release builds require an HTTPS/WSS deployment and explicit Relay origin configuration.

See [Development](docs/development.md) for Android build commands and constraints.

## Development

The repository contains four main surfaces:

- `apps/bridge` — Windows Bridge and Codex Desktop integration;
- `services/relay` — Relay, pairing, routing, and storage;
- `apps/web` — browser client, optional Android wrapper, and product Demo;
- `deploy` — Docker Compose, Nginx, and Caddy deployment configuration.

Common checks:

```powershell
cd apps/web
npm test
npm run lint
npm run build
```

```powershell
cd services/relay
go test ./...
```

```powershell
dotnet test CodexCompanion.slnx
```

See the [development guide](docs/development.md) for local service startup, toolchain requirements, Android builds, and diagnostic commands.

## FAQ

### Does Codex Companion run its own AI model?

No. It does not configure a separate backend model or require an OpenAI API key in the Relay. Work continues in the real Codex Desktop thread on the Windows PC, using that Codex account and thread configuration.

### Does the phone need a VPN?

No. The phone and Windows PC only need network access to the Relay you deployed. Production use should expose the Relay through HTTPS/WSS.

### Does the Relay store my conversations or source code?

The current Relay implementation does not persist complete prompts, responses, source code, or attachment bodies. It does process routed WebSocket messages in memory. With PostgreSQL enabled, it stores device, pairing, and credential-hash metadata.

### Can I use it as Remote Desktop or SSH?

No. Those capabilities are intentionally outside the protocol.

### Is Android required?

No. A normal phone browser is the recommended entry point. Android is an optional wrapper around the same Web client.

## Documentation

- [Quick deployment](docs/quickstart.md)
- [Public deployment and acceptance notes](docs/deployment.md)
- [Security model](docs/security.md)
- [Architecture and source of truth](docs/architecture.md)
- [WebSocket protocol](docs/protocol.md)
- [Development](docs/development.md)
- [Codex Desktop integration research](docs/codex-research.md)

## License

Codex Companion is available under the [MIT License](LICENSE).
