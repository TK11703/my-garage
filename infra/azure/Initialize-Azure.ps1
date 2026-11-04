#Requires -Version 7.0
<#
.SYNOPSIS
    One-time, repeatable Azure and GitHub preparation for the My Garage CI/CD pipeline.

.DESCRIPTION
    Idempotent: every step compares the current state first and reports created, updated, or unchanged. Nothing is deleted or
    replaced, nothing shared (cae-shared, accpsqlshared, acccrshared, aif-shared-acc) is reconfigured beyond the role assignments
    and database objects below, and no secret is created, stored, or printed.
      1. GitHub environment "production" (created only when missing; its protection rules are never changed here)
      2. App registration + service principal "mygarage-github-oidc" with federated credentials for GitHub Actions
      3. Its roles: Contributor and User Access Administrator on the subscription (temporary), AcrPush on the shared registry
      4. User-assigned identities id-mygarage-api and id-mygarage-web (infra/bicep/modules/identities.bicep)
      5. Entra registrations "MyGarage Azure API" and "MyGarage Azure Web" (infra/entra/Initialize-Entra.ps1)
      6. Database "mygarage" on the shared PostgreSQL server (infra/bicep/modules/platform-database.bicep)
      7. PostgreSQL Entra principal id-mygarage-api that owns the database; PUBLIC can no longer connect
      8. Remaining GitHub environment variables

.PARAMETER SubscriptionId
    Subscription to use. Defaults to the current Azure CLI subscription; a mismatch stops the script.

.PARAMETER ResourceGroup
    Resource group of the apps and identities.

.PARAMETER PlatformResourceGroup
    Resource group of the shared server, registry, AI account, and storage.

.PARAMETER Location
    Azure region.

.PARAMETER Repository
    GitHub repository (owner/name) that deploys.

.PARAMETER WhatIf
    Reports what would change without changing anything.

.PARAMETER TemporaryFirewallRule
    Adds a PostgreSQL firewall rule for this machine's public IP for the database step and removes it again afterwards.
    Use it when the server does not already allow this machine.

.EXAMPLE
    az login
    gh auth login
    ./infra/azure/Initialize-Azure.ps1 -WhatIf
    ./infra/azure/Initialize-Azure.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $SubscriptionId,
    [string] $ResourceGroup = 'rg-apps',
    [string] $PlatformResourceGroup = 'rg-platform',
    [string] $Location = 'northcentralus',
    [string] $Repository = 'TK11703/my-garage',
    [switch] $TemporaryFirewallRule
)

$ErrorActionPreference = 'Stop'

$EnvironmentName = 'production'
$PipelineAppName = 'mygarage-github-oidc'
$ContainerAppsEnvironment = 'cae-shared'
$Registry = 'acccrshared'
$PostgresServer = 'accpsqlshared'
$DatabaseName = 'mygarage'
$AiAccount = 'aif-shared-acc'
$ApiIdentity = 'id-mygarage-api'
$WebIdentity = 'id-mygarage-web'
$WebAppName = 'ca-web-mygarage'
$ExtractionEndpoint = 'https://aif-shared-acc.openai.azure.com'
$ExtractionDeployment = 'gpt-4.1-mini'
$bicepRoot = Join-Path $PSScriptRoot '..' 'bicep'
$entraScript = Join-Path $PSScriptRoot '..' 'entra' 'Initialize-Entra.ps1'

function Invoke-Az {
    param([string[]] $Arguments, [switch] $AllowFailure)
    $output = & az @Arguments --only-show-errors -o json 2>&1
    if ($LASTEXITCODE -ne 0) {
        if ($AllowFailure) { return $null }
        throw "az $($Arguments[0..([Math]::Min(2, $Arguments.Count - 1))] -join ' ') failed: $(($output | Out-String).Trim())"
    }
    $text = ($output | Out-String).Trim()
    if ($text) { return $text | ConvertFrom-Json -Depth 20 }
}

