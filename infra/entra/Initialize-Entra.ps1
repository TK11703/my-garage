#Requires -Version 7.0
<#
.SYNOPSIS
    Registers the My Garage web app and API in a Microsoft Entra tenant and stores the local configuration in the AppHost user-secrets.

.DESCRIPTION
    Idempotent: running it again finds the existing registrations and brings them back to the expected state.
    It creates (or updates):
      * "<NamePrefix> API"  - single tenant, Application ID URI api://<clientId>, delegated scope access_as_user, token version 2
      * "<NamePrefix> Web"  - single tenant, web redirect URIs for https://localhost:<WebPort>, delegated permission on the API scope
    and tries to grant tenant-wide admin consent for that permission.
    The web client secret is created only when none is stored for the current web app (or with -RotateSecret) and is written
    straight to the AppHost user-secrets. It is never printed or written to a tracked file.

.PARAMETER TenantId
    Tenant to register the apps in. Defaults to the tenant of the current Azure CLI session.

.PARAMETER WebPort
    HTTPS port of MyGarage.Web. Must match Web:HttpsPort in the AppHost (default 7080).

.PARAMETER NamePrefix
    Display-name prefix for the two registrations.

.PARAMETER AppHostProject
    AppHost project (folder or .csproj) whose user-secrets receive the configuration.

.PARAMETER RotateSecret
    Creates a new web client secret even if one is already stored.

.PARAMETER WhatIf
    Reports what would change without changing anything (read-only queries against Microsoft Graph still run).

.EXAMPLE
    az login --tenant <tenant-id> --allow-no-subscriptions
    ./infra/entra/Initialize-Entra.ps1 -TenantId <tenant-id>
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $TenantId,
    [ValidateRange(1024, 65535)] [int] $WebPort = 7080,
    [string] $NamePrefix = 'MyGarage',
    [string] $AppHostProject = (Join-Path $PSScriptRoot '..' '..' 'src' 'MyGarage.AppHost'),
    [switch] $RotateSecret
)

$ErrorActionPreference = 'Stop'

$ScopeName = 'access_as_user'
$apiName = "$NamePrefix API"
$webName = "$NamePrefix Web"
$webOrigin = "https://localhost:$WebPort"
$desiredRedirectUris = @("$webOrigin/signin-oidc", "$webOrigin/signout-callback-oidc")
$appSelect = '$select=id,appId,displayName,identifierUris,api,web,requiredResourceAccess'

function Invoke-Graph {
    param([string] $Method, [string] $Path, $Body = $null, [string[]] $Query = @())

    $azArgs = @('rest', '--method', $Method, '--url', "https://graph.microsoft.com/v1.0$Path", '--only-show-errors')
    # Query options go through --uri-parameters because az.cmd would split a raw '&' on Windows.
    if ($Query.Count -gt 0) { $azArgs += @('--uri-parameters') + $Query }
    $bodyFile = $null
    if ($null -ne $Body) {
        $bodyFile = New-TemporaryFile
        ($Body | ConvertTo-Json -Depth 10 -Compress) | Set-Content -Path $bodyFile -Encoding utf8NoBOM
        $azArgs += @('--headers', 'Content-Type=application/json', '--body', "@$bodyFile")
    }

    try {
        $output = & az @azArgs 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Microsoft Graph $Method $Path failed: $(($output | Out-String).Trim())"
        }
        $text = ($output | Out-String).Trim()
        if ($text) { return $text | ConvertFrom-Json }
    }
    finally {
        if ($bodyFile) { Remove-Item -Path $bodyFile -Force -ErrorAction SilentlyContinue }
    }
}

function Find-App([string] $DisplayName) {
    $result = Invoke-Graph GET '/applications' -Query "`$filter=displayName eq '$DisplayName'", $appSelect
    $apps = @($result.value)
    if ($apps.Count -gt 1) {
        throw "More than one app registration is named '$DisplayName'. Remove the duplicates and run the script again."
    }
    return $apps | Select-Object -First 1
}

function Find-ServicePrincipal([string] $AppId) {
    $result = Invoke-Graph GET '/servicePrincipals' -Query "`$filter=appId eq '$AppId'", '$select=id,appId'
    return @($result.value) | Select-Object -First 1
}

function Initialize-ServicePrincipal([string] $AppId) {
    $existing = Find-ServicePrincipal $AppId
    if ($existing) { return $existing }

    # A freshly created app can take a few seconds to replicate before its service principal can be created.
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        try { return Invoke-Graph POST '/servicePrincipals' @{ appId = $AppId } }
        catch {
            if ($attempt -eq 6) { throw }
            Start-Sleep -Seconds 5
        }
    }
}

# --- Preconditions -----------------------------------------------------------------------------------------------

