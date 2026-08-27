#!/usr/bin/env bash
set -Eeuo pipefail

REPO_URL="${REPO_URL:-https://github.com/znnnnnnn-wil/Codex-Companion.git}"
INSTALL_DIR="${INSTALL_DIR:-/opt/codex-companion}"
DOMAIN=""
PUBLIC_HOST_VALUE="${PUBLIC_HOST:-}"
MODE="quick"
USE_IMAGES="false"

usage() {
  cat <<'EOF'
Usage: install-server.sh [options]

  --domain DOMAIN   Use HTTPS mode with this DNS name
  --host HOST       Public IP/host used for IP mode and allowed origins
  --https           Use HTTPS mode with PUBLIC_HOST or --domain
  --images          Pull GHCR images instead of building locally
  --dir PATH        Installation directory (default: /opt/codex-companion)
  --help            Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --domain) DOMAIN="${2:?--domain requires a value}"; shift 2 ;;
    --host) PUBLIC_HOST_VALUE="${2:?--host requires a value}"; shift 2 ;;
    --https) MODE="https"; shift ;;
    --images) USE_IMAGES="true"; shift ;;
    --dir) INSTALL_DIR="${2:?--dir requires a value}"; shift 2 ;;
    --help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

command -v docker >/dev/null || { echo "Docker is required. Install Docker Engine first." >&2; exit 1; }
docker compose version >/dev/null || { echo "Docker Compose v2 is required." >&2; exit 1; }
command -v git >/dev/null || { echo "Git is required." >&2; exit 1; }
command -v curl >/dev/null || { echo "curl is required." >&2; exit 1; }

if [[ ! -d "$INSTALL_DIR/.git" ]]; then
  mkdir -p "$(dirname "$INSTALL_DIR")"
  git clone "$REPO_URL" "$INSTALL_DIR"
fi
cd "$INSTALL_DIR"

if [[ ! -f .env ]]; then
  cp .env.example .env
fi

if ! grep -q '^POSTGRES_PASSWORD=' .env || grep -q 'replace-with-random-production-password' .env; then
  if command -v openssl >/dev/null; then
    password="$(openssl rand -hex 32)"
  else
    password="$(od -An -N32 -tx1 /dev/urandom | tr -d ' \n')"
  fi
  sed -i "s#^POSTGRES_PASSWORD=.*#POSTGRES_PASSWORD=$password#" .env
fi

VPS_IP="${PUBLIC_HOST_VALUE:-${VPS_IP:-$(hostname -I | awk '{print $1}')}}"
[[ -n "$VPS_IP" ]] || { echo "Unable to determine the server host. Pass --host PUBLIC_IP." >&2; exit 2; }
if [[ "$MODE" == "https" || -n "$DOMAIN" ]]; then
  MODE="https"
  DOMAIN="${DOMAIN:-${PUBLIC_HOST:-}}"
  [[ -n "$DOMAIN" ]] || { echo "HTTPS mode requires --domain or PUBLIC_HOST." >&2; exit 2; }
  if grep -q '^PUBLIC_HOST=' .env; then
    sed -i "s#^PUBLIC_HOST=.*#PUBLIC_HOST=$DOMAIN#" .env
  else
    printf '\nPUBLIC_HOST=%s\n' "$DOMAIN" >> .env
  fi
  if grep -q '^ALLOWED_ORIGINS=' .env; then
    sed -i "s#^ALLOWED_ORIGINS=.*#ALLOWED_ORIGINS=$DOMAIN,localhost#" .env
  else
    printf 'ALLOWED_ORIGINS=%s,localhost\n' "$DOMAIN" >> .env
  fi
else
  if grep -q '^ALLOWED_ORIGINS=' .env; then
    sed -i "s#^ALLOWED_ORIGINS=.*#ALLOWED_ORIGINS=$VPS_IP#" .env
  else
    printf 'ALLOWED_ORIGINS=%s\n' "$VPS_IP" >> .env
  fi
fi

compose_args=(-f compose.yml)
if [[ "$MODE" == "https" ]]; then
  compose_args+=(-f deploy/docker-compose.https.yml)
else
  compose_args=(-f deploy/docker-compose.quick.yml)
fi
if [[ "$USE_IMAGES" == "true" ]]; then
  compose_args+=(-f deploy/docker-compose.images.yml)
fi

if [[ "$USE_IMAGES" == "true" ]]; then
  docker compose "${compose_args[@]}" pull
  docker compose "${compose_args[@]}" up -d
else
  docker compose "${compose_args[@]}" up -d --build
fi

health_url="http://127.0.0.1/healthz"
if [[ "$MODE" == "https" ]]; then
  health_url="https://$DOMAIN/healthz"
fi
for _ in {1..20}; do
  if curl --fail --silent --show-error "$health_url" >/dev/null; then
    echo "Codex Companion is ready: $health_url"
    exit 0
  fi
  sleep 3
done
echo "Services started but health check failed. Run: docker compose ${compose_args[*]} logs --tail=100" >&2
exit 1
