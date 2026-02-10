$ErrorActionPreference = "Stop"

function Has-Cmd([string]$name) {
  return [bool](Get-Command $name -ErrorAction SilentlyContinue)
}

function Run([string]$exe, [string[]]$arguments = @()) {
  $display = @($exe) + $arguments
  Write-Host ("==> " + ($display -join " "))
  & $exe @arguments
  if ($LASTEXITCODE -ne 0) {
    throw ("Command failed ($LASTEXITCODE): " + ($display -join " "))
  }
}

function Ensure-Maven([ref]$mvnCmd) {
  if (Get-Command mvn -ErrorAction SilentlyContinue) {
    $mvnCmd.Value = "mvn"
    return
  }

  $ver = "3.9.6"
  $zip = Join-Path $env:TEMP "apache-maven-$ver-bin.zip"
  $root = Join-Path $env:TEMP "apache-maven-$ver"
  $mavenHome = Join-Path $root "apache-maven-$ver"
  $exe = Join-Path $mavenHome "bin\\mvn.cmd"

  if (!(Test-Path $exe)) {
    if (!(Test-Path $zip)) {
      Invoke-WebRequest -Uri "https://archive.apache.org/dist/maven/maven-3/$ver/binaries/apache-maven-$ver-bin.zip" -OutFile $zip
    }
    if (Test-Path $root) { Remove-Item -Recurse -Force $root }
    Expand-Archive -Path $zip -DestinationPath $root -Force
  }

  if (!(Test-Path $exe)) {
    throw "Failed to provision Maven at $exe"
  }

  $mvnCmd.Value = $exe
}

Set-Location (Split-Path $PSScriptRoot -Parent)

if (!(Has-Cmd dotnet)) { throw "Missing required command: dotnet" }
Run "dotnet" @("test", ".\\Mics.slnx", "-c", "Release", "-v", "minimal")

if (Test-Path "sdk\\ts\\package.json") {
  if (Has-Cmd npm) {
    Push-Location "sdk\\ts"
    Run "npm" @("install")
    Run "npm" @("test")
    Run "npm" @("run", "build")
    Pop-Location
  } else {
    Write-Host "SKIP sdk/ts (missing npm)"
  }
}

if (Test-Path "sdk\\node\\mics-hook-sdk\\package.json") {
  if (Has-Cmd npm) {
    Push-Location "sdk\\node\\mics-hook-sdk"
    Run "npm" @("install")
    Run "npm" @("test")
    Run "npm" @("run", "build")
    Pop-Location
  } else {
    Write-Host "SKIP sdk/node/mics-hook-sdk (missing npm)"
  }
}

if (Test-Path "sdk\\wechat\\mics-client-sdk\\package.json") {
  if (Has-Cmd npm) {
    Push-Location "sdk\\wechat\\mics-client-sdk"
    Run "npm" @("install")
    Run "npm" @("test")
    Run "npm" @("run", "build")
    Pop-Location
  } else {
    Write-Host "SKIP sdk/wechat/mics-client-sdk (missing npm)"
  }
}

if (Test-Path "sdk\\go\\mics-hook-sdk\\go.mod") {
  if (!(Has-Cmd go)) {
    Write-Host "SKIP Go SDKs (missing go)"
  } else {
  Push-Location "sdk\\go\\mics-hook-sdk"
  Run "go" @("test", "./...")
  Pop-Location
  }
}

if (Test-Path "sdk\\go\\samples\\hook-server\\go.mod") {
  if (Has-Cmd go) {
    Push-Location "sdk\\go\\samples\\hook-server"
    Run "go" @("test", "./...")
    Pop-Location
  }
}

if (Test-Path "sdk\\go\\samples\\kafka-consumer\\go.mod") {
  if (Has-Cmd go) {
    Push-Location "sdk\\go\\samples\\kafka-consumer"
    Run "go" @("test", "./...")
    Pop-Location
  }
}

if (Test-Path "sdk\\java\\pom.xml") {
  $mvn = ""
  Ensure-Maven ([ref]$mvn)
  Push-Location "sdk\\java"
  Run $mvn @("-q", "test")
  Pop-Location
}

if (Test-Path "sdk\\flutter\\mics_client_sdk\\pubspec.yaml") {
  if (Has-Cmd dart) {
    Push-Location "sdk\\flutter\\mics_client_sdk"
    Run "dart" @("pub", "get")
    Run "dart" @("test")
    Pop-Location
  } else {
    Write-Host "SKIP Flutter SDK (missing dart)"
  }
}

if (Test-Path "sdk\\android\\mics-client-sdk\\gradlew.bat") {
  Push-Location "sdk\\android\\mics-client-sdk"
  Run ".\\gradlew.bat" @("test", "--no-daemon")
  Pop-Location
}

Write-Host "OK"
