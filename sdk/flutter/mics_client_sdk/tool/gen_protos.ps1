$ErrorActionPreference = "Stop"

$protoRoot = Join-Path $PSScriptRoot "..\\proto"
$outDir = Join-Path $PSScriptRoot "..\\lib\\src\\proto"

if (!(Get-Command protoc -ErrorAction SilentlyContinue)) {
  throw "protoc not found in PATH"
}

if (!(Get-Command dart -ErrorAction SilentlyContinue)) {
  throw "dart not found in PATH"
}

dart pub global activate protoc_plugin | Out-Null
$pubCache = if ($env:PUB_CACHE) { $env:PUB_CACHE } else { Join-Path $env:LOCALAPPDATA 'Pub\\Cache' }
$pluginDir = Join-Path $pubCache "bin"
$protocGenDart = Join-Path $pluginDir "protoc-gen-dart.bat"
if (!(Test-Path $protocGenDart)) {
  $protocGenDart = Join-Path $pluginDir "protoc-gen-dart"
}
if (!(Test-Path $protocGenDart)) {
  throw "protoc-gen-dart not found in dart pub cache bin"
}

$env:PATH = $pluginDir + ';' + $env:PATH

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

& protoc `
  "--proto_path=$protoRoot" `
  "--dart_out=$outDir" `
  "Protos/mics_message.proto"

Write-Host "Generated into $outDir"
