param(
    [string]$Configuration = "Release",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\SteamNativeBrowser.WinUI\SteamNativeBrowser.WinUI.csproj"
$output = Join-Path $root "dist-winui"

# 本机 NuGet 缓存 / 离线包目录（由 tools\fetch_nuget.py 预先生成）
$feedRoot = Join-Path $root "..\..\winui-build"
$env:DOTNET_CLI_HOME = Join-Path $feedRoot "dotnet-home"
$env:NUGET_PACKAGES = Join-Path $feedRoot "nuget-cache"

$selfContainedValue = if ($SelfContained) { "true" } else { "false" }

Write-Host "Publishing WinUI 3 app -> $output"

dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained $selfContainedValue `
    -o $output `
    -v m

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host ""
Write-Host "Done -> $output"
