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

$project = Join-Path $root 'apps\bridge\CodexCompanion.Bridge.csproj'
& $dotnet restore $project --runtime $Runtime
if ($LASTEXITCODE -ne 0) {
    throw "Bridge restore failed with exit code $LASTEXITCODE"
}

& $dotnet publish $project `
    --configuration Release --runtime $Runtime --self-contained true `
    --no-restore `
    --output $output
if ($LASTEXITCODE -ne 0) {
    throw "Bridge publish failed with exit code $LASTEXITCODE"
}

$utf8WithBom = [System.Text.UTF8Encoding]::new($true)
foreach ($scriptName in @('install-bridge.ps1', 'uninstall-bridge.ps1', 'bridge-control.ps1')) {
    $sourcePath = Join-Path $root "scripts\$scriptName"
    $targetPath = Join-Path $output $scriptName
    $scriptContent = [System.IO.File]::ReadAllText($sourcePath, [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($targetPath, $scriptContent, $utf8WithBom)
}
@"
Codex Companion Bridge $Version

1. Install Codex CLI separately, or set its path during setup.
2. Run .\install-bridge.ps1 from an interactive PowerShell window.
3. Bridge is stopped by default after installation. Use bridge-control.ps1 -Action Start or the Start menu shortcut.
4. Autostart is optional: bridge-control.ps1 -Action EnableAutostart.
5. Use bridge-control.ps1 -Action Stop to stop Bridge.
6. Run .\uninstall-bridge.ps1 to remove the task and installed files.

The package does not contain Codex CLI. It only contains the self-contained Bridge.
"@ | Set-Content (Join-Path $output 'README.txt') -Encoding UTF8

if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash
Set-Content -LiteralPath ($zip + '.sha256') -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ASCII
Write-Output "Created $zip"
