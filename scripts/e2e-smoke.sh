#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose_file="$repo_root/docker-compose.e2e.yml"
artifacts_dir="${1:-${TMPDIR:-/tmp}/mics-e2e-smoke}"
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

metric_sum() {
  local file="$1"
  local prefix="$2"
  local sum
  sum="$(grep -F "$prefix" "$file" | awk '{sum += $NF} END {print sum + 0}')"
  echo "${sum:-0}"
}

assert_metric_ge() {
  local file="$1"
  local prefix="$2"
  local minimum="$3"
  local actual
  actual="$(metric_sum "$file" "$prefix")"
  if (( actual < minimum )); then
    echo "Expected metric '$prefix' in $file to be >= $minimum, got $actual" >&2
    return 1
  fi
}

cleanup() {
  compose logs --no-color >"$artifacts_dir/compose.log" 2>&1 || true
  compose ps >"$artifacts_dir/compose.ps" 2>&1 || true
  compose down -v --remove-orphans >/dev/null 2>&1 || true
}
trap cleanup EXIT

export COMPOSE_PROJECT_NAME="${COMPOSE_PROJECT_NAME:-mics-e2e}"
export HOOK_AUTH_DELAY_MS="${HOOK_AUTH_DELAY_MS:-0}"
export HOOK_AUTH_STATUS_CODE="${HOOK_AUTH_STATUS_CODE:-0}"
export HOOK_CHECK_MESSAGE_DELAY_MS="${HOOK_CHECK_MESSAGE_DELAY_MS:-0}"
export HOOK_CHECK_MESSAGE_STATUS_CODE="${HOOK_CHECK_MESSAGE_STATUS_CODE:-0}"
export HOOK_GET_GROUP_MEMBERS_DELAY_MS="${HOOK_GET_GROUP_MEMBERS_DELAY_MS:-0}"
export HOOK_GET_GROUP_MEMBERS_STATUS_CODE="${HOOK_GET_GROUP_MEMBERS_STATUS_CODE:-0}"
export HOOK_GET_OFFLINE_MESSAGES_DELAY_MS="${HOOK_GET_OFFLINE_MESSAGES_DELAY_MS:-0}"
export HOOK_GET_OFFLINE_MESSAGES_STATUS_CODE="${HOOK_GET_OFFLINE_MESSAGES_STATUS_CODE:-0}"

run compose build
run compose up -d
wait_for_http hookmock http://localhost:18081/healthz
wait_for_http gateway-a http://localhost:18080/healthz
wait_for_http gateway-b http://localhost:28080/healthz

run dotnet run --project "$repo_root/tools/Mics.LoadTester/Mics.LoadTester.csproj" -- \
  --url ws://localhost:18080/ws --tenantId t1 --connections 2 --durationSeconds 5 --mode connect-only \
  | tee "$artifacts_dir/connect-node-a.log"

run dotnet run --project "$repo_root/tools/Mics.LoadTester/Mics.LoadTester.csproj" -- \
  --url ws://localhost:28080/ws --tenantId t1 --connections 2 --durationSeconds 5 --mode connect-only \
  | tee "$artifacts_dir/connect-node-b.log"

(
  dotnet run --project "$repo_root/tools/Mics.LoadTester/Mics.LoadTester.csproj" -- \
    --url ws://localhost:18080/ws --tenantId t1 --connections 4 --durationSeconds 8 --mode single-chat \
    --sendQpsPerConn 2 --payloadBytes 64 --devicePrefix a-
) | tee "$artifacts_dir/single-node-a.log" &
pid_a=$!

(
  dotnet run --project "$repo_root/tools/Mics.LoadTester/Mics.LoadTester.csproj" -- \
    --url ws://localhost:28080/ws --tenantId t1 --connections 4 --durationSeconds 8 --mode single-chat \
    --sendQpsPerConn 2 --payloadBytes 64 --devicePrefix b-
) | tee "$artifacts_dir/single-node-b.log" &
pid_b=$!

wait "$pid_a"
wait "$pid_b"

curl -fsS http://localhost:18080/metrics >"$artifacts_dir/gateway-a.metrics"
curl -fsS http://localhost:28080/metrics >"$artifacts_dir/gateway-b.metrics"

assert_metric_ge "$artifacts_dir/gateway-a.metrics" 'mics_ws_connected_total{tenant="t1",node="node-a"}' 1
assert_metric_ge "$artifacts_dir/gateway-b.metrics" 'mics_ws_connected_total{tenant="t1",node="node-b"}' 1

