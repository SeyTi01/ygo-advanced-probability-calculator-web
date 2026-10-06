#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch] $Full,
    [string] $RepositoryRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
}
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
. (Join-Path $PSScriptRoot 'Policy.ps1')

function Invoke-RepositoryGit {
    param([string[]] $Arguments)
    $output = & git -C $RepositoryRoot -c "safe.directory=$RepositoryRoot" --no-optional-locks @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Git command failed: git $($Arguments -join ' ')`n$($output -join "`n")"
    }
    @($output | ForEach-Object { [string]$_ })
}

function Test-CommitAvailable {
    param([string] $Sha)
    if ($Sha -notmatch '^[0-9a-fA-F]{40,64}$' -or $Sha -match '^0+$') { return $false }
    try {
        [void](Invoke-RepositoryGit -Arguments @('cat-file', '-e', "$Sha^{commit}"))
        return $true
    }
    catch { return $false }
}

function Get-WorktreeState {
    if (Test-Path -LiteralPath (Join-Path $RepositoryRoot '.git')) {
        return ((Invoke-RepositoryGit -Arguments @('status', '--porcelain=v1', '--short', '--untracked-files=all')) -join "`n")
    }
    Get-StyleSourceSnapshot -RepositoryRoot $RepositoryRoot
}

