[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$Version = 'dev'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = 'dotnet'
}

$output = Join-Path $root ".artifacts\bridge-$Runtime-$Version"
$zip = Join-Path $root ".artifacts\CodexCompanion-Bridge-$Runtime-$Version.zip"
if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
New-Item -ItemType Directory -Path $output -Force | Out-Null

& $dotnet publish (Join-Path $root 'apps\bridge\CodexCompanion.Bridge.csproj') `
    --configuration Release --runtime $Runtime --self-contained true `
    --output $output
if ($LASTEXITCODE -ne 0) {
    throw "Bridge publish failed with exit code $LASTEXITCODE"
}

Copy-Item (Join-Path $root 'scripts\install-bridge.ps1') $output
Copy-Item (Join-Path $root 'scripts\uninstall-bridge.ps1') $output
@"
Codex Companion Bridge $Version

1. Install Codex CLI separately, or set its path during setup.
2. Run .\install-bridge.ps1 from an interactive PowerShell window.
3. The installer runs Bridge setup and creates a per-user logon task.
4. Run .\uninstall-bridge.ps1 to remove the task and installed files.

The package does not contain Codex CLI. It only contains the self-contained Bridge.
"@ | Set-Content (Join-Path $output 'README.txt') -Encoding UTF8

if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash
Set-Content -LiteralPath ($zip + '.sha256') -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ASCII
Write-Output "Created $zip"
