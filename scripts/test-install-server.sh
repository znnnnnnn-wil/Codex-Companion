#!/usr/bin/env bash
set -Eeuo pipefail

# Inert integration test: no Docker daemon, network, git clone, or real .env.
repo="$(cd "$(dirname "$0")/.." && pwd)"
fixture="$(mktemp -d "$repo/.tmp-install-test.XXXXXX")"
mkdir -p "$fixture/bin" "$fixture/install path/.git"
cat > "$fixture/bin/docker" <<'EOF'
#!/usr/bin/env bash
set -eu
[[ "$1" == compose ]]
shift
[[ "$1" == version ]] && exit 0
[[ "$1" == --env-file ]] || { echo 'Missing explicit --env-file' >&2; exit 1; }
[[ "$2" == "$PWD/.env" && -f "$2" ]] || { echo 'Wrong environment path' >&2; exit 1; }
shift 2
[[ "$1" == -f && "$2" == "$EXPECTED_BASE" ]] || { echo 'Wrong Compose base file' >&2; exit 1; }
printf '%s\n' "$*" >> "$DOCKER_TEST_LOG"
EOF
for tool in git curl; do
  printf '#!/usr/bin/env bash\nexit 0\n' > "$fixture/bin/$tool"
done
chmod +x "$fixture/bin/"*
export PATH="$fixture/bin:$PATH"
export DOCKER_TEST_LOG="$fixture/docker.log"

for mode in quick quick-images https-images; do
  cat > "$fixture/install path/.env" <<'EOF'
ALLOWED_ORIGINS=old.example
POSTGRES_PASSWORD=test-only
PUBLIC_HOST=old.example
EOF
  : > "$DOCKER_TEST_LOG"
  arguments=(--host 198.51.100.10 --dir "$fixture/install path")
  export EXPECTED_BASE=deploy/docker-compose.quick.yml
  case "$mode" in
    quick-images) arguments+=(--images) ;;
    https-images) arguments+=(--domain example.test --images); export EXPECTED_BASE=compose.yml ;;
  esac
  bash "$repo/scripts/install-server.sh" "${arguments[@]}"
  if [[ "$mode" == quick ]]; then
    grep -q -- 'up -d --build' "$DOCKER_TEST_LOG"
  else
    grep -q -- 'deploy/docker-compose.images.yml pull' "$DOCKER_TEST_LOG"
    grep -q -- 'deploy/docker-compose.images.yml up -d' "$DOCKER_TEST_LOG"
  fi
  if [[ "$mode" == https-images ]]; then
    grep -q -- 'deploy/docker-compose.https.yml' "$DOCKER_TEST_LOG"
  fi
done
echo 'Server installer tests passed (explicit absolute env-file, spaces, quick/build/images/HTTPS).'
