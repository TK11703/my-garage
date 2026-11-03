#Requires -Version 7.0
<#
.SYNOPSIS
    Fails when a tracked delivery file contains something that looks like a secret.

.DESCRIPTION
    Scans the tracked files under .github/, infra/, appsettings*.json and *.bicepparam files for storage keys, SAS signatures,
    passwords with a value, client secrets with a value, private keys, and GitHub tokens. It reports file and line, never the
    matched value. Placeholders (<...>, ${...}, empty values) and parameter names are allowed.

.PARAMETER Root
    Repository root.
#>
[CmdletBinding()]
param([string] $Root = (Join-Path $PSScriptRoot '..' '..'))

$ErrorActionPreference = 'Stop'
Push-Location $Root
try {
    $self = '.github/scripts/Test-NoSecrets.ps1'
    $files = @(git ls-files -- '.github' 'infra' '*appsettings*.json' '*.bicepparam') |
        Where-Object { $_ -ne $self -and (Test-Path -LiteralPath $_ -PathType Leaf) }

    $rules = @(
        @{ Name = 'storage account key'; Pattern = 'AccountKey=[A-Za-z0-9+/=]{20,}' }
        @{ Name = 'shared access signature'; Pattern = '(?i)(SharedAccessSignature=|[?&]sig=)[A-Za-z0-9%+/=]{20,}' }
        @{ Name = 'password with a value'; Pattern = '(?i)\bpassword\s*=\s*(?![<$\{;"''\s])[^;\s"'']+' }
        @{ Name = 'client secret with a value'; Pattern = '(?i)\bclient_?secret\b["'']?\s*[:=]\s*["'']?(?![<$\{"''\s])[A-Za-z0-9~._\-]{8,}' }
        @{ Name = 'private key'; Pattern = '-----BEGIN [A-Z ]*PRIVATE KEY-----' }
        @{ Name = 'GitHub token'; Pattern = '\bgh[pousr]_[A-Za-z0-9]{30,}\b' }
    )

    $hits = 0
    foreach ($file in $files) {
        $lineNumber = 0
        foreach ($line in Get-Content -LiteralPath $file) {
            $lineNumber++
            foreach ($rule in $rules) {
                if ($line -match $rule.Pattern) {
                    Write-Host "${file}:${lineNumber}: possible $($rule.Name)"
                    $hits++
                }
            }
        }
    }

    if ($hits -gt 0) {
        Write-Host "$hits possible secret(s) found. Values are not printed."
        exit 1
    }
    Write-Host "No secrets found in $($files.Count) files."
}
finally { Pop-Location }
