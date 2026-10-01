# Codex local-environment setup (Windows / PowerShell)
# Add this file to the repository as scripts/codex/setup.ps1.
# Run it from Codex's Windows-specific setup script with:
# powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\setup.ps1

$ErrorActionPreference = 'Stop'

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git is not available. Install Git for Windows and restart Codex.'
}

& git --version
if ($LASTEXITCODE -ne 0) {
    throw 'Git is installed but could not be run. Check the Git for Windows installation and restart Codex.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot

$solution = 'YGOProbabilityCalculatorBlazor.sln'
if (-not (Test-Path $solution)) {
    throw "Expected $solution in the repository root: $(Get-Location)"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Warning '.NET SDK is not available on PATH. Continue workspace setup; the agent must bootstrap a nonprivileged .NET 10 SDK before restore/build/test.'
    Write-Host 'Codex worktree setup complete; dependency restore deferred until .NET 10 is bootstrapped.'
    exit 0
}

$sdks = @(& dotnet --list-sdks)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to query installed .NET SDKs using dotnet --list-sdks.'
}
if (-not ($sdks | Where-Object { $_ -match '^10\.' })) {
    $sdkSummary = if ($sdks.Count -gt 0) { $sdks -join ', ' } else { 'no SDKs were reported' }
    Write-Warning ".NET 10 SDK is not installed; found: $sdkSummary. Continue workspace setup; the agent must bootstrap a nonprivileged .NET 10 SDK before restore/build/test."
    Write-Host 'Codex worktree setup complete; dependency restore deferred until .NET 10 is bootstrapped.'
    exit 0
}

Write-Host 'Using .NET installation:'
& dotnet --info
if ($LASTEXITCODE -ne 0) { throw 'dotnet --info failed.' }

Write-Host "Restoring $solution ..."
& dotnet restore $solution
if ($LASTEXITCODE -ne 0) {
    throw 'NuGet restore failed. Resolve the reported error; for MSBuild node/socket issues, retry manually with -m:1.'
}

Write-Host 'Codex worktree setup complete: .NET 10 SDK verified and dependencies restored.'
