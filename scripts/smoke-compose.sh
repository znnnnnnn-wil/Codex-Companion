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
    echo "compose smoke test passed"
    exit 0
  fi
  sleep 2
done
docker compose -f "$ROOT/compose.yml" ps
docker compose -f "$ROOT/compose.yml" logs --tail=100
exit 1