function Get-CSharpRelativePaths {
    param([string[]] $Paths)
    @($Paths | ForEach-Object { $_.Replace('\', '/') } | Where-Object {
        $_ -match '^(YGOProbabilityCalculatorBlazor|YGOProbabilityCalculatorBlazorTest)/.+\.cs$'
    } | Sort-Object -Unique)
}

$timer = [Diagnostics.Stopwatch]::StartNew()
$sourceSnapshotBefore = Get-StyleSourceSnapshot -RepositoryRoot $RepositoryRoot
$failureMessage = $null
$worktreeStateBefore = $null
try {
    $worktreeStateBefore = Get-WorktreeState
    $eventName = [string]$env:GITHUB_EVENT_NAME
    $changedPaths = @()
    $scanFullRepository = $Full.IsPresent

    if (-not $scanFullRepository -and $eventName -eq 'pull_request') {
        if ([string]::IsNullOrWhiteSpace($env:GITHUB_EVENT_PATH)) {
            $scanFullRepository = $true
        }
        else {
            $event = Get-Content -LiteralPath $env:GITHUB_EVENT_PATH -Raw | ConvertFrom-Json
            $baseSha = [string]$event.pull_request.base.sha
            $headSha = [string]$event.pull_request.head.sha
            if (-not (Test-CommitAvailable -Sha $baseSha) -or -not (Test-CommitAvailable -Sha $headSha)) {
                $scanFullRepository = $true
            }
            else {
                $changedPaths = @(Invoke-RepositoryGit -Arguments @('diff', '--name-only', '--no-renames', $baseSha, $headSha, '--'))
            }
        }
    }
    elseif (-not $scanFullRepository -and $eventName -eq 'push') {
        if ([string]::IsNullOrWhiteSpace($env:GITHUB_EVENT_PATH)) {
            $scanFullRepository = $true
        }
        else {
            $event = Get-Content -LiteralPath $env:GITHUB_EVENT_PATH -Raw | ConvertFrom-Json
            $beforeSha = [string]$event.before
            $afterSha = [string]$env:GITHUB_SHA
            if ($beforeSha -match '^0+$' -or
                -not (Test-CommitAvailable -Sha $beforeSha) -or
                -not (Test-CommitAvailable -Sha $afterSha)) {
                $scanFullRepository = $true
            }
            else {
                $changedPaths = @(Invoke-RepositoryGit -Arguments @('diff', '--name-only', '--no-renames', $beforeSha, $afterSha, '--'))
            }
        }
    }
    elseif (-not $scanFullRepository -and $eventName -eq 'workflow_dispatch') {
        $scanFullRepository = $true
    }
    elseif (-not $scanFullRepository -and [string]::IsNullOrWhiteSpace($eventName)) {
        $hasGit = $true
        try { [void](Invoke-RepositoryGit -Arguments @('rev-parse', '--show-toplevel')) }
        catch { $hasGit = $false }
        if ($hasGit) {
            $changedPaths = @(Invoke-RepositoryGit -Arguments @('diff', '--name-only', '--no-renames', 'HEAD', '--'))
            $changedPaths += @(Invoke-RepositoryGit -Arguments @('ls-files', '--others', '--exclude-standard'))
            if ($changedPaths.Count -eq 0) { $scanFullRepository = $true }
        }
        else {
            $scanFullRepository = $true
        }
    }
    elseif (-not $scanFullRepository) {
        $scanFullRepository = $true
    }

    $changedPaths = @($changedPaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    $infrastructurePatterns = @(
        '^\.editorconfig$',
        '^global\.json$',
        '^\.config/dotnet-tools\.json$',
        '^scripts/style/',
        '^\.github/workflows/tests\.yml$'
    )
    $infrastructureChanged = @($changedPaths | Where-Object {
        $path = $_.Replace('\', '/')
        @($infrastructurePatterns | Where-Object { $path -match $_ }).Count -gt 0
    }).Count -gt 0

    $stylePolicy = Get-StylePolicy -RepositoryRoot $RepositoryRoot
    if ($scanFullRepository -or $infrastructureChanged) {
        $relativePaths = @(Get-StyleSources -RepositoryRoot $RepositoryRoot | ForEach-Object {
            $_.FullName.Substring($RepositoryRoot.Length).TrimStart([char[]]@('\', '/')).Replace('\', '/')
        })
    }
    else {
        $relativePaths = @(Get-CSharpRelativePaths -Paths $changedPaths)
        $relativePaths = @($relativePaths | Where-Object {
            Test-Path -LiteralPath (Join-Path $RepositoryRoot ($_.Replace('/', [IO.Path]::DirectorySeparatorChar))) -PathType Leaf
        })
    }

    if ($relativePaths.Count -eq 0) {
        Write-Host 'No changed C# sources require style verification.'
    }
    else {
        $sources = @($relativePaths | ForEach-Object { Join-Path $RepositoryRoot $_ })
        Write-Host ("Checking {0} C# source file(s){1}." -f $sources.Count, $(if ($scanFullRepository -or $infrastructureChanged) { ' across the repository' } else { ' changed by this event' }))
        $issues = [Collections.Generic.List[string]]::new()
        foreach ($source in $sources) {
            foreach ($issue in Get-StyleTextViolations -Path $source -Policy $stylePolicy) {
                $issues.Add("$($source.Substring($RepositoryRoot.Length + 1))`: $issue")
            }
        }
        if ($issues.Count -gt 0) { throw "C# text policy violations:`n$($issues -join "`n")" }
        Write-Host 'PASS: text/encoding policy'

        Push-Location $RepositoryRoot
        try {
            # Only this SDK-local helper is built, never the application. Check mode cannot write source.
            $layoutArguments = @('run', '--project', 'scripts/style/InvocationLayout/InvocationLayout.csproj',
                '--no-launch-profile', '--verbosity', 'quiet', '--', '--check', '--policy', '.editorconfig') + $relativePaths
            $layoutOutput = @(& dotnet @layoutArguments 2>&1)
            $layoutExitCode = $LASTEXITCODE
            $layoutOutput | ForEach-Object { Write-Host $_ }
            if ($layoutExitCode -ne 0) { throw "Syntax/trivia and invocation layout verification failed:`n$($layoutOutput -join "`n")" }
            Write-Host 'PASS: syntax/trivia and invocation layout policy'

            # Semantic style diagnostics need project evaluation and restored reference assemblies.
            # Reuse existing assets locally; on a fresh checkout dotnet format performs its own restore.
            $hasAssets = (Test-Path 'YGOProbabilityCalculatorBlazor/obj/project.assets.json') -and
                (Test-Path 'YGOProbabilityCalculatorBlazorTest/obj/project.assets.json')
            $formatArguments = @('format', 'style', 'YGOProbabilityCalculatorBlazor.sln',
                '--severity', 'info', '--verify-no-changes', '--verbosity', 'minimal')
            if ($hasAssets) { $formatArguments += '--no-restore' }
            $formatArguments += @('--diagnostics') + @($stylePolicy.DiagnosticIds) + @('--include') + $relativePaths
            & dotnet @formatArguments
            if ($LASTEXITCODE -ne 0) { throw "Roslyn style verification failed ($($stylePolicy.DiagnosticIds -join ', '))." }
            Write-Host 'PASS: Roslyn style diagnostics'
        }
        finally { Pop-Location }

    }
}
catch {
    $failureMessage = $_.Exception.Message
}
finally {
    if ((Get-StyleSourceSnapshot -RepositoryRoot $RepositoryRoot) -cne $sourceSnapshotBefore) {
        $failureMessage += "`nThe style check changed source bytes; this violates the read-only contract."
    }
    if ($null -ne $worktreeStateBefore) {
        try {
            $worktreeStateAfter = Get-WorktreeState
            if ($worktreeStateBefore -cne $worktreeStateAfter) {
                $statusFailure = 'The style check changed the checkout; this violates the non-mutating check contract.'
                if ($failureMessage) { $failureMessage += "`n$statusFailure" } else { $failureMessage = $statusFailure }
            }
        }
        catch {
            if ($failureMessage) { $failureMessage += "`nCould not verify checkout state after checking: $($_.Exception.Message)" }
            else { $failureMessage = "Could not verify checkout state after checking: $($_.Exception.Message)" }
        }
    }
    $timer.Stop()
    Write-Host ("C# style verification elapsed: {0:N1}s" -f $timer.Elapsed.TotalSeconds)
}

if ($failureMessage) { throw "$failureMessage`nRun pwsh -NoProfile -File scripts/style/fix.ps1, then re-run this check." }
Write-Host 'C# style verification passed; the checked-out worktree is unchanged.'
