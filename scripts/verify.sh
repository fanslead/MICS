#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root_dir"

run() {
  echo "==> $*"
  "$@"
}

ensure_maven() {
  if command -v mvn >/dev/null 2>&1; then
    echo "mvn"
    return 0
  fi

  local ver="3.9.6"
  local tmp="${TMPDIR:-/tmp}"
  local root="$tmp/apache-maven-$ver"
  local home="$root/apache-maven-$ver"
  local mvn_cmd="$home/bin/mvn"
  local tgz="$tmp/apache-maven-$ver-bin.tar.gz"

  if [[ -x "$mvn_cmd" ]]; then
    echo "$mvn_cmd"
    return 0
  fi

  if command -v tar >/dev/null 2>&1; then :; else return 1; fi

  if [[ ! -f "$tgz" ]]; then
    local url="https://archive.apache.org/dist/maven/maven-3/$ver/binaries/apache-maven-$ver-bin.tar.gz"
    if command -v curl >/dev/null 2>&1; then
      curl -fsSL -o "$tgz" "$url"
    elif command -v wget >/dev/null 2>&1; then
      wget -q -O "$tgz" "$url"
    else
      return 1
    fi
  fi

  rm -rf "$root"
  mkdir -p "$root"
  tar -xzf "$tgz" -C "$root"

  if [[ -x "$mvn_cmd" ]]; then
    echo "$mvn_cmd"
    return 0
  fi

  return 1
}

command -v dotnet >/dev/null 2>&1 || { echo "Missing dotnet" >&2; exit 1; }
run dotnet test ./Mics.slnx -c Release -v minimal

if [[ -f sdk/ts/package.json ]]; then
  if command -v npm >/dev/null 2>&1; then
    pushd sdk/ts >/dev/null
    run npm install
    run npm test
    run npm run build
    popd >/dev/null
  else
    echo "SKIP sdk/ts (missing npm)" >&2
  fi
fi

if [[ -f sdk/node/mics-hook-sdk/package.json ]]; then
  if command -v npm >/dev/null 2>&1; then
    pushd sdk/node/mics-hook-sdk >/dev/null
    run npm install
    run npm test
    run npm run build
    popd >/dev/null
  else
    echo "SKIP sdk/node/mics-hook-sdk (missing npm)" >&2
  fi
fi

if [[ -f sdk/wechat/mics-client-sdk/package.json ]]; then
  if command -v npm >/dev/null 2>&1; then
    pushd sdk/wechat/mics-client-sdk >/dev/null
    run npm install
    run npm test
    run npm run build
    popd >/dev/null
  else
    echo "SKIP sdk/wechat/mics-client-sdk (missing npm)" >&2
  fi
fi

if [[ -f sdk/go/mics-hook-sdk/go.mod ]]; then
  if command -v go >/dev/null 2>&1; then
    pushd sdk/go/mics-hook-sdk >/dev/null
    run go test ./...
    popd >/dev/null
  else
    echo "SKIP Go SDKs (missing go)" >&2
  fi
fi

if [[ -f sdk/go/samples/hook-server/go.mod ]]; then
  if command -v go >/dev/null 2>&1; then
    pushd sdk/go/samples/hook-server >/dev/null
    run go test ./...
    popd >/dev/null
  fi
fi

if [[ -f sdk/go/samples/kafka-consumer/go.mod ]]; then
  if command -v go >/dev/null 2>&1; then
    pushd sdk/go/samples/kafka-consumer >/dev/null
    run go test ./...
    popd >/dev/null
  fi
fi

if [[ -f sdk/java/pom.xml ]]; then
  if mvn_cmd="$(ensure_maven)"; then
    pushd sdk/java >/dev/null
    run "$mvn_cmd" -q test
    popd >/dev/null
  else
    echo "SKIP sdk/java (missing mvn/curl/wget/tar)" >&2
  fi
fi

if [[ -f sdk/flutter/mics_client_sdk/pubspec.yaml ]]; then
  if command -v dart >/dev/null 2>&1; then
    pushd sdk/flutter/mics_client_sdk >/dev/null
    run dart pub get
    run dart test
    popd >/dev/null
  else
    echo "SKIP Flutter SDK (missing dart)" >&2
  fi
fi

if [[ -f sdk/android/mics-client-sdk/gradlew ]]; then
  pushd sdk/android/mics-client-sdk >/dev/null
  run ./gradlew test --no-daemon
  popd >/dev/null
fi

echo "OK"