function Invoke-Gh {
    param([string] $Method = 'GET', [string] $Path, [switch] $AllowNotFound)
    $output = & gh api --method $Method $Path 2>&1
    if ($LASTEXITCODE -ne 0) {
        $text = ($output | Out-String).Trim()
        if ($AllowNotFound -and $text -match 'HTTP 404') { return $null }
        throw "gh api $Method $Path failed: $text"
    }
    $text = ($output | Out-String).Trim()
    if ($text) { return $text | ConvertFrom-Json -Depth 20 }
}

function Write-Step([int] $Number, [string] $Name, [string] $State, [string] $Detail = '') {
    $color = switch -Wildcard ($State) { 'unchanged' { 'DarkGray' } 'would*' { 'Cyan' } default { 'Green' } }
    Write-Host ("{0,2}. {1,-58} {2}{3}" -f $Number, $Name, $State, $(if ($Detail) { " ($Detail)" } else { '' })) -ForegroundColor $color
}

function Initialize-Npgsql {
    # The az rdbms-connect extension is unreliable on some machines, so SQL runs through Npgsql from a build of the repository.
    if ('Npgsql.NpgsqlConnection' -as [type]) { return }
    $root = Join-Path $PSScriptRoot '..' '..'
    $required = 'Microsoft.Extensions.Logging.Abstractions', 'Microsoft.Extensions.DependencyInjection.Abstractions', 'Npgsql'
    $dll = Get-ChildItem -Path $root -Filter 'Npgsql.dll' -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $dir = $_.DirectoryName; -not ($required | Where-Object { -not (Test-Path (Join-Path $dir "$_.dll")) }) } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $dll) { throw 'Npgsql.dll with its dependencies was not found. Run: dotnet build tests/MyGarage.Database.Tests' }
    foreach ($name in $required) { [void] [Reflection.Assembly]::LoadFrom((Join-Path $dll.DirectoryName "$name.dll")) }
}

function Invoke-Sql([string] $Database, [string] $Query) {
    Initialize-Npgsql
    $builder = [Npgsql.NpgsqlConnectionStringBuilder]::new()
    $builder.Host = "$PostgresServer.postgres.database.azure.com"
    $builder.Database = $Database
    $builder.Username = $script:pgAdminUser
    $builder.Password = $script:pgToken
    $builder.SslMode = 'Require'
    $builder.Timeout = 20
    $connection = [Npgsql.NpgsqlConnection]::new($builder.ConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        $reader = $command.ExecuteReader()
        $rows = @()
        while ($reader.Read()) {
            $row = [ordered]@{}
            for ($i = 0; $i -lt $reader.FieldCount; $i++) { $row[$reader.GetName($i)] = $reader.GetValue($i) }
            $rows += [pscustomobject] $row
        }
        $reader.Close()
        return $rows
    }
    catch { throw "SQL against '$Database' failed: $($_.Exception.Message)" }
    finally { $connection.Dispose() }
}

