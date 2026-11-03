#Requires -Version 7.0
<#
.SYNOPSIS
    Applies the GitHub repository settings the CI/CD pipeline relies on.

.DESCRIPTION
    Idempotent: each setting is compared with its current state and reported as created, updated, or unchanged.
      * Ruleset "protect-main" on the default branch: a pull request is required, the status check "verify" must pass,
        force pushes and deletion are blocked.
      * The default workflow token is read-only.
      * Optional: a required reviewer on environment "production" (self-review stays allowed so a single owner can approve
        their own deployment).
      * Optional: deployments to environment "production" are limited to the "main" branch.
    Run it after the first pipeline run has reported the "verify" check, because GitHub can only require a check it has seen.

.PARAMETER Repository
    Repository in owner/name form.

.PARAMETER RequiredReviewer
    GitHub login to add as a required reviewer on environment "production".

.PARAMETER RestrictEnvironmentToMain
    Limits deployments to environment "production" to the branch "main".

.PARAMETER WhatIf
    Reports what would change without changing anything.

.EXAMPLE
    ./infra/github/Initialize-GitHub.ps1 -WhatIf
    ./infra/github/Initialize-GitHub.ps1 -RequiredReviewer TK11703
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $Repository = 'TK11703/my-garage',
    [string] $RequiredReviewer,
    [switch] $RestrictEnvironmentToMain
)

$ErrorActionPreference = 'Stop'

$RulesetName = 'protect-main'
$EnvironmentName = 'production'
$RequiredCheck = 'verify'

function Invoke-Gh {
    param([string] $Method = 'GET', [string] $Path, $Body = $null, [switch] $AllowNotFound)

    $ghArgs = @('api', '--method', $Method, $Path)
    $output = if ($null -ne $Body) {
        ($Body | ConvertTo-Json -Depth 12 -Compress) | & gh @ghArgs --input - 2>&1
    }
    else {
        & gh @ghArgs 2>&1
    }

    if ($LASTEXITCODE -ne 0) {
        $text = ($output | Out-String).Trim()
        if ($AllowNotFound -and $text -match 'HTTP 404') { return $null }
        throw "gh api $Method $Path failed: $text"
    }

    $text = ($output | Out-String).Trim()
    if ($text) { return $text | ConvertFrom-Json -Depth 20 }
}

function Write-Result([string] $Name, [string] $State, [string] $Detail = '') {
    $color = switch ($State) { 'unchanged' { 'DarkGray' } 'created' { 'Green' } 'updated' { 'Green' } default { 'Cyan' } }
    Write-Host ("  {0,-34} {1}{2}" -f $Name, $State, $(if ($Detail) { " ($Detail)" } else { '' })) -ForegroundColor $color
}

