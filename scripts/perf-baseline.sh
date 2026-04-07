#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose_file="$repo_root/docker-compose.e2e.yml"
artifacts_dir="${1:-${TMPDIR:-/tmp}/mics-perf-baseline}"
mkdir -p "$artifacts_dir"

compose() {
  docker compose -f "$compose_file" "$@"
}

run() {
  echo "==> $*"
  "$@"
}

wait_for_http() {
  local name="$1"
  local url="$2"
  local attempts="${3:-60}"

  for ((i=1; i<=attempts; i++)); do
    if curl -fsS "$url" >/dev/null 2>&1; then
      return 0
    fi
    sleep 2
  done

  echo "Timed out waiting for $name at $url" >&2
  return 1
}

cleanup() {
  compose logs --no-color >"$artifacts_dir/compose.log" 2>&1 || true
  compose ps >"$artifacts_dir/compose.ps" 2>&1 || true
  compose down -v --remove-orphans >/dev/null 2>&1 || true
}
trap cleanup EXIT

export COMPOSE_PROJECT_NAME="${COMPOSE_PROJECT_NAME:-mics-perf}"
export HOOK_AUTH_DELAY_MS=0
export HOOK_AUTH_STATUS_CODE=0
export HOOK_CHECK_MESSAGE_DELAY_MS=0
export HOOK_CHECK_MESSAGE_STATUS_CODE=0
export HOOK_GET_GROUP_MEMBERS_DELAY_MS=0
export HOOK_GET_GROUP_MEMBERS_STATUS_CODE=0
export HOOK_GET_OFFLINE_MESSAGES_DELAY_MS=0
export HOOK_GET_OFFLINE_MESSAGES_STATUS_CODE=0

run dotnet build "$repo_root/tools/Mics.LoadTester/Mics.LoadTester.csproj" -c Release
run compose build
run compose up -d
wait_for_http hookmock http://localhost:18081/healthz
wait_for_http gateway-a http://localhost:18080/healthz
wait_for_http gateway-b http://localhost:28080/healthz

run dotnet run --project "$repo_root/tools/Mics.LoadTester/Mics.LoadTester.csproj" -c Release --no-build -- \
  --url ws://localhost:18080/ws --tenantId t1 --connections 50 --rampSeconds 5 --durationSeconds 20 --mode connect-only \
  | tee "$artifacts_dir/connect-only.log"

run dotnet run --project "$repo_root/tools/Mics.LoadTester/Mics.LoadTester.csproj" -c Release --no-build -- \
  --url ws://localhost:18080/ws --tenantId t1 --connections 50 --rampSeconds 5 --durationSeconds 20 --mode heartbeat --sendQpsPerConn 1 \
  | tee "$artifacts_dir/heartbeat.log"

(
  dotnet run --project "$repo_root/tools/Mics.LoadTester/Mics.LoadTester.csproj" -c Release --no-build -- \
    --url ws://localhost:18080/ws --tenantId t1 --connections 40 --rampSeconds 4 --durationSeconds 20 --mode single-chat \
    --sendQpsPerConn 2 --payloadBytes 128 --devicePrefix perf-a-
) | tee "$artifacts_dir/single-chat-node-a.log" &
pid_a=$!

(
  dotnet run --project "$repo_root/tools/Mics.LoadTester/Mics.LoadTester.csproj" -c Release --no-build -- \
    --url ws://localhost:28080/ws --tenantId t1 --connections 40 --rampSeconds 4 --durationSeconds 20 --mode single-chat \
    --sendQpsPerConn 2 --payloadBytes 128 --devicePrefix perf-b-
) | tee "$artifacts_dir/single-chat-node-b.log" &
pid_b=$!

wait "$pid_a"
wait "$pid_b"

run dotnet run --project "$repo_root/tools/Mics.LoadTester/Mics.LoadTester.csproj" -c Release --no-build -- \
  --url ws://localhost:18080/ws --tenantId t1 --connections 40 --rampSeconds 4 --durationSeconds 20 --mode group-chat \
  --groupId group-1 --sendQpsPerConn 1 --payloadBytes 128 --devicePrefix perf-group- \
  | tee "$artifacts_dir/group-chat.log"

curl -fsS http://localhost:18080/metrics >"$artifacts_dir/gateway-a.metrics"
curl -fsS http://localhost:28080/metrics >"$artifacts_dir/gateway-b.metrics"

cat >"$artifacts_dir/README.txt" <<EOF
MICS perf baseline artifacts

Files:
- connect-only.log: baseline connect/close throughput and connect latency
- heartbeat.log: steady-state heartbeat baseline
- single-chat-node-a.log / single-chat-node-b.log: dual-node single chat baseline for cross-node forwarding
- group-chat.log: group fanout baseline using HookMock group members
- gateway-a.metrics / gateway-b.metrics: Prometheus snapshots captured after the runs
- compose.log / compose.ps: service lifecycle diagnostics
EOF

echo "Perf baseline artifacts written to $artifacts_dir"
