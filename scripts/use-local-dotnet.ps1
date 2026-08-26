[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dotnetRoot = Join-Path $projectRoot '.tools\dotnet'
$dotnet = Join-Path $dotnetRoot 'dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnet)) {
    & (Join-Path $PSScriptRoot 'bootstrap-dotnet.ps1')
}

$env:DOTNET_ROOT = $dotnetRoot
if (-not (($env:PATH -split ';') -contains $dotnetRoot)) {
    $env:PATH = "$dotnetRoot;$env:PATH"
}
Write-Output "当前终端已启用 .NET SDK $(& $dotnet --version)"
