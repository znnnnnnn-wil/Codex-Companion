[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$installDir = Join-Path $projectRoot '.tools\dotnet'
$dotnet = Join-Path $installDir 'dotnet.exe'

if (Test-Path -LiteralPath $dotnet) {
    Write-Output "本地 .NET SDK 已存在：$(& $dotnet --version)"
    exit 0
}

$installer = Join-Path $env:TEMP 'dotnet-install-codex-companion.ps1'
Invoke-WebRequest -UseBasicParsing -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
& $installer -Channel '10.0' -Quality 'GA' -InstallDir $installDir -NoPath
& $dotnet --info
