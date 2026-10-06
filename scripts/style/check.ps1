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

function Write-InfrastructureOutput {
    param([bool] $Changed)
    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
        $value = if ($Changed) { 'true' } else { 'false' }
        [IO.File]::AppendAllText($env:GITHUB_OUTPUT, "infrastructureChanged=$value`n", [Text.UTF8Encoding]::new($false))
    }
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
$temporaryRoot = $null
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
            $headSha = [string]$env:GITHUB_SHA
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
    Write-InfrastructureOutput -Changed $infrastructureChanged

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
        $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("ygo-style-check-" + [Guid]::NewGuid().ToString('N'))
        Copy-StyleValidationTree -SourceRoot $RepositoryRoot -DestinationRoot $temporaryRoot
        $sources = @($relativePaths | ForEach-Object {
            Join-Path $temporaryRoot ($_.Replace('/', [IO.Path]::DirectorySeparatorChar))
        } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
        if ($sources.Count -eq 0) {
            Write-Host 'All changed C# sources were deleted; no formatter work is needed.'
        }
        else {
            Write-Host ("Verifying {0} C# source file(s){1}." -f $sources.Count, $(if ($scanFullRepository -or $infrastructureChanged) { ' across the repository' } else { ' changed by this event' }))

            $textIssues = [Collections.Generic.List[string]]::new()
            foreach ($source in $sources) {
                foreach ($issue in Get-StyleTextViolations -Path $source -Policy $stylePolicy) {
                    $relative = $source.Substring($temporaryRoot.Length).TrimStart([char[]]@('\', '/'))
                    $textIssues.Add("$relative`: $issue")
                }
            }
            if ($textIssues.Count -gt 0) {
                throw "C# text policy violations:`n$($textIssues -join "`n")`nRun pwsh -NoProfile -File scripts/style/fix.ps1, then re-run this check."
            }

            $temporarySolution = Join-Path $temporaryRoot 'YGOProbabilityCalculatorBlazor.sln'
            $temporaryLayoutProject = Join-Path $temporaryRoot 'scripts/style/InvocationLayout/InvocationLayout.csproj'
            $relativeSourcePaths = @($sources | ForEach-Object {
                $_.Substring($temporaryRoot.Length).TrimStart([char[]]@('\', '/')).Replace('\', '/')
            })
            $dotnetToolRestore = @('tool', 'restore')
            $dotnetSolutionRestore = @('restore', $temporarySolution, '--verbosity', 'quiet')

            Push-Location $temporaryRoot
            try {
                & dotnet @dotnetToolRestore
                if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed with exit code $LASTEXITCODE." }
                & dotnet @dotnetSolutionRestore
                if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

                $layoutArguments = @('run', '--project', $temporaryLayoutProject, '--no-launch-profile', '--', '--check') + $relativeSourcePaths
                & dotnet @layoutArguments
                if ($LASTEXITCODE -ne 0) {
                    throw 'Invocation layout verification failed. Run pwsh -NoProfile -File scripts/style/fix.ps1 and re-run this check.'
                }

                $formatArguments = @(
                    'format', 'style', $temporarySolution,
                    '--severity', 'info',
                    '--no-restore',
                    '--verify-no-changes',
                    '--diagnostics'
                ) + @($stylePolicy.DiagnosticIds) + @('--include') + $relativeSourcePaths
                & dotnet @formatArguments
                if ($LASTEXITCODE -ne 0) {
                    throw "Roslyn style diagnostics $($stylePolicy.DiagnosticIds -join ', ') would change one or more checked files:`n$($relativeSourcePaths -join "`n")`nRun pwsh -NoProfile -File scripts/style/fix.ps1, then re-run this check."
                }

                $checkoutHashes = @{}
                foreach ($relative in $relativeSourcePaths) {
                    $path = Join-Path $RepositoryRoot ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))
                    $checkoutHashes[$relative] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
                }
                $include = $relativeSourcePaths -join ';'
                $cleanupArguments = @(
                    'tool', 'run', 'jb', '--', 'cleanupcode', $temporarySolution,
                    '--settings=scripts/style/Cleanup.DotSettings',
                    '--profile=YGO Autofix',
                    "--include=$include",
                    '--disable-settings-layers=GlobalAll;GlobalPerProduct;SolutionPersonal;ProjectPersonal',
                    '--no-build', '--no-updates'
                )
                & dotnet @cleanupArguments
                if ($LASTEXITCODE -ne 0) { throw "ReSharper CleanupCode verification failed with exit code $LASTEXITCODE." }

                # CleanupCode normalizes indentation that the custom final layout pass owns.
                # Apply that pass only to the disposable copy before comparing it to the checkout.
                $layoutFixArguments = @('run', '--project', $temporaryLayoutProject, '--no-launch-profile', '--') + $relativeSourcePaths
                & dotnet @layoutFixArguments
                if ($LASTEXITCODE -ne 0) { throw "Invocation layout normalization failed with exit code $LASTEXITCODE." }

                $pipelineChanges = [Collections.Generic.List[string]]::new()
                foreach ($relative in $relativeSourcePaths) {
                    $path = Join-Path $temporaryRoot ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))
                    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
                    if ($hash -cne $checkoutHashes[$relative]) { $pipelineChanges.Add($relative) }
                }
                if ($pipelineChanges.Count -gt 0) {
                    throw "ReSharper profile 'YGO Autofix' plus InvocationLayout would change:`n$($pipelineChanges -join "`n")`nRun pwsh -NoProfile -File scripts/style/fix.ps1, then re-run this check."
                }
            }
            finally {
                Pop-Location
            }
        }
    }
}
catch {
    $failureMessage = $_.Exception.Message
}
finally {
    if ($temporaryRoot -and (Test-Path -LiteralPath $temporaryRoot)) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
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

if ($failureMessage) { throw $failureMessage }
Write-Host 'C# style verification passed; the checked-out worktree is unchanged.'
