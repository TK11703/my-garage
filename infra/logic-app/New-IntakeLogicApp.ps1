#Requires -Version 7.0
<#
.SYNOPSIS
    Creates or updates the My Garage email-intake Logic App. Not part of the deployment pipeline.

.DESCRIPTION
    Idempotent: each step reports created, updated, or unchanged. The Logic App (Consumption) watches the mailbox behind the
    Outlook connection you provide and posts each email to POST <api>/api/intake/emails, authenticating with the user-assigned
    managed identity id-mygarage-intake, which is granted the Email.Ingest app role on "MyGarage Azure API".
      1. Creates the Outlook.com connection from outlook-connection.bicep when it is missing (never consents to it: the mailbox
         owner authorises it once in the Azure portal)
      2. Creates the content-conversion connection (no sign-in needed) used to turn the HTML body into text
      3. Creates the user-assigned identity id-mygarage-intake
      4. Creates or updates the Logic App from workflow.definition.json
      5. Grants the identity the Email.Ingest app role through Microsoft Graph
    No secret exists in this flow and none is printed.

.PARAMETER OutlookConnectionName
    Name of the Microsoft.Web/connections resource for the Outlook.com connector. Created when missing; the intake mailbox owner
    must then authorise it in the Azure portal.

.PARAMETER ResourceGroup
    Resource group of the Logic App, identity, and conversion connection.

.PARAMETER Location
    Azure region; must equal the region of the Outlook connection.

.PARAMETER Name
    Logic App name.

.PARAMETER IdentityName
    User-assigned identity of the Logic App.

.PARAMETER ApiAddress
    https address of the deployed API. Defaults to the address of ca-api-mygarage.

.PARAMETER ApiClientId
    Client id of the "MyGarage Azure API" registration. Defaults to the registration found by name.

.PARAMETER PollIntervalMinutes
    How often the trigger checks the mailbox.

.PARAMETER WhatIf
    Reports what would change without changing anything.

.EXAMPLE
    ./infra/logic-app/New-IntakeLogicApp.ps1 -WhatIf
    ./infra/logic-app/New-IntakeLogicApp.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $OutlookConnectionName = 'con-mygarage-outlook',
    [string] $ResourceGroup = 'rg-apps',
    [string] $Location = 'northcentralus',
    [string] $Name = 'logic-mygarage-intake',
    [string] $IdentityName = 'id-mygarage-intake',
    [string] $ApiAddress,
    [string] $ApiClientId,
    [ValidateRange(1, 1440)] [int] $PollIntervalMinutes = 5
)

$ErrorActionPreference = 'Stop'

$ApiAppName = 'MyGarage Azure API'
$ApiContainerApp = 'ca-api-mygarage'
$IngestRole = 'Email.Ingest'
$ConversionConnectionName = 'con-mygarage-conversion'
$definitionPath = Join-Path $PSScriptRoot 'workflow.definition.json'
$outlookTemplate = Join-Path $PSScriptRoot 'outlook-connection.bicep'

function Invoke-Az {
    param([string[]] $Arguments, [switch] $AllowFailure)
    $output = & az @Arguments --only-show-errors -o json 2>&1
    if ($LASTEXITCODE -ne 0) {
        if ($AllowFailure) { return $null }
        throw "az $($Arguments[0..([Math]::Min(2, $Arguments.Count - 1))] -join ' ') failed: $(($output | Out-String).Trim())"
    }
    $text = ($output | Out-String).Trim()
    if ($text) { return $text | ConvertFrom-Json -Depth 50 }
}

function Invoke-Rest {
    param([string] $Method, [string] $Url, $Body = $null, [string[]] $Query = @(), [switch] $AllowFailure)
    $azArgs = @('rest', '--method', $Method, '--url', $Url)
    if ($Query.Count -gt 0) { $azArgs += @('--uri-parameters') + $Query }
    $file = $null
    if ($null -ne $Body) {
        $file = New-TemporaryFile
        ($Body | ConvertTo-Json -Depth 50 -Compress) | Set-Content -Path $file -Encoding utf8NoBOM
        $azArgs += @('--headers', 'Content-Type=application/json', '--body', "@$file")
    }
    try { return Invoke-Az $azArgs -AllowFailure:$AllowFailure }
    finally { if ($file) { Remove-Item -Path $file -Force -ErrorAction SilentlyContinue } }
}

function Write-Step([string] $Name, [string] $State, [string] $Detail = '') {
    $color = switch -Wildcard ($State) { 'unchanged' { 'DarkGray' } 'would*' { 'Cyan' } default { 'Green' } }
    Write-Host ("  {0,-52} {1}{2}" -f $Name, $State, $(if ($Detail) { " ($Detail)" } else { '' })) -ForegroundColor $color
}