grpc_total=$(( \
  $(metric_sum "$artifacts_dir/gateway-a.metrics" 'mics_deliveries_total{tenant="t1",via="grpc_single"}') + \
  $(metric_sum "$artifacts_dir/gateway-b.metrics" 'mics_deliveries_total{tenant="t1",via="grpc_single"}') + \
  $(metric_sum "$artifacts_dir/gateway-a.metrics" 'mics_deliveries_total{tenant="t1",via="grpc_in_single"}') + \
  $(metric_sum "$artifacts_dir/gateway-b.metrics" 'mics_deliveries_total{tenant="t1",via="grpc_in_single"}') \
))

if (( grpc_total < 1 )); then
  echo "Expected cross-node gRPC deliveries during dual-node smoke" >&2
  exit 1
fi

mq_total=$(( \
  $(metric_sum "$artifacts_dir/gateway-a.metrics" 'mics_mq_published_total{tenant="t1",topic="event",event_type="ConnectOnline"}') + \
  $(metric_sum "$artifacts_dir/gateway-b.metrics" 'mics_mq_published_total{tenant="t1",topic="event",event_type="ConnectOnline"}') \
))

if (( mq_total < 1 )); then
  echo "Expected Kafka-backed MQ publish metrics during smoke" >&2
  exit 1
fi

export HOOK_CHECK_MESSAGE_DELAY_MS=250
run compose down -v --remove-orphans
run compose up -d
wait_for_http hookmock http://localhost:18081/healthz
wait_for_http gateway-a http://localhost:18080/healthz
wait_for_http gateway-b http://localhost:28080/healthz

run dotnet run --project "$repo_root/tools/Mics.LoadTester/Mics.LoadTester.csproj" -- \
  --url ws://localhost:18080/ws --tenantId t1 --connections 2 --durationSeconds 6 --mode single-chat \
  --sendQpsPerConn 1 --payloadBytes 32 --devicePrefix degrade- \
  | tee "$artifacts_dir/degrade.log"

curl -fsS http://localhost:18080/metrics >"$artifacts_dir/gateway-a-degrade.metrics"
assert_metric_ge "$artifacts_dir/gateway-a-degrade.metrics" 'mics_hook_requests_total{tenant="t1",op="CheckMessage",result="timeout"}' 1
assert_metric_ge "$artifacts_dir/gateway-a-degrade.metrics" 'mics_hook_check_message_total{tenant="t1",result="degraded"}' 1

export HOOK_CHECK_MESSAGE_DELAY_MS=0
run compose down -v --remove-orphans
run compose up -d
wait_for_http hookmock http://localhost:18081/healthz
wait_for_http gateway-a http://localhost:18080/healthz
wait_for_http gateway-b http://localhost:28080/healthz

(
  dotnet run --project "$repo_root/tools/Mics.LoadTester/Mics.LoadTester.csproj" -- \
    --url ws://localhost:28080/ws --tenantId t1 --connections 2 --durationSeconds 20 --mode connect-only \
    --devicePrefix drain-
) >"$artifacts_dir/drain-client.log" 2>&1 &
drain_pid=$!
sleep 3
run compose stop -t 15 gateway-b
wait "$drain_pid" || true
compose logs --no-color gateway-b >"$artifacts_dir/gateway-b-drain.log"
grep -q 'Application is shutting down' "$artifacts_dir/gateway-b-drain.log"
if ! grep -q 'shutdown_drain_begin' "$artifacts_dir/gateway-b-drain.log" || ! grep -q 'shutdown_drain_done' "$artifacts_dir/gateway-b-drain.log"; then
  echo "WARN: graceful stop signal observed but shutdown_drain markers were not emitted" | tee "$artifacts_dir/gateway-b-drain.warn"
fi

run compose up -d gateway-b
wait_for_http gateway-b http://localhost:28080/healthz

(
  dotnet run --project "$repo_root/tools/Mics.LoadTester/Mics.LoadTester.csproj" -- \
    --url ws://localhost:28080/ws --tenantId t1 --connections 2 --durationSeconds 25 --mode connect-only \
    --devicePrefix dead-
) >"$artifacts_dir/dead-node-client.log" 2>&1 &
dead_pid=$!
sleep 4
run compose kill -s KILL gateway-b
wait "$dead_pid" || true
sleep 12

curl -fsS http://localhost:18080/metrics >"$artifacts_dir/gateway-a-dead-node.metrics"
assert_metric_ge "$artifacts_dir/gateway-a-dead-node.metrics" 'mics_dead_node_cleanups_total{node="node-b"}' 1

echo "E2E smoke completed successfully. Artifacts: $artifacts_dir"