try {
    foreach ($tool in 'az', 'gh') {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "'$tool' was not found on PATH." }
    }
    $account = Invoke-Az @('account', 'show') -AllowFailure
    if (-not $account) { throw 'You are not signed in to the Azure CLI. Run: az login' }
    if ($SubscriptionId -and $account.id -ne $SubscriptionId) {
        throw "The Azure CLI is on subscription $($account.id), not $SubscriptionId. Run: az account set --subscription $SubscriptionId"
    }
    $SubscriptionId = $account.id
    $tenantId = $account.tenantId
    & gh auth status *> $null
    if ($LASTEXITCODE -ne 0) { throw 'You are not signed in to the GitHub CLI. Run: gh auth login' }

    $whatIf = [bool] $WhatIfPreference
    Write-Host "Subscription $($account.name) ($SubscriptionId), tenant $tenantId, repository $Repository$(if ($whatIf) { ' - what if' })"
    Write-Host ''

    $subscriptionScope = "/subscriptions/$SubscriptionId"
    $registryId = "$subscriptionScope/resourceGroups/$PlatformResourceGroup/providers/Microsoft.ContainerRegistry/registries/$Registry"

    # --- 1. GitHub environment ---------------------------------------------------------------------------------
    $environment = Invoke-Gh -Path "repos/$Repository/environments/$EnvironmentName" -AllowNotFound
    if ($environment) { Write-Step 1 "GitHub environment '$EnvironmentName'" 'unchanged' }
    elseif ($PSCmdlet.ShouldProcess("GitHub environment $EnvironmentName", 'create')) {
        Invoke-Gh -Method PUT -Path "repos/$Repository/environments/$EnvironmentName" | Out-Null
        Write-Step 1 "GitHub environment '$EnvironmentName'" 'created'
    }
    else { Write-Step 1 "GitHub environment '$EnvironmentName'" 'would be created' }

    # --- 2. Pipeline app registration and federated credentials -------------------------------------------------
    $subjectInfo = Invoke-Gh -Path "repos/$Repository/actions/oidc/customization/sub"
    $subjectPrefix = if ($subjectInfo.sub_claim_prefix) { $subjectInfo.sub_claim_prefix } else { "repo:$Repository" }
    $desiredCredentials = @(
        @{ name = 'github-environment-production'; subject = "${subjectPrefix}:environment:$EnvironmentName" }
        @{ name = 'github-main-branch'; subject = "${subjectPrefix}:ref:refs/heads/main" }
    )

    $app = @(Invoke-Az @('ad', 'app', 'list', '--display-name', $PipelineAppName)) | Where-Object displayName -eq $PipelineAppName | Select-Object -First 1
    $pipelineSp = $null
    if (-not $app) {
        if ($PSCmdlet.ShouldProcess("app registration $PipelineAppName", 'create')) {
            $app = Invoke-Az @('ad', 'app', 'create', '--display-name', $PipelineAppName, '--sign-in-audience', 'AzureADMyOrg')
            Write-Step 2 "App registration '$PipelineAppName'" 'created'
        }
        else { Write-Step 2 "App registration '$PipelineAppName'" 'would be created' }
    }
    else { Write-Step 2 "App registration '$PipelineAppName'" 'unchanged' }

    if ($app) {
        $pipelineSp = @(Invoke-Az @('ad', 'sp', 'list', '--filter', "appId eq '$($app.appId)'")) | Select-Object -First 1
        if (-not $pipelineSp) {
            if ($PSCmdlet.ShouldProcess("service principal of $PipelineAppName", 'create')) {
                $pipelineSp = Invoke-Az @('ad', 'sp', 'create', '--id', $app.appId)
                Write-Step 2 '   service principal' 'created'
            }
            else { Write-Step 2 '   service principal' 'would be created' }
        }

        $existingCredentials = @(Invoke-Az @('ad', 'app', 'federated-credential', 'list', '--id', $app.appId))
        foreach ($credential in $desiredCredentials) {
            $current = $existingCredentials | Where-Object name -eq $credential.name | Select-Object -First 1
            if ($current -and $current.subject -eq $credential.subject) {
                Write-Step 2 "   federated credential $($credential.name)" 'unchanged'
                continue
            }
            if ($PSCmdlet.ShouldProcess("federated credential $($credential.name)", 'create or update')) {
                $file = New-TemporaryFile
                try {
                    @{
                        name      = $credential.name
                        issuer    = 'https://token.actions.githubusercontent.com'
                        subject   = $credential.subject
                        audiences = @('api://AzureADTokenExchange')
                    } | ConvertTo-Json -Compress | Set-Content -Path $file -Encoding utf8NoBOM
                    if ($current) {
                        Invoke-Az @('ad', 'app', 'federated-credential', 'update', '--id', $app.appId, '--federated-credential-id', $current.id, '--parameters', "@$file") | Out-Null
                    }
                    else {
                        Invoke-Az @('ad', 'app', 'federated-credential', 'create', '--id', $app.appId, '--parameters', "@$file") | Out-Null
                    }
                }
                finally { Remove-Item -Path $file -Force -ErrorAction SilentlyContinue }
                Write-Step 2 "   federated credential $($credential.name)" $(if ($current) { 'updated' } else { 'created' })
            }
            else { Write-Step 2 "   federated credential $($credential.name)" 'would be created' }
        }
    }

    # --- 3. Role assignments of the pipeline identity ------------------------------------------------------------
    $roleTargets = @(
        @{ role = 'Contributor'; scope = $subscriptionScope; note = 'subscription, temporary' }
        @{ role = 'User Access Administrator'; scope = $subscriptionScope; note = 'subscription, temporary' }
        @{ role = 'AcrPush'; scope = $registryId; note = $Registry }
    )
    foreach ($target in $roleTargets) {
        $label = "Role $($target.role) ($($target.note))"
        $has = $false
        if ($pipelineSp) {
            $assigned = @(Invoke-Az @('role', 'assignment', 'list', '--assignee', $pipelineSp.id, '--scope', $target.scope, '--role', $target.role))
            $has = [bool] ($assigned | Where-Object { $_.scope -eq $target.scope })
        }
        if ($has) { Write-Step 3 $label 'unchanged' }
        elseif ($pipelineSp -and $PSCmdlet.ShouldProcess($label, 'assign')) {
            Invoke-Az @('role', 'assignment', 'create', '--assignee-object-id', $pipelineSp.id, '--assignee-principal-type', 'ServicePrincipal', '--role', $target.role, '--scope', $target.scope) | Out-Null
            Write-Step 3 $label 'created'
        }
        else { Write-Step 3 $label 'would be created' }
    }

    # --- 4. Managed identities ---------------------------------------------------------------------------------
    $identities = @{}
    foreach ($name in $ApiIdentity, $WebIdentity) {
        $identities[$name] = Invoke-Az @('identity', 'show', '--name', $name, '--resource-group', $ResourceGroup) -AllowFailure
    }
    if ($identities[$ApiIdentity] -and $identities[$WebIdentity]) {
        Write-Step 4 "Identities $ApiIdentity, $WebIdentity" 'unchanged'
    }
    elseif ($PSCmdlet.ShouldProcess("identities in $ResourceGroup", 'deploy')) {
        Invoke-Az @('deployment', 'group', 'create', '--resource-group', $ResourceGroup, '--name', 'mygarage-identities',
            '--template-file', (Join-Path $bicepRoot 'modules' 'identities.bicep'), '--parameters', "location=$Location") | Out-Null
        foreach ($name in $ApiIdentity, $WebIdentity) {
            $identities[$name] = Invoke-Az @('identity', 'show', '--name', $name, '--resource-group', $ResourceGroup)
        }
        Write-Step 4 "Identities $ApiIdentity, $WebIdentity" 'created'
    }
    else { Write-Step 4 "Identities $ApiIdentity, $WebIdentity" 'would be created' }

    # --- 5. Entra registrations ---------------------------------------------------------------------------------
    $domain = (Invoke-Az @('containerapp', 'env', 'show', '--name', $ContainerAppsEnvironment, '--resource-group', $ResourceGroup)).properties.defaultDomain
    $webBaseUri = "https://$WebAppName.$domain"
    $webPrincipalId = if ($identities[$WebIdentity]) { $identities[$WebIdentity].principalId } else { '00000000-0000-0000-0000-000000000000' }
    $entraArgs = @{
        NamePrefix                 = 'MyGarage Azure'
        WebBaseUri                 = $webBaseUri
        ManagedIdentityPrincipalId = $webPrincipalId
        GitHubEnvironment          = $EnvironmentName
        Repository                 = $Repository
        TenantId                   = $tenantId
    }
    if ($whatIf) { $entraArgs.WhatIf = $true }
    Write-Host ' 5. Entra registrations (infra/entra/Initialize-Entra.ps1)'
    & pwsh -NoProfile -File $entraScript @(foreach ($key in $entraArgs.Keys) {
            if ($entraArgs[$key] -is [bool]) { "-$key" } else { "-$key"; $entraArgs[$key] }
        })
    if ($LASTEXITCODE -ne 0) { throw 'Initialize-Entra.ps1 failed.' }

    # --- 6. Database ---------------------------------------------------------------------------------------------
    $database = @(Invoke-Az @('postgres', 'flexible-server', 'db', 'list', '--resource-group', $PlatformResourceGroup, '--server-name', $PostgresServer)) |
        Where-Object { $_.name -eq $DatabaseName } | Select-Object -First 1
    if ($database) { Write-Step 6 "Database '$DatabaseName' on $PostgresServer" 'unchanged' }
    elseif ($PSCmdlet.ShouldProcess("database $DatabaseName", 'create')) {
        Invoke-Az @('deployment', 'group', 'create', '--resource-group', $PlatformResourceGroup, '--name', 'mygarage-database',
            '--template-file', (Join-Path $bicepRoot 'modules' 'platform-database.bicep'), '--parameters', "serverName=$PostgresServer", "databaseName=$DatabaseName") | Out-Null
        Write-Step 6 "Database '$DatabaseName' on $PostgresServer" 'created'
        $database = $true
    }
    else { Write-Step 6 "Database '$DatabaseName' on $PostgresServer" 'would be created' }

    # --- 7. Database principal and ownership -------------------------------------------------------------------------
    if ($database -and $identities[$ApiIdentity]) {
        $me = Invoke-Az @('ad', 'signed-in-user', 'show')
        $admins = @(Invoke-Az @('postgres', 'flexible-server', 'microsoft-entra-admin', 'list', '--resource-group', $PlatformResourceGroup, '--server-name', $PostgresServer))
        $admin = $admins | Where-Object objectId -eq $me.id | Select-Object -First 1
        if (-not $admin) { throw "The signed-in account is not a Microsoft Entra administrator of $PostgresServer, so it cannot create the database principal." }
        $script:pgAdminUser = $admin.principalName
        $script:pgToken = (& az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv --only-show-errors).Trim()

        $ruleName = $null
        if ($TemporaryFirewallRule -and -not $whatIf) {
            $myIp = (Invoke-RestMethod -Uri 'https://api.ipify.org').ToString().Trim()
            $ruleName = "mygarage-setup-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"
            Invoke-Az @('postgres', 'flexible-server', 'firewall-rule', 'create', '--resource-group', $PlatformResourceGroup, '--server-name', $PostgresServer,
                '--name', $ruleName, '--start-ip-address', $myIp, '--end-ip-address', $myIp) | Out-Null
            Write-Host "    temporary firewall rule $ruleName for $myIp (removed afterwards)"
            Start-Sleep -Seconds 30
        }
        try {

        $role = @(Invoke-Sql 'postgres' "SELECT 1 AS present FROM pg_roles WHERE rolname = '$ApiIdentity'")
        if ($role.Count -gt 0) { Write-Step 7 "Database principal '$ApiIdentity'" 'unchanged' }
        elseif ($PSCmdlet.ShouldProcess("database principal $ApiIdentity", 'create')) {
            Invoke-Sql 'postgres' "SELECT * FROM pgaadauth_create_principal('$ApiIdentity', false, false)" | Out-Null
            Write-Step 7 "Database principal '$ApiIdentity'" 'created'
        }
        else { Write-Step 7 "Database principal '$ApiIdentity'" 'would be created' }

        $owner = @(Invoke-Sql 'postgres' "SELECT pg_get_userbyid(datdba) AS owner FROM pg_database WHERE datname = '$DatabaseName'")
        if ($owner.Count -gt 0 -and ($owner[0].owner -eq $ApiIdentity)) { Write-Step 7 "Database owner '$ApiIdentity'" 'unchanged' }
        elseif ($PSCmdlet.ShouldProcess("database $DatabaseName", "make $ApiIdentity the owner")) {
            try { Invoke-Sql 'postgres' "ALTER DATABASE $DatabaseName OWNER TO `"$ApiIdentity`"" | Out-Null }
            catch {
                # Changing the owner requires membership of the new owner; grant it for the change and take it back.
                Invoke-Sql 'postgres' "GRANT `"$ApiIdentity`" TO `"$($script:pgAdminUser)`"" | Out-Null
                try { Invoke-Sql 'postgres' "ALTER DATABASE $DatabaseName OWNER TO `"$ApiIdentity`"" | Out-Null }
                finally { Invoke-Sql 'postgres' "REVOKE `"$ApiIdentity`" FROM `"$($script:pgAdminUser)`"" | Out-Null }
            }
            Write-Step 7 "Database owner '$ApiIdentity'" 'updated'
        }
        else { Write-Step 7 "Database owner '$ApiIdentity'" 'would be updated' }

        $publicConnect = @(Invoke-Sql 'postgres' "SELECT count(*) AS grants FROM pg_database d, aclexplode(coalesce(d.datacl, acldefault('d', d.datdba))) a WHERE d.datname = '$DatabaseName' AND a.grantee = 0 AND a.privilege_type = 'CONNECT'")
        if ($publicConnect.Count -gt 0 -and [int] $publicConnect[0].grants -eq 0) { Write-Step 7 'PUBLIC connect on the database revoked' 'unchanged' }
        elseif ($PSCmdlet.ShouldProcess("database $DatabaseName", 'revoke PUBLIC connect')) {
            Invoke-Sql 'postgres' "REVOKE CONNECT ON DATABASE $DatabaseName FROM PUBLIC" | Out-Null
            Write-Step 7 'PUBLIC connect on the database revoked' 'updated'
        }
        else { Write-Step 7 'PUBLIC connect on the database revoked' 'would be updated' }
        }
        finally {
            if ($ruleName) {
                Invoke-Az @('postgres', 'flexible-server', 'firewall-rule', 'delete', '--resource-group', $PlatformResourceGroup, '--server-name', $PostgresServer, '--name', $ruleName, '--yes') -AllowFailure | Out-Null
                Write-Host "    temporary firewall rule $ruleName removed"
            }
        }
    }
    else { Write-Step 7 'Database principal, ownership, PUBLIC connect' 'would be applied after steps 4 and 6' }

    # --- 8. GitHub variables ---------------------------------------------------------------------------------------
    # The sign-in identifiers are repository variables because the build-images job has no environment (so no approval
    # gate) and still needs them; the extraction settings belong to the production environment.
    $repositoryVariables = [ordered]@{
        AZURE_CLIENT_ID       = if ($app) { $app.appId } else { '<pipeline app id>' }
        AZURE_TENANT_ID       = $tenantId
        AZURE_SUBSCRIPTION_ID = $SubscriptionId
    }
    $environmentVariables = [ordered]@{
        EXTRACTION_ENDPOINT   = $ExtractionEndpoint
        EXTRACTION_DEPLOYMENT = $ExtractionDeployment
    }

    function Sync-GitHubVariables([string] $Label, $Desired, [string[]] $ScopeArguments, [bool] $Exists) {
        $current = @{}
        if ($Exists) {
            $listed = & gh variable list @ScopeArguments --repo $Repository --json name,value 2>$null
            if ($LASTEXITCODE -eq 0 -and $listed) { foreach ($item in ($listed | ConvertFrom-Json)) { $current[$item.name] = $item.value } }
        }
        $changed = @($Desired.Keys | Where-Object { $current[$_] -ne $Desired[$_] })
        if ($changed.Count -eq 0) { Write-Step 8 $Label 'unchanged'; return }
        if ($app -and $PSCmdlet.ShouldProcess("$Label $($changed -join ', ')", 'set')) {
            foreach ($name in $changed) {
                & gh variable set $name @ScopeArguments --repo $Repository --body $Desired[$name] *> $null
                if ($LASTEXITCODE -ne 0) { throw "Could not set variable '$name'." }
            }
            Write-Step 8 $Label 'updated' ($changed -join ', ')
        }
        else { Write-Step 8 $Label 'would be set' ($changed -join ', ') }
    }

    Sync-GitHubVariables 'GitHub repository variables' $repositoryVariables @() $true
    Sync-GitHubVariables 'GitHub environment variables' $environmentVariables @('--env', $EnvironmentName) ([bool] $environment)
    Write-Host ''
    if ($whatIf) { Write-Host 'No changes were made.' -ForegroundColor Cyan }
    else {
        Write-Host 'Azure preparation complete.' -ForegroundColor Green
        Write-Host 'Next: run ./infra/github/Initialize-GitHub.ps1 after the first pipeline run, then merge to deploy.'
    }
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
