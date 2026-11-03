#Requires -Version 7.0
<#
.SYNOPSIS
    Writes a summary of test results (TRX files) to the GitHub Actions step summary.

.DESCRIPTION
    Shows totals and, for each failed test, its name and the first lines of its message and stack trace.
    Output of passing tests is never written. Always exits 0 so it cannot mask the real test result.

.PARAMETER ResultsPath
    Folder searched recursively for *.trx files.

.PARAMETER MaxFailureLines
    Lines of message and stack trace shown per failed test.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ResultsPath,
    [int] $MaxFailureLines = 15
)

# A problem reading one file must never fail the step; the test step reports the real result.
$ErrorActionPreference = 'Continue'

$summaryPath = $env:GITHUB_STEP_SUMMARY
function Write-Summary([string] $Text) {
    if ($summaryPath) { Add-Content -Path $summaryPath -Value $Text -Encoding utf8 } else { Write-Host $Text }
}

$files = @(Get-ChildItem -Path $ResultsPath -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue)
if ($files.Count -eq 0) {
    Write-Summary '### Test results'
    Write-Summary "No TRX files were found under ``$ResultsPath``."
    exit 0
}

$passed = 0; $failed = 0; $skipped = 0
$failures = [System.Collections.Generic.List[object]]::new()

foreach ($file in $files) {
    [xml] $trx = Get-Content -LiteralPath $file.FullName -Raw
    $ns = [System.Xml.XmlNamespaceManager]::new($trx.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')

    foreach ($result in $trx.SelectNodes('//t:UnitTestResult', $ns)) {
        switch ($result.outcome) {
            'Passed' { $passed++ }
            'Failed' {
                $failed++
                $errorInfo = $result.SelectSingleNode('t:Output/t:ErrorInfo', $ns)
                $lines = @()
                if ($errorInfo) {
                    $lines = @(($errorInfo.InnerText -split "`r?`n") | Where-Object { $_.Trim() } | Select-Object -First $MaxFailureLines)
                }
                $failures.Add([pscustomobject]@{ Name = $result.testName; Lines = $lines })
            }
            default { $skipped++ }
        }
    }
}

Write-Summary '### Test results'
Write-Summary ''
Write-Summary '| Passed | Failed | Skipped |'
Write-Summary '|---|---|---|'
Write-Summary "| $passed | $failed | $skipped |"

if ($failures.Count -gt 0) {
    Write-Summary ''
    Write-Summary '#### Failed tests'
    foreach ($failure in $failures) {
        Write-Summary ''
        Write-Summary "<details><summary>$($failure.Name)</summary>"
        Write-Summary ''
        Write-Summary '```text'
        foreach ($line in $failure.Lines) { Write-Summary $line }
        Write-Summary '```'
        Write-Summary '</details>'
    }
}

exit 0