function Read-UserSecrets {
    $secrets = @{}
    $lines = & dotnet user-secrets list --project $script:projectPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read user-secrets for '$script:projectPath'. Make sure the project has a UserSecretsId. $(($lines | Out-String).Trim())"
    }
    foreach ($line in $lines) {
        if ($line -match '^(?<key>[^=]+?)\s*=\s*(?<value>.*)$') { $secrets[$Matches.key] = $Matches.value }
    }
    return $secrets
}

function Set-UserSecret([string] $Key, [string] $Value) {
    # The CLI would parse a value starting with '-' as an option and then echo it in its error, so refuse it up front.
    if ($Value.StartsWith('-')) { throw "The value for '$Key' starts with '-' and cannot be passed to 'dotnet user-secrets'. Run the script again to get a new value." }

    # Output is discarded so a secret is never echoed to the console.
    & dotnet user-secrets set $Key $Value --project $script:projectPath *> $null
    if ($LASTEXITCODE -ne 0) { throw "Could not store '$Key' in the AppHost user-secrets." }
}

try {
    foreach ($tool in 'az', 'dotnet') {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "'$tool' was not found on PATH." }
    }

    $accountJson = az account show --only-show-errors 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $accountJson) {
        throw 'You are not signed in to the Azure CLI. Run: az login --tenant <tenant-id> --allow-no-subscriptions'
    }
    $account = $accountJson | ConvertFrom-Json
    if (-not $TenantId) { $TenantId = $account.tenantId }
    elseif ($account.tenantId -ne $TenantId) {
        throw "The Azure CLI is signed in to tenant $($account.tenantId), not $TenantId. Run: az login --tenant $TenantId --allow-no-subscriptions"
    }

    $script:projectPath = (Resolve-Path -Path $AppHostProject -ErrorAction Stop).Path
    if (-not (Test-Path -Path $script:projectPath)) { throw "AppHost project '$AppHostProject' was not found." }
    $secrets = Read-UserSecrets

    # --- Discovery (read-only) -----------------------------------------------------------------------------------
    $apiApp = Find-App $apiName
    $webApp = Find-App $webName

    if ($WhatIfPreference) {
        Write-Host "What if: tenant $TenantId, AppHost '$script:projectPath'" -ForegroundColor Cyan
        Write-Host ("  {0,-22} {1}" -f $apiName, $(if ($apiApp) { 'exists - scope, identifier URI and token version would be verified' } else { 'would be created' }))
        Write-Host ("  {0,-22} {1}" -f $webName, $(if ($webApp) { 'exists - redirect URIs and API permission would be verified' } else { 'would be created' }))
        $missingUris = $desiredRedirectUris | Where-Object { $_ -notin @($webApp.web.redirectUris) }
        Write-Host "  Redirect URIs to add : $(if ($missingUris) { $missingUris -join ', ' } else { 'none' })"
        $storedClient = $secrets['AzureAd:Web:ClientId']
        $needsSecret = $RotateSecret -or -not $secrets['AzureAd:Web:ClientSecret'] -or -not $webApp -or $storedClient -ne $webApp.appId
        Write-Host "  Web client secret    : $(if ($needsSecret) { 'a new secret would be created and stored in user-secrets' } else { 'existing secret would be kept' })"
        Write-Host '  Admin consent        : would be granted for the delegated API permission if missing'
        Write-Host 'No changes were made.' -ForegroundColor Cyan
        exit 0
    }

    # --- API app -------------------------------------------------------------------------------------------------
    if (-not $apiApp) {
        Write-Host "Creating app registration '$apiName'..."
        $apiApp = Invoke-Graph POST '/applications' @{ displayName = $apiName; signInAudience = 'AzureADMyOrg' }
    }

    $existingScopes = @($apiApp.api.oauth2PermissionScopes)
    $existingScope = $existingScopes | Where-Object { $_.value -eq $ScopeName } | Select-Object -First 1
    $scopeId = if ($existingScope) { $existingScope.id } else { [guid]::NewGuid().ToString() }
    $scope = @{
        id                      = $scopeId
        value                   = $ScopeName
        type                    = 'User'
        isEnabled               = $true
        adminConsentDisplayName = 'Access My Garage API'
        adminConsentDescription = 'Allows the My Garage web app to call the My Garage API on behalf of the signed-in user.'
        userConsentDisplayName  = 'Access My Garage API'
        userConsentDescription  = 'Allow the My Garage web app to call the My Garage API on your behalf.'
    }
    $scopes = @($existingScopes | Where-Object { $_ -and $_.value -ne $ScopeName }) + $scope

    Invoke-Graph PATCH "/applications/$($apiApp.id)" @{
        identifierUris = @("api://$($apiApp.appId)")
        api            = @{ requestedAccessTokenVersion = 2; oauth2PermissionScopes = $scopes }
    } | Out-Null
    $apiSp = Initialize-ServicePrincipal $apiApp.appId

    # --- Web app -------------------------------------------------------------------------------------------------
    if (-not $webApp) {
        Write-Host "Creating app registration '$webName'..."
        $webApp = Invoke-Graph POST '/applications' @{
            displayName    = $webName
            signInAudience = 'AzureADMyOrg'
            web            = @{ redirectUris = $desiredRedirectUris }
        }
    }

    $redirectUris = @(@($webApp.web.redirectUris) + $desiredRedirectUris | Where-Object { $_ } | Select-Object -Unique)
    $otherAccess = @($webApp.requiredResourceAccess | Where-Object { $_ -and $_.resourceAppId -ne $apiApp.appId })
    Invoke-Graph PATCH "/applications/$($webApp.id)" @{
        web                    = @{
            redirectUris          = $redirectUris
            implicitGrantSettings = @{ enableIdTokenIssuance = $false; enableAccessTokenIssuance = $false }
        }
        requiredResourceAccess = @($otherAccess) + @(@{
                resourceAppId  = $apiApp.appId
                resourceAccess = @(@{ id = $scopeId; type = 'Scope' })
            })
    } | Out-Null
    $webSp = Initialize-ServicePrincipal $webApp.appId

    # --- Admin consent -------------------------------------------------------------------------------------------
    $consentGranted = $false
    $consentUrl = "https://login.microsoftonline.com/$TenantId/adminconsent?client_id=$($webApp.appId)"
    try {
        $grantFilter = "`$filter=clientId eq '$($webSp.id)' and resourceId eq '$($apiSp.id)'"
        $grant = @((Invoke-Graph GET '/oauth2PermissionGrants' -Query $grantFilter).value) | Select-Object -First 1
        if (-not $grant) {
            Invoke-Graph POST '/oauth2PermissionGrants' @{
                clientId    = $webSp.id
                consentType = 'AllPrincipals'
                resourceId  = $apiSp.id
                scope       = $ScopeName
            } | Out-Null
        }
        elseif (($grant.scope -split ' ') -notcontains $ScopeName) {
            Invoke-Graph PATCH "/oauth2PermissionGrants/$($grant.id)" @{ scope = "$($grant.scope) $ScopeName".Trim() } | Out-Null
        }
        $consentGranted = $true
    }
    catch {
        Write-Warning "Admin consent could not be granted automatically: $($_.Exception.Message)"
        Write-Warning "A tenant administrator can grant it at: $consentUrl"
    }

    # --- Web client secret ---------------------------------------------------------------------------------------
    $storedSecret = $secrets['AzureAd:Web:ClientSecret']
    $storedClient = $secrets['AzureAd:Web:ClientId']
    $secretCreated = $false
    if ($RotateSecret -or -not $storedSecret -or $storedClient -ne $webApp.appId) {
        Write-Host 'Creating a web client secret (valid for 180 days)...'
        $credential = Invoke-Graph POST "/applications/$($webApp.id)/addPassword" @{
            passwordCredential = @{
                displayName = "$NamePrefix local development"
                endDateTime = (Get-Date).AddDays(180).ToUniversalTime().ToString('o')
            }
        }
        Set-UserSecret 'AzureAd:Web:ClientSecret' $credential.secretText
        $credential = $null
        $secretCreated = $true
    }

    # --- AppHost user-secrets ------------------------------------------------------------------------------------
    Set-UserSecret 'AzureAd:TenantId' $TenantId
    Set-UserSecret 'AzureAd:Web:ClientId' $webApp.appId
    Set-UserSecret 'AzureAd:Api:ClientId' $apiApp.appId
    Set-UserSecret 'AzureAd:Api:Scope' $ScopeName

    Write-Host ''
    Write-Host 'My Garage Entra setup complete' -ForegroundColor Green
    Write-Host "  Tenant            : $TenantId"
    Write-Host "  API client id     : $($apiApp.appId)  (scope api://$($apiApp.appId)/$ScopeName)"
    Write-Host "  Web client id     : $($webApp.appId)  (redirect $($desiredRedirectUris[0]))"
    Write-Host "  Web client secret : $(if ($secretCreated) { 'created and stored in the AppHost user-secrets' } else { 'already stored, left unchanged' })"
    Write-Host "  Admin consent     : $(if ($consentGranted) { 'granted' } else { 'NOT granted - see the warning above' })"
    Write-Host ''
    Write-Host 'Next: run the system with "aspire run" from the repository root, then browse to ' -NoNewline
    Write-Host $webOrigin -ForegroundColor Cyan
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
