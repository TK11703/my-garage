#Requires -Version 7.0
<#
.SYNOPSIS
    Waits for the deployed API and web apps to report healthy on the expected image tag.

.DESCRIPTION
    Polls GET <address>/health of each app until it answers 200 with the body "Healthy" (a scaled-to-zero app needs a moment to
    start), then confirms with the Container Apps CLI that the latest revision of each app runs the expected tag and is ready.
    Prints one line per app; no response body beyond the status word and no secret is printed. Exits non-zero on any failure.

.PARAMETER ApiAddress
    https address of the API.

.PARAMETER WebAddress
    https address of the web app.

.PARAMETER ExpectedTag
    Image tag both apps must run (sha-<commit>).

.PARAMETER ResourceGroup
    Resource group of the Container Apps.

.PARAMETER TimeoutMinutes
    Time allowed for each app to become healthy.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ApiAddress,
    [Parameter(Mandatory)] [string] $WebAddress,
    [Parameter(Mandatory)] [string] $ExpectedTag,
    [string] $ResourceGroup = 'rg-apps',
    [int] $TimeoutMinutes = 10
)

$ErrorActionPreference = 'Stop'

$apps = @(
    @{ Label = 'api'; Name = 'ca-api-mygarage'; Address = $ApiAddress }
    @{ Label = 'web'; Name = 'ca-web-mygarage'; Address = $WebAddress }
)
$failures = 0

foreach ($app in $apps) {
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    $healthy = $false
    $lastStatus = 'no response'
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri "$($app.Address.TrimEnd('/'))/health" -TimeoutSec 30 -SkipHttpErrorCheck
            $lastStatus = "HTTP $([int] $response.StatusCode)"
            if ($response.StatusCode -eq 200 -and $response.Content.Trim() -eq 'Healthy') { $healthy = $true; break }
        }
        catch { $lastStatus = $_.Exception.GetType().Name }
        Start-Sleep -Seconds 10
    }

    $details = & az containerapp show --name $app.Name --resource-group $ResourceGroup `
        --query '{image:properties.template.containers[0].image, state:properties.provisioningState, latest:properties.latestRevisionName, ready:properties.latestReadyRevisionName}' `
        --only-show-errors -o json 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL $($app.Label): could not read $($app.Name): $(($details | Out-String).Trim())"
        $failures++
        continue
    }
    $info = ($details | Out-String) | ConvertFrom-Json

    $tagOk = $info.image.EndsWith(":$ExpectedTag", [StringComparison]::Ordinal)
    $revisionOk = $info.state -eq 'Succeeded' -and $info.latest -eq $info.ready
    if ($healthy -and $tagOk -and $revisionOk) {
        Write-Host "OK   $($app.Label): healthy on $ExpectedTag, revision $($info.latest)"
    }
    else {
        Write-Host "FAIL $($app.Label): health=$(if ($healthy) { 'Healthy' } else { $lastStatus }); tag $(if ($tagOk) { 'matches' } else { "is $($info.image.Split(':')[-1]), expected $ExpectedTag" }); revision $($info.latest), ready $($info.ready), state $($info.state)"
        $failures++
    }
}

if ($failures -gt 0) { exit 1 }
