#!/usr/bin/env bash
set -euo pipefail

# Linux cloud development only. Windows uses the SDK from global.json and PowerShell 7.4+.
task_tools=/workspace/toolchains
task_cache=/workspace/setup-cache
mkdir -p "$task_tools/dotnet" "$task_tools/pwsh" "$task_cache"

if [[ ! -x "$task_tools/dotnet/dotnet" ]] || [[ "$("$task_tools/dotnet/dotnet" --version)" != "10.0.401" ]]; then
  curl --fail --show-error --silent --location --retry 2 \
    https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-linux-x64.tar.gz \
    -o "$task_cache/dotnet-sdk.tar.gz"
  printf '%s  %s\n' '51c8b999af9e8dd9998c9edc5944e19a90788862068acd38694e098889054ce8c23d4f0c5cccfa16bf187d044562359e5ee69a9f8ad0bbe913ba90311fbce25b' "$task_cache/dotnet-sdk.tar.gz" | sha512sum --check --status
  tar -xzf "$task_cache/dotnet-sdk.tar.gz" -C "$task_tools/dotnet"
fi

if [[ ! -x "$task_tools/pwsh/pwsh" ]]; then
  curl --fail --show-error --silent --location --retry 2 \
    https://github.com/PowerShell/PowerShell/releases/download/v7.6.6/powershell-7.6.6-linux-x64.tar.gz \
    -o "$task_cache/powershell-7.6.6-linux-x64.tar.gz"
  printf '%s  %s\n' 'ddbc4a2d113bbd46d283cfedcbcd117a70caefd7673f41f2b4e0000badf103bc' "$task_cache/powershell-7.6.6-linux-x64.tar.gz" | sha256sum --check --status
  tar -xzf "$task_cache/powershell-7.6.6-linux-x64.tar.gz" -C "$task_tools/pwsh"
  chmod +x "$task_tools/pwsh/pwsh"
fi

cat > "$task_tools/activate.sh" <<'ACTIVATE'
export PATH="/workspace/toolchains/dotnet:/workspace/toolchains/pwsh:$PATH"
export DOTNET_ROOT=/workspace/toolchains/dotnet
export DOTNET_CLI_HOME=/workspace/toolchains/cli-home
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export NUGET_PACKAGES=/workspace/toolchains/nuget
export XDG_CACHE_HOME=/workspace/toolchains/cache
export XDG_CONFIG_HOME=/workspace/toolchains/config
export XDG_DATA_HOME=/workspace/toolchains/data
ACTIVATE
source "$task_tools/activate.sh"
cd /workspace/MPswift
dotnet --version
pwsh -NoProfile -File scripts/Setup-Native.ps1
pwsh -NoProfile -File scripts/Build.ps1
