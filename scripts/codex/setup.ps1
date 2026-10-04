# Codex local-environment setup (Windows / PowerShell)
# Run it from Codex's Windows-specific setup script with:
# powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\setup.ps1

param(
    [string]$GitPublicName,
    [string]$GitPublicEmail
)

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

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    throw 'Node.js 18+ is required for the local publication guard. Install it and rerun project setup.'
}
$guardArguments = @((Join-Path $PSScriptRoot 'privacy-guard.mjs'), 'install')
if ($GitPublicName -or $GitPublicEmail) {
    if (-not $GitPublicName -or -not $GitPublicEmail) {
        throw 'Supply both approved public Git identity inputs; values are never inferred from account or machine names.'
    }
    $guardArguments += @('--name', $GitPublicName, '--email', $GitPublicEmail)
}
& node @guardArguments
if ($LASTEXITCODE -ne 0) { throw 'Project publication guard installation failed; resolve it before committing or pushing.' }

$solution = 'YGOProbabilityCalculatorBlazor.sln'
if (-not (Test-Path $solution)) {
    throw "Expected $solution in the repository root."
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET is not on PATH. Install the .NET 10 SDK, restart Codex, and retry: https://dotnet.microsoft.com/download/dotnet/10.0'
}

$sdks = @(& dotnet --list-sdks)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to query installed .NET SDKs using dotnet --list-sdks.'
}
if (-not ($sdks | Where-Object { $_ -match '^10\.' })) {
    throw ".NET 10 SDK is required; found: $($sdks -join ', '). Install it and restart Codex: https://dotnet.microsoft.com/download/dotnet/10.0"
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