try {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "'gh' was not found on PATH." }
    & gh auth status *> $null
    if ($LASTEXITCODE -ne 0) { throw 'You are not signed in to the GitHub CLI. Run: gh auth login' }

    $repo = Invoke-Gh -Path "repos/$Repository"
    if (-not $repo.permissions.admin) { throw "Your account is not an administrator of $Repository." }

    Write-Host "Repository $Repository$(if ($WhatIfPreference) { ' (what if)' })"

    # --- Ruleset on the default branch ---------------------------------------------------------------------------
    $desiredRules = @(
        @{ type = 'deletion' }
        @{ type = 'non_fast_forward' }
        @{
            type       = 'pull_request'
            parameters = @{
                required_approving_review_count   = 0
                dismiss_stale_reviews_on_push     = $false
                require_code_owner_review         = $false
                require_last_push_approval        = $false
                required_review_thread_resolution = $false
            }
        }
        @{
            type       = 'required_status_checks'
            parameters = @{
                strict_required_status_checks_policy = $false
                required_status_checks               = @(@{ context = $RequiredCheck })
            }
        }
    )
    $desiredRuleset = @{
        name        = $RulesetName
        target      = 'branch'
        enforcement = 'active'
        conditions  = @{ ref_name = @{ include = @('~DEFAULT_BRANCH'); exclude = @() } }
        rules       = $desiredRules
    }

    $existing = @(Invoke-Gh -Path "repos/$Repository/rulesets") | Where-Object { $_.name -eq $RulesetName } | Select-Object -First 1
    if (-not $existing) {
        if ($PSCmdlet.ShouldProcess("$Repository ruleset $RulesetName", 'create')) {
            Invoke-Gh -Method POST -Path "repos/$Repository/rulesets" -Body $desiredRuleset | Out-Null
            Write-Result "ruleset $RulesetName" 'created'
        }
        else { Write-Result "ruleset $RulesetName" 'would be created' }
    }
    else {
        $current = Invoke-Gh -Path "repos/$Repository/rulesets/$($existing.id)"
        $currentTypes = @($current.rules | ForEach-Object { $_.type } | Sort-Object)
        $desiredTypes = @($desiredRules | ForEach-Object { $_.type } | Sort-Object)
        $checks = @($current.rules | Where-Object type -eq 'required_status_checks' | ForEach-Object { $_.parameters.required_status_checks } | ForEach-Object { $_.context })
        $pullRequest = $current.rules | Where-Object type -eq 'pull_request' | Select-Object -First 1
        $same = $current.enforcement -eq 'active' -and
            ($currentTypes -join ',') -eq ($desiredTypes -join ',') -and
            $checks -contains $RequiredCheck -and
            ($current.conditions.ref_name.include -join ',') -eq '~DEFAULT_BRANCH' -and
            $null -ne $pullRequest
        if ($same) { Write-Result "ruleset $RulesetName" 'unchanged' }
        elseif ($PSCmdlet.ShouldProcess("$Repository ruleset $RulesetName", 'update')) {
            Invoke-Gh -Method PUT -Path "repos/$Repository/rulesets/$($existing.id)" -Body $desiredRuleset | Out-Null
            Write-Result "ruleset $RulesetName" 'updated'
        }
        else { Write-Result "ruleset $RulesetName" 'would be updated' }
    }

    # --- Default workflow token ----------------------------------------------------------------------------------
    $permissions = Invoke-Gh -Path "repos/$Repository/actions/permissions/workflow"
    if ($permissions.default_workflow_permissions -eq 'read' -and -not $permissions.can_approve_pull_request_reviews) {
        Write-Result 'default workflow token' 'unchanged' 'read-only'
    }
    elseif ($PSCmdlet.ShouldProcess("$Repository workflow token", 'make read-only')) {
        Invoke-Gh -Method PUT -Path "repos/$Repository/actions/permissions/workflow" -Body @{
            default_workflow_permissions     = 'read'
            can_approve_pull_request_reviews = $false
        } | Out-Null
        Write-Result 'default workflow token' 'updated' 'read-only'
    }
    else { Write-Result 'default workflow token' 'would be updated' 'read-only' }

    # --- Environment: reviewer and deployment branches -----------------------------------------------------------
    if ($RequiredReviewer -or $RestrictEnvironmentToMain) {
        $environment = Invoke-Gh -Path "repos/$Repository/environments/$EnvironmentName" -AllowNotFound
        $reviewerRule = $environment.protection_rules | Where-Object type -eq 'required_reviewers' | Select-Object -First 1
        $reviewers = @($reviewerRule.reviewers | ForEach-Object { @{ type = $_.type; id = $_.reviewer.id } })
        $changed = -not $environment

        if ($RequiredReviewer) {
            $user = Invoke-Gh -Path "users/$RequiredReviewer"
            if ($reviewers | Where-Object { $_.id -eq $user.id }) {
                Write-Result "reviewer $RequiredReviewer" 'unchanged'
            }
            else {
                $reviewers += @{ type = 'User'; id = $user.id }
                $changed = $true
                Write-Result "reviewer $RequiredReviewer" $(if ($WhatIfPreference) { 'would be added' } else { 'updated' })
            }
        }

        $policy = $environment.deployment_branch_policy
        $restricted = $policy -and $policy.custom_branch_policies
        $branchPolicies = @()
        if ($restricted) {
            $branchPolicies = @((Invoke-Gh -Path "repos/$Repository/environments/$EnvironmentName/deployment-branch-policies").branch_policies)
        }
        $onlyMain = $restricted -and $branchPolicies.Count -eq 1 -and $branchPolicies[0].name -eq 'main'
        $needsPolicy = $RestrictEnvironmentToMain -and -not $onlyMain
        if ($RestrictEnvironmentToMain) {
            if ($onlyMain) { Write-Result 'deployment branches' 'unchanged' 'main only' }
            else { $changed = $true; Write-Result 'deployment branches' $(if ($WhatIfPreference) { 'would be restricted' } else { 'updated' }) 'main only' }
        }

        if ($changed -and $PSCmdlet.ShouldProcess("$Repository environment $EnvironmentName", 'update protection')) {
            $body = @{
                prevent_self_review = $false
                reviewers           = $reviewers
            }
            if ($needsPolicy -or $restricted) { $body.deployment_branch_policy = @{ protected_branches = $false; custom_branch_policies = $true } }
            Invoke-Gh -Method PUT -Path "repos/$Repository/environments/$EnvironmentName" -Body $body | Out-Null

            if ($needsPolicy) {
                foreach ($policyItem in $branchPolicies) {
                    Invoke-Gh -Method DELETE -Path "repos/$Repository/environments/$EnvironmentName/deployment-branch-policies/$($policyItem.id)" | Out-Null
                }
                Invoke-Gh -Method POST -Path "repos/$Repository/environments/$EnvironmentName/deployment-branch-policies" -Body @{ name = 'main'; type = 'branch' } | Out-Null
            }
        }
    }

    Write-Host ''
    if ($WhatIfPreference) { Write-Host 'No changes were made.' -ForegroundColor Cyan }
    else { Write-Host 'GitHub repository settings are in the expected state.' -ForegroundColor Green }
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
