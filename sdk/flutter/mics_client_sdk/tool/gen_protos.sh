#!/usr/bin/env bash
set -euo pipefail

proto_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../proto" && pwd)"
out_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/src/proto"

command -v protoc >/dev/null 2>&1 || { echo "protoc not found in PATH" >&2; exit 1; }
command -v dart >/dev/null 2>&1 || { echo "dart not found in PATH" >&2; exit 1; }

dart pub global activate protoc_plugin >/dev/null

pub_cache="${PUB_CACHE:-$HOME/.pub-cache}"
export PATH="$pub_cache/bin:$PATH"

mkdir -p "$out_dir"

protoc \
  "--proto_path=$proto_root" \
  "--dart_out=$out_dir" \
  "Protos/mics_message.proto"

echo "Generated into $out_dir"
