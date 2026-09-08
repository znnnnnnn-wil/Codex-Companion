#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export ALLOWED_ORIGINS="localhost,127.0.0.1"
export POSTGRES_PASSWORD="smoke-test-password-please-discard"

cleanup() {
  docker compose -f "$ROOT/compose.yml" down -v --remove-orphans >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker compose -f "$ROOT/compose.yml" up -d --build
for _ in {1..30}; do
  if curl --fail --silent http://127.0.0.1/healthz >/dev/null; then
    for path in / /index.html /sw.js /registerSW.js /version.json /manifest.webmanifest; do
      curl --fail --silent --head "http://127.0.0.1$path" | tr -d '\r' | grep -qi '^Cache-Control: no-store'
    done
    asset=$(curl --fail --silent http://127.0.0.1/ | grep -oE '/assets/app-[^" ]+\.js' | head -1)
    test -n "$asset"
    curl --fail --silent --head "http://127.0.0.1$asset" | tr -d '\r' | grep -qi '^Cache-Control: public,.*immutable'
    curl --fail --silent http://127.0.0.1/version.json | grep -q '"version"'
    echo "compose smoke test passed"
    exit 0
  fi
  sleep 2
done
docker compose -f "$ROOT/compose.yml" ps
docker compose -f "$ROOT/compose.yml" logs --tail=100
exit 1
