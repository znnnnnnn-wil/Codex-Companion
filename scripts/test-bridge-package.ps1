[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory
)

$ErrorActionPreference = 'Stop'
$packageRoot = (Resolve-Path -LiteralPath $PackageDirectory).Path

foreach ($scriptName in @('install-bridge.ps1', 'uninstall-bridge.ps1')) {
    $scriptPath = Join-Path $packageRoot $scriptName
    if (-not (Test-Path -LiteralPath $scriptPath)) {
        throw "Bridge package is missing $scriptName"
    }

    $bytes = [System.IO.File]::ReadAllBytes($scriptPath)
    $hasUtf8Bom = $bytes.Length -ge 3 -and
        $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and
        $bytes[2] -eq 0xBF
    if (-not $hasUtf8Bom) {
        throw "$scriptName must be encoded as UTF-8 with BOM for Windows PowerShell 5.1"
    }

    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile(
        $scriptPath,
        [ref]$tokens,
        [ref]$parseErrors
    ) | Out-Null

    if ($parseErrors.Count -gt 0) {
        $details = ($parseErrors | ForEach-Object { $_.Message }) -join [Environment]::NewLine
        throw "$scriptName failed Windows PowerShell parsing:$([Environment]::NewLine)$details"
    }
}

Write-Output 'Bridge PowerShell package validation passed.'