# Hashtable key order differs between runs, so the state hash needs sorted keys.
function ConvertTo-Canonical($Value) {
    if ($Value -is [System.Collections.IDictionary]) {
        $sorted = [ordered]@{}
        foreach ($key in ($Value.Keys | Sort-Object -CaseSensitive)) { $sorted[$key] = ConvertTo-Canonical $Value[$key] }
        return $sorted
    }
    if ($Value -is [System.Collections.IList]) { return , @($Value | ForEach-Object { ConvertTo-Canonical $_ }) }
    return $Value
}

try {
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) { throw "'az' was not found on PATH." }
    $account = Invoke-Az @('account', 'show') -AllowFailure
    if (-not $account) { throw 'You are not signed in to the Azure CLI. Run: az login' }
    $subscriptionId = $account.id
    $armBase = 'https://management.azure.com'
    $groupScope = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup"
    $whatIf = [bool] $WhatIfPreference

    Write-Host "Logic App $Name in $ResourceGroup ($Location)$(if ($whatIf) { ' - what if' })"

    # --- Inputs --------------------------------------------------------------------------------------------------
    if (-not $ApiAddress) {
        $fqdn = (Invoke-Az @('containerapp', 'show', '--name', $ApiContainerApp, '--resource-group', $ResourceGroup, '--query', 'properties.configuration.ingress.fqdn') -AllowFailure)
        if (-not $fqdn) { throw "The API '$ApiContainerApp' is not deployed yet. Deploy it first or pass -ApiAddress." }
        $ApiAddress = "https://$fqdn"
    }
    $ApiAddress = $ApiAddress.TrimEnd('/')

    $apiApp = @(Invoke-Az @('ad', 'app', 'list', '--display-name', $ApiAppName)) | Where-Object displayName -eq $ApiAppName | Select-Object -First 1
    if (-not $ApiClientId) {
        if (-not $apiApp) { throw "App registration '$ApiAppName' was not found. Run infra/azure/Initialize-Azure.ps1 first or pass -ApiClientId." }
        $ApiClientId = $apiApp.appId
    }

    # --- Outlook connection (created here, authorised by the mailbox owner) --------------------------------------
    $outlookApiId = "/subscriptions/$subscriptionId/providers/Microsoft.Web/locations/$Location/managedApis/outlook"
    $outlookConnectionId = "$groupScope/providers/Microsoft.Web/connections/$OutlookConnectionName"
    $outlookAuthorised = $false
    $connection = Invoke-Az @('resource', 'show', '--ids', $outlookConnectionId) -AllowFailure
    if ($connection) {
        if ($connection.properties.api.id -notmatch '/managedApis/outlook$') { throw "'$OutlookConnectionName' is not an Outlook.com (outlook) connection." }
        if ($connection.location.Replace(' ', '').ToLower() -ne $Location.ToLower()) {
            throw "The connection is in $($connection.location) but the Logic App is in $Location; they must match."
        }
        $outlookAuthorised = @($connection.properties.statuses | ForEach-Object { $_.status }) -contains 'Connected'
        Write-Step 'Outlook connection' 'unchanged' "$OutlookConnectionName, $(if ($outlookAuthorised) { 'connected' } else { 'waiting for authorisation' })"
    }
    elseif ($PSCmdlet.ShouldProcess($OutlookConnectionName, 'create')) {
        Invoke-Az @('deployment', 'group', 'create', '--resource-group', $ResourceGroup, '--name', 'mygarage-outlook-connection',
            '--template-file', $outlookTemplate, '--parameters', "connectionName=$OutlookConnectionName", "location=$Location") | Out-Null
        Write-Step 'Outlook connection' 'created' 'waiting for authorisation'
    }
    else { Write-Step 'Outlook connection' 'would be created' }

    # --- Content-conversion connection ----------------------------------------------------------------------
    $conversionId = "$groupScope/providers/Microsoft.Web/connections/$ConversionConnectionName"
    $conversion = Invoke-Az @('resource', 'show', '--ids', $conversionId) -AllowFailure
    if ($conversion) { Write-Step "Connection $ConversionConnectionName" 'unchanged' }
    elseif ($PSCmdlet.ShouldProcess($ConversionConnectionName, 'create')) {
        Invoke-Rest PUT "$armBase$conversionId" @{
            location   = $Location
            properties = @{
                displayName = 'My Garage content conversion'
                api         = @{ id = "/subscriptions/$subscriptionId/providers/Microsoft.Web/locations/$Location/managedApis/conversionservice" }
            }
        } -Query 'api-version=2016-06-01' | Out-Null
        Write-Step "Connection $ConversionConnectionName" 'created'
    }
    else { Write-Step "Connection $ConversionConnectionName" 'would be created' }

    # --- Identity ----------------------------------------------------------------------------------------------
    $identity = Invoke-Az @('identity', 'show', '--name', $IdentityName, '--resource-group', $ResourceGroup) -AllowFailure
    if ($identity) { Write-Step "Identity $IdentityName" 'unchanged' }
    elseif ($PSCmdlet.ShouldProcess($IdentityName, 'create')) {
        $identity = Invoke-Az @('identity', 'create', '--name', $IdentityName, '--resource-group', $ResourceGroup, '--location', $Location)
        Write-Step "Identity $IdentityName" 'created'
    }
    else { Write-Step "Identity $IdentityName" 'would be created' }
    $identityId = "$groupScope/providers/Microsoft.ManagedIdentity/userAssignedIdentities/$IdentityName"

    # --- Logic App ---------------------------------------------------------------------------------------------
    $definition = Get-Content -Path $definitionPath -Raw | ConvertFrom-Json -Depth 50 -AsHashtable
    $definition.triggers['When_a_new_email_arrives'].recurrence.interval = $PollIntervalMinutes
    $parameters = @{
        '$connections'     = @{
            value = @{
                outlook         = @{
                    connectionId   = $outlookConnectionId
                    connectionName = $OutlookConnectionName
                    id             = $outlookApiId
                }
                conversionservice = @{
                    connectionId   = $conversionId
                    connectionName = $ConversionConnectionName
                    id             = "/subscriptions/$subscriptionId/providers/Microsoft.Web/locations/$Location/managedApis/conversionservice"
                }
            }
        }
        ApiAddress         = @{ value = $ApiAddress }
        ApiClientId        = @{ value = $ApiClientId }
        IdentityResourceId = @{ value = $identityId }
    }

    # A hash of the desired state is kept in a tag so a repeat run can tell whether anything changed.
    $desiredJson = ConvertTo-Canonical @{ definition = $definition; parameters = $parameters; identity = $identityId } | ConvertTo-Json -Depth 50 -Compress
    $hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($desiredJson))).ToLower()
    $workflowUrl = "$armBase$groupScope/providers/Microsoft.Logic/workflows/$Name"
    $current = Invoke-Rest GET $workflowUrl -Query 'api-version=2019-05-01' -AllowFailure

    if ($current -and $current.tags.definitionHash -eq $hash -and $current.properties.state -eq 'Enabled') {
        Write-Step "Logic App $Name" 'unchanged' "every $PollIntervalMinutes min"
    }
    elseif ($PSCmdlet.ShouldProcess($Name, $(if ($current) { 'update' } else { 'create' }))) {
        Invoke-Rest PUT $workflowUrl @{
            location   = $Location
            tags       = @{ definitionHash = $hash }
            identity   = @{ type = 'UserAssigned'; userAssignedIdentities = @{ $identityId = @{} } }
            properties = @{ state = 'Enabled'; definition = $definition; parameters = $parameters }
        } -Query 'api-version=2019-05-01' | Out-Null
        Write-Step "Logic App $Name" $(if ($current) { 'updated' } else { 'created' }) "every $PollIntervalMinutes min"
    }
    else { Write-Step "Logic App $Name" $(if ($current) { 'would be updated' } else { 'would be created' }) }

    # --- Email.Ingest role ------------------------------------------------------------------------------------------
    $apiSp = @(Invoke-Az @('ad', 'sp', 'list', '--filter', "appId eq '$ApiClientId'")) | Select-Object -First 1
    if (-not $apiSp) { throw "The service principal of '$ApiAppName' was not found." }
    $roleId = ($apiSp.appRoles | Where-Object value -eq $IngestRole | Select-Object -First 1).id
    if (-not $roleId) { throw "The app role '$IngestRole' does not exist on '$ApiAppName'." }

    if ($identity) {
        $assignments = @((Invoke-Rest GET "https://graph.microsoft.com/v1.0/servicePrincipals/$($apiSp.id)/appRoleAssignedTo").value)
        $has = [bool] ($assignments | Where-Object { $_.principalId -eq $identity.principalId -and $_.appRoleId -eq $roleId })
        if ($has) { Write-Step "App role $IngestRole for $IdentityName" 'unchanged' }
        elseif ($PSCmdlet.ShouldProcess("$IdentityName", "assign $IngestRole")) {
            Invoke-Rest POST "https://graph.microsoft.com/v1.0/servicePrincipals/$($apiSp.id)/appRoleAssignedTo" @{
                principalId = $identity.principalId
                resourceId  = $apiSp.id
                appRoleId   = $roleId
            } | Out-Null
            Write-Step "App role $IngestRole for $IdentityName" 'created'
        }
        else { Write-Step "App role $IngestRole for $IdentityName" 'would be created' }
    }
    else { Write-Step "App role $IngestRole for $IdentityName" 'would be created after the identity exists' }

    Write-Host ''
    if ($whatIf) { Write-Host 'No changes were made.' -ForegroundColor Cyan }
    else {
        Write-Host "Logic App ready: $Name posts to $ApiAddress/api/intake/emails every $PollIntervalMinutes minute(s)." -ForegroundColor Green
        if (-not $outlookAuthorised) {
            Write-Host "Next: in the Azure portal open the API connection '$OutlookConnectionName' in $ResourceGroup, choose Edit API connection, Authorize, sign in as the intake mailbox, and Save. The Logic App cannot read mail until then." -ForegroundColor Yellow
        }
    }
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
