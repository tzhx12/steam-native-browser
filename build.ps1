param(
    [string]$Configuration = "Release",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\SteamNativeBrowser\SteamNativeBrowser.csproj"
$output = Join-Path $root "dist"

$selfContainedValue = if ($SelfContained) { "true" } else { "false" }

Write-Host "Publishing $project ($Configuration, self-contained=$selfContainedValue) ..."

dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained $selfContainedValue `
    -p:PublishSingleFile=true `
    -o $output `
    -v m

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host ""
Write-Host "Done -> $output"
Get-ChildItem $output | Select-Object Name, @{ n = "Size"; e = { "{0:N0} KB" -f ($_.Length / 1kb) } } | Format-Table -AutoSize
